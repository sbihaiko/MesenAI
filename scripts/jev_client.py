#!/usr/bin/env python3
"""Ask Jev (TypeSafe's System One) for one Choice, through OpenRouter (ADR-0238 §3).

The client half of F14.14. It builds the `alpha/decisions` request body, sends
it with `curl`, and returns the answer with its provenance: the served model
snapshot, the request id and the cost. It never loads a ROM, never sees a
pixel and never decides anything itself — the step-mode emulator (F14.12) owns
the state, and the committed route is a plain input script either way.

Wire contract (measured 2026-09-26, ADR-0238):

  POST https://openrouter.ai/api/alpha/decisions
  {"model": "typesafe/jev-1.13", "state": {...RAM-derived numbers...},
   "questions": {"next_action": {"type": "choice",
     "instructions": "...", "criteria": {"JUMP_RIGHT": "...", ...}}}}

  -> {"model": "typesafe/jev-1.13-20260917",
      "answers": {"next_action": {"type": "choice", "choice": "JUMP_RIGHT",
        "probabilities": {...}, "confidence": 0.8}},
      "usage": {"input_tokens": 547, "output_tokens": 86, "cost": 2.2974e-05},
      "id": "...", "provider": "TypeSafe"}

Options are looked up **by name**: `probabilities` key order is not stable.
The chosen option must be one of the criteria the caller offered — a name we
did not offer is an error, never something a harness drives a pad with.

The key comes from `OPENROUTER_API_KEY` or the repo-root `.env` (gitignored),
and it is never printed, logged, put on a command line or left in an
exception: it travels in a 0600 temp file handed to `curl -H @file`, unlinked
in a `finally`, and `JevError.redacted` scrubs it from any error that could
have picked it up on the way out.

Budget: `JevClient` sums each response's `usage.cost` and refuses the next
call once `budget_usd` (default US$ 1.00) is reached. Every decision appends
one JSONL line to `runs/` — snapshot, request id, choice, probabilities,
confidence, cost, latency — never the state and never the key.

Connection reuse: one `curl` process per request, so there is no connection to
keep across calls; the one reuse the endpoint itself offers is its `questions`
map, several questions over one connection, which `ask_many` uses. Per-call
latency is the vendor's (0.32–0.46 s warm, 0.63–0.79 s cold, ADR-0238) and
`curl`'s process start is ~10 ms of it.

Usage:
  python3 scripts/jev_client.py --smoke [--budget 0.01] [--log PATH]
  python3 scripts/jev_client.py --question next_action --instructions ... \
      --option JUMP_RIGHT="..." --state state.json

Exit codes: 0 success, 2 usage, missing key or a bad request, 3 HTTP or
transport failure, 4 an answer we refuse, 5 the budget is already spent.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import NamedTuple

ENDPOINT = "https://openrouter.ai/api/alpha/decisions"
MODEL = "typesafe/jev-1.13"
API_KEY_ENV = "OPENROUTER_API_KEY"
DEFAULT_BUDGET_USD = 1.00
DEFAULT_TIMEOUT = 60.0
CURL = "curl"
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOG = ROOT / "runs" / "jev-decisions.jsonl"
# Anything echoed back from the vendor is truncated before it reaches a
# message or a log: we do not print a response body in full.
DIAGNOSTIC_CHARS = 400
REDACTED = "<redacted>"


class JevError(RuntimeError):
    """A failed decision, carrying the HTTP status and body when there was one."""

    def __init__(self, message, *, status=None, body=None):
        super().__init__(message)
        self.status = status
        self.body = body

    def redacted(self, secret):
        """A copy with `secret` removed from the message and the body.

        `JevClient` scrubs every error on the way out, so a transport that
        echoed the key in a diagnostic cannot land it in a caller's log or
        traceback. The caller re-raises with `from None` for the same reason:
        an exception chain would print the original, unscrubbed text again.
        """
        if not secret or (secret not in str(self) and secret not in (self.body or "")):
            return self
        return type(self)(
            str(self).replace(secret, REDACTED),
            status=self.status,
            body=(self.body.replace(secret, REDACTED) if self.body else self.body),
        )


class MissingKeyError(JevError):
    """No key: the harness refuses to run rather than call Jev unauthenticated."""


class BudgetExceededError(JevError):
    """The per-run cap is already spent."""


class TransportError(JevError):
    """`curl` itself failed: no HTTP exchange happened."""


class HttpError(JevError):
    """The vendor answered a non-2xx status."""

    @property
    def retryable(self) -> bool:
        """429 and the 5xx family are worth another try; a 402 is not (buy credit)."""
        return self.status in (429, 500, 502, 503, 504)


class AnswerError(JevError):
    """A 2xx answer we refuse to act on (wrong type, unknown option, no confidence)."""


class RequestError(JevError):
    """The caller's own request is malformed — no HTTP call is made."""


