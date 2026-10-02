#!/usr/bin/env python3
"""Unit tests for scripts/jev_client.py (stdlib only, no network).

Every case injects its own transport, so the vendor is never contacted and no
real key is ever read: the key comes from an explicit `env` dict or a temp
`.env`, never from `os.environ`. The one live path — the smoke — is exercised
against a recorded response, not a socket.
"""
import contextlib
import io
import json
import os
import stat
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import jev_client  # noqa: E402

KEY = "sk-or-v1-TESTKEY-0123456789abcdef"
STATE_MARKER = "STATE-MARKER-must-never-be-logged"

# The response shape ADR-0238 recorded on 2026-09-26, with the chosen option
# deliberately NOT first in `probabilities`: a positional lookup would read
# WAIT_15 / 0.05 and drive the wrong macro.
RECORDED = {
    "model": "typesafe/jev-1.13-20260917",
    "answers": {
        "next_action": {
            "type": "choice",
            "choice": "JUMP_RIGHT",
            "probabilities": {
                "WAIT_15": 0.05,
                "LEFT_15": 0.1,
                "ATTACK": 0.05,
                "JUMP": 0.2,
                "JUMP_RIGHT": 0.35,
                "RIGHT_RUN_15": 0.15,
                "RIGHT_15": 0.1,
            },
            "confidence": 0.8,
        }
    },
    "usage": {"input_tokens": 547, "output_tokens": 86, "cost": 2.2974e-05},
    "id": "gen-req-0123456789",
    "provider": "TypeSafe",
}
COST = 2.2974e-05


def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def ok(msg):
    print(f"PASS: {msg}")


def _clock(*values):
    """A monotonic() replacement; the last value repeats forever."""
    seq = list(values)

    def clock():
        return seq.pop(0) if len(seq) > 1 else seq[0]

    return clock


def _transport(payload, *, status=200, calls=None, error=None):
    """A stub transport. Records the bodies it was handed."""

    def transport(api_key, body, timeout):
        if calls is not None:
            calls.append(body)
        if error is not None:
            raise error
        text = payload if isinstance(payload, str) else json.dumps(payload)
        return status, text

    return transport


def _client(tmp, transport, api_key=KEY, **kwargs):
    kwargs.setdefault("budget_usd", 1.0)
    kwargs.setdefault("log_path", Path(tmp) / "jev-decisions.jsonl")
    kwargs.setdefault("clock", _clock(100.0, 100.4))
    return jev_client.JevClient(api_key, transport=transport, **kwargs)


def _smoke_kwargs():
    return {
        "instructions": jev_client.SMOKE_INSTRUCTIONS,
        "criteria": jev_client.SMOKE_CRITERIA,
    }


def test_response_shape(tmp):
    calls = []
    client = _client(tmp, _transport(RECORDED, calls=calls))
    decision = client.ask(
        jev_client.SMOKE_QUESTION,
        state={"camera_x": 859, "ryu_x": 987},
        **_smoke_kwargs(),
    )
    if decision.choice != "JUMP_RIGHT":
        fail(f"the chosen option was not read from `choice`: {decision.choice!r}")
    # By name: `probabilities` key order is not stable.
    if decision.probabilities["JUMP_RIGHT"] != 0.35:
        fail(f"probabilities were not looked up by name: {decision.probabilities}")
    if list(decision.probabilities)[0] == "JUMP_RIGHT":
        fail("the fixture no longer puts the chosen option out of first place")
    if decision.probabilities[list(decision.probabilities)[0]] == 0.35:
        fail("a positional lookup would have agreed by accident; fixture is useless")
    if decision.confidence != 0.8:
        fail(f"confidence was not read: {decision.confidence!r}")
    if decision.model != "typesafe/jev-1.13-20260917":
        fail(f"the served snapshot was not read: {decision.model!r}")
    if decision.request_id != "gen-req-0123456789":
        fail(f"the request id was not read: {decision.request_id!r}")
    if decision.cost != COST:
        fail(f"the cost was not read: {decision.cost!r}")
    body = calls[0]
    if body["model"] != jev_client.MODEL:
        fail(f"the model is not pinned: {body['model']!r}")
    if body["state"] != {"camera_x": 859, "ryu_x": 987}:
        fail(f"the state did not travel verbatim: {body['state']!r}")
    question = body["questions"][jev_client.SMOKE_QUESTION]
    if question["type"] != "choice" or len(question["criteria"]) != 7:
        fail(f"the question is not a seven-option Choice: {question!r}")
    if question["criteria"] != jev_client.SMOKE_CRITERIA:
        fail("the criteria did not travel verbatim")
    ok("a recorded response yields choice, probabilities, confidence, snapshot, id, cost")


