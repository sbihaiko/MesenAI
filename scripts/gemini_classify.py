#!/usr/bin/env python3
"""Call the Gemini API to classify one community pack (ADR-0199).

Single responsibility: take the already-rendered classify prompt and its
JSON schema — both rendered from `.github/ai/validate-classify.md`, the
single source shared with the local harness — send them to the Gemini API,
and print the model's JSON object on stdout.

Replaces the `anthropics/claude-code-action` step the CI used until
2026-09-16. That action was only ever a schema-constrained text generator
here: `--disallowedTools Bash,Read,Write,Edit,MultiEdit,NotebookEdit,
WebFetch,WebSearch,Task` denied every tool, and the prompt already carried
all evidence (`{{PACK_BRIEF}}`), precisely so the model could not open
`pack_download.bin` or a 26 MiB `hires.txt` (issue #148). A direct HTTP
call makes that boundary structural rather than a denial list: the request
body below has no `tools` key at all, so there is nothing for the model to
call.

Usage:
  python3 scripts/gemini_classify.py --prompt-file P --schema-file S
                                     [--model gemini-3.8-flash]
                                     [--fallback-model gemini-3.6-flash]
                                     [--timeout 240] [--attempts 2]
                                     [--deadline 600]

Reads the API key from `GEMINI_API_KEY` in the environment — never from an
argument, so it cannot land in a process listing. Exit codes: 0 success,
2 usage/input error, 3 transport or HTTP error, 4 no model output,
5 output that is not a JSON object.

A model chain, not a single model (2026-09-16). `gemini-3.8-flash` answered
`HTTP 500 "currently experiencing high demand"` and, on a free-tier key,
`429 ... limit: 20, model: gemini-3.8-flash`, for minutes at a time, while
`gemini-3.6-flash` answered the same request in 7 seconds. A spike on one
model must not strand a submission in "Em validação", so the step walks a
chain — primary first, then each `--fallback-model` — and only a
model-level failure moves it along: a 400/401/403 aborts at once, because
the next model would fail identically.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/interactions"
DEFAULT_MODEL = "gemini-3.8-flash"
DEFAULT_FALLBACK_MODELS = ("gemini-3.6-flash",)
API_KEY_ENV = "GEMINI_API_KEY"
# Retried within one model, and treated as a reason to try the next model in
# the chain: rate limiting, model demand, and transient server faults. A 4xx
# that is not 429 is a request-shape or credential error — the next model
# would answer identically, so the run aborts instead of walking the chain.
RETRY_STATUS = {429, 500, 502, 503, 504}
MODEL_LEVEL_STATUS = {404, *RETRY_STATUS}
BACKOFF_SECONDS = (5, 15)
DEFAULT_ATTEMPTS = 2
# Total wall-clock budget for the whole chain. The step carries
# `timeout-minutes: 15`; this keeps a slow chain from being killed by it
# with no diagnostic at all.
DEFAULT_DEADLINE = 600.0
# stderr only, and truncated: this runs in a public repository's Actions log.
DIAGNOSTIC_CHARS = 400


class ApiError(RuntimeError):
    """A failed call, carrying the HTTP status when there was one."""

    def __init__(self, message: str, status: int | None = None):
        super().__init__(message)
        self.status = status

    @property
    def model_level(self) -> bool:
        """True when another model in the chain is worth trying."""
        return self.status is None or self.status in MODEL_LEVEL_STATUS


def build_request(model: str, prompt: str, schema: dict) -> dict:
    """The Interactions API request body.

    `response_format.schema` is where the structured-output contract goes
    (the older `generationConfig.responseSchema` surface is a different,
    more restrictive OpenAPI subset and is not used here). No `tools` key:
    the classify step is a text generator by construction.
    """
    return {
        "model": model,
        "input": prompt,
        "response_format": {
            "type": "text",
            "mime_type": "application/json",
            "schema": schema,
        },
    }


def extract_text(response: dict) -> str:
    """The model's answer from an Interactions API response.

    Text lives in `steps[].content[].text`, one level deeper than the
    pre-May-2026 `outputs[].text` shape (that legacy schema was removed on
    2026-06-08, so there is no fallback path here). Steps may also carry
    `thought` and `user_input` entries; only `model_output` steps are the
    answer, and consecutive text blocks within one are concatenated.
    """
    chunks = []
    for step in response.get("steps") or []:
        if step.get("type") != "model_output":
            continue
        for block in step.get("content") or []:
            if block.get("type") == "text" and block.get("text"):
                chunks.append(block["text"])
    return "".join(chunks)


def post_once(api_key: str, body: dict, timeout: float) -> dict:
    """One POST. Raises ApiError, carrying the HTTP status when there is one."""
    request = urllib.request.Request(
        ENDPOINT,
        data=json.dumps(body).encode("utf-8"),
        method="POST",
        headers={"x-goog-api-key": api_key, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as handle:
            return json.loads(handle.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")[:DIAGNOSTIC_CHARS]
        raise ApiError(f"HTTP {exc.code}: {detail}", status=exc.code) from exc
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        raise ApiError(f"{type(exc).__name__}: {exc}") from exc


def run_chain(
    models,
    api_key: str,
    prompt: str,
    schema: dict,
    timeout: float,
    attempts: int,
    deadline: float,
    call=None,
    sleep=time.sleep,
    clock=time.monotonic,
):
    """Walk the model chain, returning (response, model_used).

    Each model gets `attempts` tries with a backoff between them; a
    model-level failure exhausts the model and moves to the next one. A
    non-model-level failure (bad request, bad credential) propagates at
    once, and the whole chain is abandoned once `deadline` seconds have
    passed — the step's own `timeout-minutes` must never be what stops
    this, because that leaves no diagnostic in the log.

    `call` is resolved here rather than as a default argument so that
    replacing the module-level post_once (which the unit tests do) reaches
    this function.
    """
    call = call or post_once
    started = clock()
    failures = []
    for index, model in enumerate(models):
        for attempt in range(1, attempts + 1):
            if clock() - started > deadline:
                raise ApiError(
                    f"deadline of {deadline:.0f}s exceeded; last failure: "
                    + (failures[-1] if failures else "none"),
                    status=None,
                )
            try:
                return call(api_key, build_request(model, prompt, schema), timeout), model
            except ApiError as exc:
                failures.append(f"{model}: {exc}")
                if not exc.model_level:
                    raise
                if attempt < attempts:
                    sleep(BACKOFF_SECONDS[min(attempt - 1, len(BACKOFF_SECONDS) - 1)])
        if index + 1 < len(models):
            print(
                f"gemini_classify: {model} unavailable, falling back to "
                f"{models[index + 1]} — {failures[-1]}",
                file=sys.stderr,
            )
    raise ApiError("every model in the chain failed: " + " | ".join(failures[-4:]))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--schema-file", required=True)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument(
        "--fallback-model",
        action="append",
        default=None,
        help="tried in order when the primary model is unavailable; "
        f"defaults to {' '.join(DEFAULT_FALLBACK_MODELS)}. Repeatable.",
    )
    parser.add_argument("--timeout", type=float, default=240.0)
    parser.add_argument("--attempts", type=int, default=DEFAULT_ATTEMPTS)
    parser.add_argument("--deadline", type=float, default=DEFAULT_DEADLINE)
    args = parser.parse_args(argv)
    models = [args.model, *(args.fallback_model or DEFAULT_FALLBACK_MODELS)]

    api_key = os.environ.get(API_KEY_ENV, "")
    if not api_key:
        print(f"error: {API_KEY_ENV} is not set", file=sys.stderr)
        return 2
    try:
        prompt = open(args.prompt_file, encoding="utf-8").read()
        schema = json.loads(open(args.schema_file, encoding="utf-8").read())
    except (OSError, json.JSONDecodeError) as exc:
        print(f"error: could not read prompt/schema: {exc}", file=sys.stderr)
        return 2
    if not prompt.strip() or not schema:
        print("error: empty prompt or schema", file=sys.stderr)
        return 2

    try:
        response, model_used = run_chain(
            models,
            api_key,
            prompt,
            schema,
            args.timeout,
            args.attempts,
            args.deadline,
        )
    except ApiError as exc:
        print(f"error: Gemini call failed: {exc}", file=sys.stderr)
        return 3

    usage = response.get("usage") or {}
    print(
        f"gemini_classify: model={model_used} chain={','.join(models)} "
        f"prompt_chars={len(prompt)} usage={json.dumps(usage, sort_keys=True)}",
        file=sys.stderr,
    )

    text = extract_text(response)
    if not text.strip():
        status = response.get("status") or response.get("finish_reason") or "unknown"
        print(f"error: no model output (status={status})", file=sys.stderr)
        return 4
    try:
        verdict = json.loads(text)
    except json.JSONDecodeError as exc:
        print(
            f"error: model output is not JSON: {exc}; "
            f"first {DIAGNOSTIC_CHARS} chars: {text[:DIAGNOSTIC_CHARS]}",
            file=sys.stderr,
        )
        return 5
    if not isinstance(verdict, dict) or "verdict" not in verdict:
        print(f"error: model output is not a verdict object: {text[:DIAGNOSTIC_CHARS]}", file=sys.stderr)
        return 5

    sys.stdout.write(json.dumps(verdict, ensure_ascii=False, separators=(",", ":")) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