class Decision(NamedTuple):
    """One Choice: the option, its distribution, and the call's provenance."""

    choice: str
    probabilities: dict
    confidence: float
    model: str
    request_id: str
    cost: float


def redact(text: str, secret: str) -> str:
    """`text` with `secret` removed. The one place a key could leak through."""
    if not secret:
        return text
    return text.replace(secret, REDACTED)


def read_env_file(path) -> dict:
    """`KEY=value` pairs from a `.env`: comments, blanks and quotes tolerated."""
    values = {}
    try:
        text = Path(path).read_text(encoding="utf-8")
    except OSError:
        return values
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("export "):
            line = line[len("export ") :].lstrip()
        name, sep, value = line.partition("=")
        if not sep:
            continue
        name = name.strip()
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        if name:
            values[name] = value
    return values


def load_api_key(env=None, repo_root=None) -> str:
    """The OpenRouter key: the environment wins over the repo-root `.env`.

    Raises `MissingKeyError`, whose message names the variable and the file but
    never a value.
    """
    env = os.environ if env is None else env
    key = (env.get(API_KEY_ENV) or "").strip()
    if key:
        return key
    root = Path(repo_root) if repo_root else ROOT
    key = (read_env_file(root / ".env").get(API_KEY_ENV) or "").strip()
    if key:
        return key
    raise MissingKeyError(
        f"{API_KEY_ENV} is not set: export it, or put "
        f"`{API_KEY_ENV}=...` in {root / '.env'}"
    )


def _write_private(text: str) -> str:
    """A 0600 temp file holding `text`; the caller unlinks it."""
    fd, path = tempfile.mkstemp(prefix="jev-header-")
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(text)
    return path


def _unlink(path) -> None:
    try:
        os.unlink(path)
    except OSError:
        pass


def curl_post(api_key, body, timeout, *, curl=CURL, run=subprocess.run):
    """One POST through `curl`. Returns `(status, response_text)`.

    The key never reaches `argv`: it is written to a 0600 temp file handed to
    `curl -H @file` and unlinked in a `finally`. The request body goes in on
    stdin for the same reason — it carries the state, which has no business in
    a process listing either.
    """
    payload = json.dumps(body)
    header_path = _write_private(f"Authorization: Bearer {api_key}\n")
    fd, body_path = tempfile.mkstemp(prefix="jev-response-")
    os.close(fd)
    try:
        argv = [
            curl,
            "--silent",
            "--show-error",
            "--request",
            "POST",
            ENDPOINT,
            "--header",
            "Content-Type: application/json",
            "--header",
            f"@{header_path}",
            "--data-binary",
            "@-",
            "--output",
            body_path,
            "--write-out",
            "%{http_code}",
            "--max-time",
            f"{timeout:g}",
        ]
        try:
            completed = run(
                argv,
                input=payload,
                capture_output=True,
                text=True,
                timeout=timeout + 10.0,
            )
        except subprocess.TimeoutExpired:
            raise TransportError(f"curl gave up after {timeout:g}s")
        if completed.returncode != 0:
            raise TransportError(
                f"curl exited {completed.returncode}: "
                f"{(completed.stderr or '').strip()[:DIAGNOSTIC_CHARS]}"
            )
        status = int((completed.stdout or "").strip() or "0")
        if not status:
            raise TransportError("curl reported no HTTP status")
        try:
            text = Path(body_path).read_text(encoding="utf-8", errors="replace")
        except OSError as exc:
            raise TransportError(f"could not read curl's response body: {exc}")
        return status, text
    finally:
        _unlink(header_path)
        _unlink(body_path)