def test_key_required(tmp):
    empty = Path(tmp) / "empty"
    empty.mkdir()
    try:
        jev_client.load_api_key(env={}, repo_root=empty)
        fail("load_api_key invented a key")
    except jev_client.MissingKeyError as exc:
        if jev_client.API_KEY_ENV not in str(exc) or ".env" not in str(exc):
            fail(f"the missing-key error is not clear: {exc}")
    try:
        _client(tmp, _transport(RECORDED), api_key=None, env={}, repo_root=empty)
        fail("JevClient ran without a key")
    except jev_client.MissingKeyError:
        pass
    calls = []
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        code = jev_client.main(
            ["--smoke", "--log", str(Path(tmp) / "l.jsonl")],
            transport=_transport(RECORDED, calls=calls),
            env={},
            repo_root=empty,
        )
    if code != 2:
        fail(f"a missing key should exit 2, got {code}")
    if jev_client.API_KEY_ENV not in err.getvalue():
        fail(f"the refusal does not name the variable: {err.getvalue()!r}")
    if calls:
        fail("a call went out without a key")
    ok("no key: load_api_key, JevClient and main all refuse, and nothing is sent")


def test_env_file(tmp):
    with tempfile.TemporaryDirectory() as tmp2:
        root = Path(tmp2)
        (root / ".env").write_text(
            "# a comment\n"
            "\n"
            "export GITHUB_TOKEN=ghp_not_ours\n"
            f"{jev_client.API_KEY_ENV}='{KEY}'\n",
            encoding="utf-8",
        )
        if jev_client.load_api_key(env={}, repo_root=root) != KEY:
            fail("a quoted .env entry was not read")
        if jev_client.load_api_key(env={jev_client.API_KEY_ENV: "from-env"}, repo_root=root) != "from-env":
            fail("the environment did not win over .env")
        if jev_client.read_env_file(root / ".env")["GITHUB_TOKEN"] != "ghp_not_ours":
            fail("an `export ` prefix or a plain entry was dropped")
        if jev_client.load_api_key(env={"OPENROUTER_API_KEY": "  padded  "}) != "padded":
            fail("a padded environment value was not stripped")
    ok(".env is read (quotes, comments, `export`), the environment wins, values are stripped")


def test_budget_cap(tmp):
    calls = []
    client = _client(tmp, _transport(RECORDED, calls=calls), budget_usd=COST)
    client.ask("next_action", state={"a": 1}, **_smoke_kwargs())
    if len(calls) != 1:
        fail(f"the first call should have gone out: {len(calls)}")
    try:
        client.ask("next_action", state={"a": 1}, **_smoke_kwargs())
        fail("the cap did not stop the second call")
    except jev_client.BudgetExceededError as exc:
        if "budget reached" not in str(exc):
            fail(f"the refusal is not clear: {exc}")
        if f"{COST:.6f}" not in str(exc):
            fail(f"the refusal does not report what was spent: {exc}")
    if len(calls) != 1:
        fail("the refused call still reached the transport")
    if client.remaining_usd != 0.0 or client.calls != 1:
        fail(f"budget bookkeeping is wrong: {client.remaining_usd} {client.calls}")

    calls.clear()
    zero = _client(tmp, _transport(RECORDED, calls=calls), budget_usd=0.0)
    try:
        zero.ask("next_action", state={"a": 1}, **_smoke_kwargs())
        fail("a zero budget still called out")
    except jev_client.BudgetExceededError:
        pass
    if calls:
        fail("a zero budget sent a request")
    ok("the run stops at the cap, before the next request leaves")


def test_no_key_leak(tmp):
    log = Path(tmp) / "logged.jsonl"
    state = {"camera_x": 859, "marker": STATE_MARKER}
    client = _client(tmp, _transport(RECORDED), log_path=log, budget_usd=1.0)
    client.ask("next_action", state=state, **_smoke_kwargs())
    text = log.read_text(encoding="utf-8")
    record = json.loads(text.strip().splitlines()[-1])
    if KEY in text or STATE_MARKER in text:
        fail("the log carries the key or the state")
    for field in ("model", "request_id", "choice", "probabilities", "confidence", "cost"):
        if field not in record:
            fail(f"the per-decision log drops {field!r}: {record}")
    if record["request_id"] != "gen-req-0123456789" or record["choice"] != "JUMP_RIGHT":
        fail(f"the logged provenance is wrong: {record}")
    if not isinstance(record["latency_s"], float):
        fail(f"latency is not logged: {record}")

    # The key travels in a 0600 file handed to `curl -H @file`, and never in argv.
    argv_used = []
    input_used = []

    class Run:
        def __call__(self, argv, **kwargs):
            argv_used.extend(argv)
            input_used.append(kwargs.get("input"))
            header = argv[[i for i, a in enumerate(argv) if a == "--header"][-1] + 1]
            header_path = header.lstrip("@")
            Run.header_path = header_path
            Run.header_mode = stat.S_IMODE(os.stat(header_path).st_mode)
            Run.header_text = Path(header_path).read_text(encoding="utf-8")
            output = argv[argv.index("--output") + 1]
            Path(output).write_text(json.dumps(RECORDED), encoding="utf-8")
            return subprocess_result("200")

    run = Run()
    status, text = jev_client.curl_post(KEY, {"state": {"marker": STATE_MARKER}}, 5.0, run=run)
    if status != 200 or "JUMP_RIGHT" not in text:
        fail(f"curl_post did not pass the exchange through: {status} {text!r}")
    joined = " ".join(argv_used)
    if KEY in joined or STATE_MARKER in joined:
        fail("the key or the state reached a command line")
    if "--header" not in argv_used:
        fail("the authorization header did not go through curl's header-file form")
    if run.header_text != f"Authorization: Bearer {KEY}\n":
        fail("the header file does not carry the authorization line")
    if run.header_mode != 0o600:
        fail(f"the header file is not 0600: {oct(run.header_mode)}")
    if Path(run.header_path).exists():
        fail("the header file survived the call")
    if KEY in (input_used[0] or ""):
        fail("the key reached the request body on stdin")

    # A transport that echoes the key in its own diagnostic must not get it out.
    try:
        _client(
            tmp, _transport(None, error=jev_client.TransportError(f"boom {KEY}"))
        ).ask("next_action", state={"a": 1}, **_smoke_kwargs())
        fail("the transport error did not propagate")
    except jev_client.TransportError as exc:
        if KEY in str(exc) or jev_client.REDACTED not in str(exc):
            fail(f"the key survived in an exception: {exc}")
    try:
        _client(tmp, _transport(f'{{"error": "denied {KEY}"}}', status=402)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
        fail("the HTTP error did not propagate")
    except jev_client.HttpError as exc:
        if KEY in str(exc) or KEY in (exc.body or ""):
            fail(f"the key survived in an HTTP error: {exc} / {exc.body!r}")
    ok("the key stays out of the log, argv, stdin, and every exception")


def subprocess_result(stdout, returncode=0, stderr=""):
    class Completed:
        pass

    completed = Completed()
    completed.stdout = stdout
    completed.stderr = stderr
    completed.returncode = returncode
    return completed


def test_http_errors(tmp):
    for status in (402, 429, 500, 503):
        client = _client(tmp, _transport('{"error": {"message": "nope"}}', status=status))
        try:
            client.ask("next_action", state={"a": 1}, **_smoke_kwargs())
            fail(f"HTTP {status} did not raise")
        except jev_client.HttpError as exc:
            if exc.status != status or str(status) not in str(exc):
                fail(f"HTTP {status} is not reported: {exc}")
            if exc.retryable != (status in (429, 500, 503)):
                fail(f"retryable is wrong for HTTP {status}: {exc.retryable}")
    try:
        _client(tmp, _transport("", status=200)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
        fail("an empty 200 body did not raise")
    except jev_client.TransportError:
        pass
    try:
        _client(tmp, _transport("not json", status=200)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
        fail("a non-JSON 200 body did not raise")
    except jev_client.TransportError:
        pass
    ok("402/429/5xx surface as a typed HttpError with its status; a broken 200 does not pass")


def test_bad_answers(tmp):
    def with_answer(**overrides):
        response = json.loads(json.dumps(RECORDED))
        response["answers"]["next_action"].update(overrides)
        return response

    def without_probability(option):
        response = json.loads(json.dumps(RECORDED))
        response["answers"]["next_action"]["probabilities"].pop(option)
        return response

    cases = [
        ("an option we never offered", with_answer(choice="TELEPORT")),
        ("a non-choice type", with_answer(type="text")),
        ("no probabilities", with_answer(probabilities=None)),
        # Offered in `criteria`, absent from the distribution: the by-name
        # lookup must fail loudly rather than read a neighbour's number.
        ("the choice missing from its own probabilities", without_probability("JUMP_RIGHT")),
        ("a non-numeric probability", with_answer(probabilities={"JUMP_RIGHT": "0.35"})),
        ("no confidence", with_answer(confidence=None)),
    ]
    for label, response in cases:
        try:
            _client(tmp, _transport(response)).ask(
                "next_action", state={"a": 1}, **_smoke_kwargs()
            )
            fail(f"{label} was accepted")
        except jev_client.AnswerError:
            pass
    no_answers = {"model": RECORDED["model"], "usage": RECORDED["usage"]}
    try:
        _client(tmp, _transport(no_answers)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
        fail("a response with no `answers` was accepted")
    except jev_client.AnswerError:
        pass
    no_snapshot = json.loads(json.dumps(RECORDED))
    no_snapshot.pop("model")
    try:
        _client(tmp, _transport(no_snapshot)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
        fail("a response with no served snapshot was accepted")
    except jev_client.AnswerError:
        pass
    # A missing cost is 0.0, not a guess: the log shows what the vendor reported.
    no_cost = json.loads(json.dumps(RECORDED))
    no_cost["usage"] = {"input_tokens": 547}
    client = _client(tmp, _transport(no_cost))
    decision = client.ask("next_action", state={"a": 1}, **_smoke_kwargs())
    if decision.cost != 0.0 or client.spent_usd != 0.0:
        fail(f"a missing cost was invented: {decision.cost} {client.spent_usd}")
    # An older snapshot than the pinned model is logged, and said out loud.
    other = json.loads(json.dumps(RECORDED))
    other["model"] = "typesafe/jev-1.14-20261001"
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        decision = _client(tmp, _transport(other)).ask(
            "next_action", state={"a": 1}, **_smoke_kwargs()
        )
    if decision.model != "typesafe/jev-1.14-20261001":
        fail("the served snapshot was not kept as it came")
    if "typesafe/jev-1.14-20261001" not in err.getvalue():
        fail(f"an unexpected served model was not reported: {err.getvalue()!r}")
    ok("every answer we will not act on is refused, and provenance is kept as it came")


def test_bad_requests(tmp):
    client = _client(tmp, _transport(RECORDED))
    for label, kwargs in [
        ("a list state", {"state": [1, 2]}),
        ("a state that is not JSON", {"state": {"x": {1, 2}}}),
    ]:
        try:
            client.ask("next_action", state=kwargs["state"], **_smoke_kwargs())
            fail(f"{label} was accepted")
        except jev_client.RequestError as exc:
            if str(kwargs["state"]) in str(exc):
                fail(f"the refusal echoes the state: {exc}")
    for label, kwargs in [
        ("no instructions", {"state": {"a": 1}, "instructions": " ", "criteria": {"A": "a"}}),
        ("no criteria", {"state": {"a": 1}, "instructions": "i", "criteria": {}}),
        ("a criterion with no description", {"state": {"a": 1}, "instructions": "i", "criteria": {"A": " "}}),
    ]:
        try:
            client.ask("next_action", **kwargs)
            fail(f"{label} was accepted")
        except jev_client.RequestError:
            pass
    ok("a malformed request is refused before any call, without echoing the state")


def test_smoke_and_exit_codes(tmp):
    calls = []
    log = Path(tmp) / "smoke.jsonl"
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = jev_client.main(
            ["--smoke", "--log", str(log), "--budget", "0.01"],
            transport=_transport(RECORDED, calls=calls),
            env={jev_client.API_KEY_ENV: KEY},
        )
    if code != 0:
        fail(f"the smoke exited {code}")
    body = calls[0]
    question = body["questions"][jev_client.SMOKE_QUESTION]
    if sorted(question["criteria"]) != sorted(jev_client.SMOKE_CRITERIA):
        fail(f"the smoke did not offer the seven macros: {sorted(question['criteria'])}")
    if len(question["criteria"]) != 7 or sorted(question["criteria"]) != [
        "ATTACK", "JUMP", "JUMP_RIGHT", "LEFT_15", "RIGHT_15", "RIGHT_RUN_15", "WAIT_15",
    ]:
        fail(f"the smoke's macro set is wrong: {sorted(question['criteria'])}")
    if body["state"] != jev_client.SMOKE_STATE:
        fail("the smoke did not send its state")
    printed = json.loads(out.getvalue().strip())
    if printed["choice"] != "JUMP_RIGHT" or printed["model"] != RECORDED["model"]:
        fail(f"the smoke line is wrong: {printed}")
    if KEY in out.getvalue():
        fail("the smoke printed the key")
    if printed["cost"] != COST or "latency_s" not in printed:
        fail(f"the smoke did not report cost and latency: {printed}")

    # 5: the cap is already spent.
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        code = jev_client.main(
            ["--smoke", "--log", str(log), "--budget", "0"],
            transport=_transport(RECORDED),
            env={jev_client.API_KEY_ENV: KEY},
        )
    if code != 5:
        fail(f"a spent budget should exit 5, got {code}")
    # 3: the vendor refused.
    with contextlib.redirect_stderr(io.StringIO()):
        code = jev_client.main(
            ["--smoke", "--log", str(log)],
            transport=_transport('{"error": "denied"}', status=402),
            env={jev_client.API_KEY_ENV: KEY},
        )
    if code != 3:
        fail(f"an HTTP failure should exit 3, got {code}")
    # 4: an answer we will not act on.
    with contextlib.redirect_stderr(io.StringIO()):
        code = jev_client.main(
            ["--smoke", "--log", str(log)],
            transport=_transport({"model": RECORDED["model"], "answers": {}}),
            env={jev_client.API_KEY_ENV: KEY},
        )
    if code != 4:
        fail(f"a refused answer should exit 4, got {code}")
    # 2: a state file that is not there.
    with contextlib.redirect_stderr(io.StringIO()):
        code = jev_client.main(
            ["--question", "q", "--instructions", "i", "--option", "A=a",
             "--state", str(Path(tmp) / "nope.json"), "--log", str(log)],
            transport=_transport(RECORDED),
            env={jev_client.API_KEY_ENV: KEY},
        )
    if code != 2:
        fail(f"an unreadable state should exit 2, got {code}")
    ok("the smoke sends the seven macros and reports choice, cost and latency; exit codes hold")


def main():
    if not jev_client.SMOKE_CRITERIA:
        fail("the smoke has no macros")
    for test in (
        test_response_shape,
        test_key_required,
        test_env_file,
        test_budget_cap,
        test_no_key_leak,
        test_http_errors,
        test_bad_answers,
        test_bad_requests,
        test_smoke_and_exit_codes,
    ):
        with tempfile.TemporaryDirectory() as tmp:
            test(tmp)
    return 0


if __name__ == "__main__":
    sys.exit(main())