def build_body(model: str, state, questions: dict) -> dict:
    """The `alpha/decisions` request body, with the caller's mistakes refused here.

    Every message names a type and never the state itself: the state is
    RAM-derived and is not to be echoed into a log or an exception.
    """
    if not isinstance(state, dict):
        raise RequestError(f"state must be a JSON object, got {type(state).__name__}")
    try:
        json.dumps(state)
    except (TypeError, ValueError) as exc:
        raise RequestError(
            f"state is not JSON-serializable ({type(exc).__name__}); "
            "it must stay RAM-derived numbers"
        )
    if not questions:
        raise RequestError("at least one question is required")
    spec = {}
    for name, question in questions.items():
        if not name or not isinstance(name, str):
            raise RequestError("a question key must be a non-empty string")
        if not isinstance(question, dict):
            raise RequestError(f"question {name!r} must be a mapping")
        instructions = question.get("instructions") or ""
        if not isinstance(instructions, str) or not instructions.strip():
            raise RequestError(f"question {name!r} carries no instructions")
        criteria = question.get("criteria") or {}
        if not isinstance(criteria, dict) or not criteria:
            raise RequestError(f"question {name!r} offers no options")
        for option, description in criteria.items():
            if not isinstance(option, str) or not option:
                raise RequestError(f"question {name!r} has an empty option name")
            if not isinstance(description, str) or not description.strip():
                raise RequestError(f"option {option!r} carries no description")
        spec[name] = {
            "type": "choice",
            "instructions": instructions.strip(),
            "criteria": dict(criteria),
        }
    return {"model": model, "state": state, "questions": spec}


def parse_response(text: str) -> dict:
    """The vendor's 2xx body as a JSON object."""
    try:
        response = json.loads(text)
    except json.JSONDecodeError as exc:
        raise TransportError(f"Jev's response is not JSON: {exc}")
    if not isinstance(response, dict):
        raise TransportError("Jev's response is not a JSON object")
    return response


def _number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def answer_of(response: dict, name: str, criteria: dict):
    """`(choice, probabilities, confidence)` for one question, or `AnswerError`."""
    answers = response.get("answers")
    if not isinstance(answers, dict):
        raise AnswerError("Jev's response carries no `answers` object")
    answer = answers.get(name)
    if not isinstance(answer, dict):
        raise AnswerError(f"Jev's response carries no answer for question {name!r}")
    kind = answer.get("type")
    if kind != "choice":
        raise AnswerError(f"the answer for {name!r} has type {kind!r}, not 'choice'")
    # By name, never by position: `probabilities` key order is not stable.
    choice = answer.get("choice")
    if not isinstance(choice, str) or choice not in criteria:
        raise AnswerError(
            f"the answer for {name!r} chose {choice!r}, which is not one of the "
            "options this run offered"
        )
    probabilities = answer.get("probabilities")
    if not isinstance(probabilities, dict) or not probabilities:
        raise AnswerError(f"the answer for {name!r} carries no probabilities")
    for option, value in probabilities.items():
        if not _number(value):
            raise AnswerError(f"the probability for {option!r} is not a number")
    if choice not in probabilities:
        raise AnswerError(
            f"the probabilities for {name!r} do not name the chosen option {choice!r}"
        )
    confidence = answer.get("confidence")
    if not _number(confidence):
        raise AnswerError(f"the answer for {name!r} carries no numeric confidence")
    return choice, dict(probabilities), float(confidence)


def cost_of(response: dict) -> float:
    """`usage.cost` in US$, 0.0 when the vendor does not report one.

    Documented rather than guessed: the budget cap can only sum what the
    response actually carries, and a missing cost is a contract change worth
    seeing in the log, not a reason to refuse the run.
    """
    usage = response.get("usage")
    if not isinstance(usage, dict):
        return 0.0
    cost = usage.get("cost")
    return float(cost) if _number(cost) else 0.0


class JevClient:
    """One run's worth of Jev calls, with a hard cap and a per-decision log."""

    def __init__(
        self,
        api_key=None,
        *,
        env=None,
        repo_root=None,
        budget_usd=DEFAULT_BUDGET_USD,
        log_path=DEFAULT_LOG,
        transport=None,
        timeout=DEFAULT_TIMEOUT,
        clock=time.monotonic,
    ):
        self._api_key = (
            api_key if api_key is not None else load_api_key(env=env, repo_root=repo_root)
        )
        if not self._api_key.strip():
            raise MissingKeyError(f"{API_KEY_ENV} is empty; refusing to call Jev without a key")
        self.budget_usd = float(budget_usd)
        self.log_path = Path(log_path) if log_path else None
        self._transport = transport or curl_post
        self._timeout = float(timeout)
        self._clock = clock
        self.spent_usd = 0.0
        self.calls = 0
        self.last_latency_s = 0.0

    @property
    def remaining_usd(self) -> float:
        return self.budget_usd - self.spent_usd

    def ask(self, question, *, state, instructions, criteria, model=MODEL) -> Decision:
        """One Choice over one closed set of options. Returns a `Decision`."""
        return self.ask_many(
            {question: {"instructions": instructions, "criteria": criteria}},
            state=state,
            model=model,
        )[question]

    def ask_many(self, questions, *, state, model=MODEL) -> dict:
        """Ask several Choices in one request, returning `{question: Decision}`.

        One request is one connection, which is the only connection reuse
        available to a client that starts a `curl` per call. It buys nothing
        measurable for one question (the vendor's own latency dominates) and
        everything for a batch, so a caller with several questions at the same
        state should prefer this to a loop over `ask`.
        """
        if self.spent_usd >= self.budget_usd:
            raise BudgetExceededError(
                f"budget reached: spent US$ {self.spent_usd:.6f} of "
                f"US$ {self.budget_usd:.2f}; refusing the next Jev call"
            )
        body = build_body(model, state, questions)
        try:
            started = self._clock()
            status, text = self._transport(self._api_key, body, self._timeout)
            if not 200 <= status < 300:
                raise HttpError(
                    f"Jev answered HTTP {status}: "
                    f"{text.strip()[:DIAGNOSTIC_CHARS]}",
                    status=status,
                    body=text[:DIAGNOSTIC_CHARS],
                )
            self.last_latency_s = self._clock() - started
            self.calls += 1
            response = parse_response(text)
        except JevError as exc:
            # One scrub, at the one door every failure leaves by, and with the
            # original dropped: a chain would print the unscrubbed text again.
            raise exc.redacted(self._api_key) from None
        except OSError as exc:
            raise TransportError(
                redact(f"transport failed: {type(exc).__name__}: {exc}", self._api_key)
            ) from None
        served = response.get("model")
        if not isinstance(served, str) or not served:
            raise AnswerError("Jev's response carries no served model snapshot")
        if not served.startswith(model):
            print(
                f"jev_client: served model {served!r} is not the pinned {model!r}",
                file=sys.stderr,
            )
        request_id = response.get("id")
        if not isinstance(request_id, str):
            request_id = ""
        cost = cost_of(response)
        # Counted before the answers are read: a 2xx that we then refuse was
        # still bought, and the cap has to see that.
        self.spent_usd += cost
        try:
            decisions = {}
            for name, question in questions.items():
                choice, probabilities, confidence = answer_of(
                    response, name, question["criteria"]
                )
                decisions[name] = Decision(
                    choice=choice,
                    probabilities=probabilities,
                    confidence=confidence,
                    model=served,
                    request_id=request_id,
                    cost=cost,
                )
        except JevError as exc:
            raise exc.redacted(self._api_key) from None
        for name, decision in decisions.items():
            self._record(name, decision)
        return decisions

    def _record(self, question: str, decision: Decision) -> None:
        """One JSONL line per decision. Never the state, never the key."""
        if self.log_path is None:
            return
        record = {
            "ts": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "question": question,
            "choice": decision.choice,
            "probabilities": decision.probabilities,
            "confidence": decision.confidence,
            "model": decision.model,
            "request_id": decision.request_id,
            "cost": decision.cost,
            "spent_usd": self.spent_usd,
            "budget_usd": self.budget_usd,
            "latency_s": self.last_latency_s,
        }
        line = redact(
            json.dumps(record, sort_keys=True, ensure_ascii=False), self._api_key
        )
        self.log_path.parent.mkdir(parents=True, exist_ok=True)
        with self.log_path.open("a", encoding="utf-8") as handle:
            handle.write(line + "\n")


# --- the smoke -----------------------------------------------------------

SMOKE_QUESTION = "next_action"
SMOKE_INSTRUCTIONS = (
    "The run has made no progress for several emulated seconds: the same "
    "position, the same camera. Choose the one macro to play next."
)
# The seven macros ADR-0238 names. Each duration is fixed here, in code: the
# model picks a name, never a length.
SMOKE_CRITERIA = {
    "RIGHT_15": "Hold Right for 15 frames.",
    "RIGHT_RUN_15": "Hold Right and B (run) for 15 frames.",
    "JUMP_RIGHT": "Jump forward: hold Right and A (jump) for 15 frames.",
    "JUMP": "Jump in place: hold A for 15 frames.",
    "ATTACK": "Attack without moving: press B for 15 frames.",
    "LEFT_15": "Hold Left for 15 frames.",
    "WAIT_15": "Press nothing for 15 frames.",
}
# A hand-made state, RAM-derived numbers only, standing at Ninja Gaiden's Act
# 1-1 pin (x 987 = camera 859 + Ryu's screen x 128). It exists to prove the
# wire, not to drive a recording — the emulator owns the real one (F14.12).
SMOKE_STATE = {
    "game": "ninja-gaiden",
    "stage": "1-1",
    "frame": 688,
    "camera_x": 859,
    "ryu_x": 987,
    "hp": 16,
    "stuck_seconds": 5,
}


def smoke_questions() -> dict:
    return {
        SMOKE_QUESTION: {
            "instructions": SMOKE_INSTRUCTIONS,
            "criteria": SMOKE_CRITERIA,
        }
    }


def _parse_options(pairs, parser) -> dict:
    criteria = {}
    for pair in pairs or []:
        name, sep, description = pair.partition("=")
        if not sep or not name.strip() or not description.strip():
            parser.error(f"--option expects NAME=DESCRIPTION, got {pair!r}")
        criteria[name.strip()] = description.strip()
    return criteria


def main(argv=None, *, transport=None, env=None, repo_root=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--smoke",
        action="store_true",
        help="one decision over the seven fixed macros of ADR-0238 (F14.14)",
    )
    parser.add_argument("--question", default=SMOKE_QUESTION)
    parser.add_argument("--instructions", default=None)
    parser.add_argument(
        "--option", action="append", default=None, metavar="NAME=DESCRIPTION"
    )
    parser.add_argument("--state", default=None, help="a JSON file, or '-' for stdin")
    parser.add_argument("--model", default=MODEL)
    parser.add_argument("--budget", type=float, default=DEFAULT_BUDGET_USD)
    parser.add_argument("--log", default=str(DEFAULT_LOG))
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT)
    args = parser.parse_args(argv)

    try:
        api_key = load_api_key(env=env, repo_root=repo_root)
    except MissingKeyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    if args.smoke:
        state = SMOKE_STATE
        questions = smoke_questions()
    else:
        if args.state is None:
            parser.error("--state is required unless --smoke is used")
        if args.instructions is None:
            parser.error("--instructions is required unless --smoke is used")
        criteria = _parse_options(args.option, parser)
        if not criteria:
            parser.error("--option is required unless --smoke is used")
        questions = {
            args.question: {"instructions": args.instructions, "criteria": criteria}
        }
        try:
            state = (
                json.loads(sys.stdin.read())
                if args.state == "-"
                else json.loads(Path(args.state).read_text(encoding="utf-8"))
            )
        except (OSError, json.JSONDecodeError) as exc:
            print(f"error: could not read the state: {exc}", file=sys.stderr)
            return 2

    client = JevClient(
        api_key,
        budget_usd=args.budget,
        log_path=args.log,
        transport=transport,
        timeout=args.timeout,
    )
    try:
        decisions = client.ask_many(questions, state=state, model=args.model)
    except BudgetExceededError as exc:
        print(f"error: {redact(str(exc), api_key)}", file=sys.stderr)
        return 5
    except HttpError as exc:
        print(f"error: {redact(str(exc), api_key)}", file=sys.stderr)
        return 3
    except TransportError as exc:
        print(f"error: {redact(str(exc), api_key)}", file=sys.stderr)
        return 3
    except RequestError as exc:
        print(f"error: {redact(str(exc), api_key)}", file=sys.stderr)
        return 2
    except AnswerError as exc:
        print(f"error: {redact(str(exc), api_key)}", file=sys.stderr)
        return 4

    for name, decision in decisions.items():
        line = json.dumps(
            {
                "question": name,
                "choice": decision.choice,
                "probabilities": decision.probabilities,
                "confidence": decision.confidence,
                "model": decision.model,
                "request_id": decision.request_id,
                "cost": decision.cost,
                "latency_s": client.last_latency_s,
            },
            sort_keys=True,
            ensure_ascii=False,
        )
        print(redact(line, api_key))
    return 0


if __name__ == "__main__":
    sys.exit(main())
