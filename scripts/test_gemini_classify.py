#!/usr/bin/env python3
"""Unit tests for scripts/gemini_classify.py (stdlib only, no network).

The API is never contacted: every case drives build_request/extract_text
directly, or replaces post_once with a stub that returns a recorded
Interactions API response shape. The model chain is driven through
run_chain's injected call/sleep/clock.
"""
import contextlib
import io
import json
import os
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import gemini_classify  # noqa: E402


def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def ok(msg):
    print(f"PASS: {msg}")


SCHEMA = {"type": "object", "properties": {"verdict": {"type": "string"}}, "required": ["verdict"]}
ANSWER = '{"verdict":"accepted"}'


def _response(*steps):
    return {"id": "int_1", "status": "completed", "steps": list(steps)}


def _model_output(text):
    return {"type": "model_output", "content": [{"type": "text", "text": text}]}


def _run_main(prompt, schema, response=None, error=None, argv=None, capture_err=False):
    """Run main() with a stubbed post_once. Returns (exit_code, stdout[, stderr])."""
    original = gemini_classify.post_once

    def stub(api_key, body, timeout):
        if error is not None:
            raise error
        return response

    gemini_classify.post_once = stub
    out, err = io.StringIO(), io.StringIO()
    try:
        with tempfile.TemporaryDirectory() as tmp:
            prompt_file = Path(tmp) / "prompt.txt"
            schema_file = Path(tmp) / "schema.json"
            prompt_file.write_text(prompt, encoding="utf-8")
            schema_file.write_text(json.dumps(schema), encoding="utf-8")
            args = argv or ["--prompt-file", str(prompt_file), "--schema-file", str(schema_file)]
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                code = gemini_classify.main(args)
    finally:
        gemini_classify.post_once = original
    return (code, out.getvalue(), err.getvalue()) if capture_err else (code, out.getvalue())


def _fake_clock(*values):
    """A monotonic() replacement; the last value repeats forever."""
    seq = list(values)

    def clock():
        return seq.pop(0) if len(seq) > 1 else seq[0]

    return clock


def _no_sleep():
    return lambda _seconds: None


def main():
    body = gemini_classify.build_request("gemini-3.8-flash", "PROMPT", SCHEMA)
    if body["model"] != "gemini-3.8-flash" or body["input"] != "PROMPT":
        fail(f"request body carries the wrong model/input: {body}")
    fmt = body.get("response_format") or {}
    if fmt.get("mime_type") != "application/json" or fmt.get("schema") != SCHEMA:
        fail(f"schema is not wired through response_format.schema: {body}")
    if "tools" in body:
        fail("request body declares tools; classify must be a plain text generator")
    if "generationConfig" in body or "responseSchema" in body:
        fail("request uses the legacy generateContent surface instead of the Interactions API")
    ok("build_request produces a tool-free Interactions API call")

    text = gemini_classify.extract_text(
        _response(
            {"type": "user_input", "content": [{"type": "text", "text": "IGNORED"}]},
            {"type": "thought", "content": [{"type": "text", "text": "IGNORED"}]},
            _model_output('{"verdict":'),
            _model_output('"accepted"}'),
        )
    )
    if text != ANSWER:
        fail(f"extract_text did not take the model_output steps in order: {text!r}")
    if gemini_classify.extract_text(_response({"type": "thought", "content": []})) != "":
        fail("extract_text invented text from a thought-only response")
    if gemini_classify.extract_text({}) != "":
        fail("extract_text did not survive a response with no steps")
    ok("extract_text reads model_output steps and ignores thought/user_input")

    # --- the model chain -------------------------------------------------
    calls = []

    def call_factory(statuses):
        """A post_once stub failing with each status in turn, then answering."""
        queue = list(statuses)

        def call(api_key, body, timeout):
            calls.append(body["model"])
            status = queue.pop(0) if queue else None
            if status is not None:
                raise gemini_classify.ApiError(f"HTTP {status}: boom", status=status)
            return _response(_model_output(ANSWER))

        return call

    sleeps = []
    response, used = gemini_classify.run_chain(
        ["primary", "secondary"],
        "k",
        "P",
        SCHEMA,
        1.0,
        1,
        600.0,
        call=call_factory([500]),
        sleep=sleeps.append,
        clock=_fake_clock(0.0),
    )
    if used != "secondary":
        fail(f"a 500 on the primary did not fall back, used={used}")
    if calls != ["primary", "secondary"]:
        fail(f"unexpected call order: {calls}")
    ok("a model-level failure walks to the next model")

    calls.clear()
    got = None
    try:
        gemini_classify.run_chain(
            ["primary", "secondary"],
            "k",
            "P",
            SCHEMA,
            1.0,
            1,
            600.0,
            call=call_factory([400]),
            sleep=sleeps.append,
            clock=_fake_clock(0.0),
        )
    except gemini_classify.ApiError as exc:
        got = exc.status
    if got != 400:
        fail(f"a 400 should abort the chain at once, got {got}")
    if calls != ["primary"]:
        fail(f"a 400 walked the chain instead of aborting: {calls}")
    ok("a request/credential error aborts instead of walking the chain")

    calls.clear()
    sleeps.clear()
    response, used = gemini_classify.run_chain(
        ["primary"],
        "k",
        "P",
        SCHEMA,
        1.0,
        3,
        600.0,
        call=call_factory([503, 503]),
        sleep=sleeps.append,
        clock=_fake_clock(0.0),
    )
    if used != "primary" or calls != ["primary"] * 3:
        fail(f"retries within one model are wrong: used={used} calls={calls}")
    if sleeps != [5, 15]:
        fail(f"backoff between attempts is wrong: {sleeps}")
    ok("one model is retried with backoff before the chain moves on")

    try:
        gemini_classify.run_chain(
            ["a", "b"],
            "k",
            "P",
            SCHEMA,
            1.0,
            1,
            600.0,
            call=call_factory([503, 503]),
            sleep=_no_sleep(),
            clock=_fake_clock(0.0),
        )
        fail("an exhausted chain should raise")
    except gemini_classify.ApiError as exc:
        if "every model in the chain failed" not in str(exc):
            fail(f"exhausted-chain error is not descriptive: {exc}")
    ok("an exhausted chain reports every model that failed")

    try:
        gemini_classify.run_chain(
            ["a", "b"],
            "k",
            "P",
            SCHEMA,
            1.0,
            2,
            60.0,
            call=call_factory([503, 503, 503, 503]),
            sleep=_no_sleep(),
            clock=_fake_clock(0.0, 1000.0),
        )
        fail("the deadline should stop the chain")
    except gemini_classify.ApiError as exc:
        if "deadline" not in str(exc):
            fail(f"deadline error is not descriptive: {exc}")
    ok("the chain gives up at its deadline instead of the step's timeout")

    # --- main() ----------------------------------------------------------
    code, stdout = _run_main("PROMPT", SCHEMA, response=_response(_model_output(ANSWER)))
    if code != 0:
        fail(f"successful classify exited {code}")
    if stdout != ANSWER + "\n":
        fail(f"stdout is not one compact JSON line: {stdout!r}")

    code, _, err = _run_main(
        "PROMPT", SCHEMA, response=_response(_model_output(ANSWER)), capture_err=True
    )
    if "chain=" not in err:
        fail(f"the chain that was tried is not logged: {err!r}")
    ok("a valid answer is printed as one compact JSON line, with the chain logged")

    code, _ = _run_main("PROMPT", SCHEMA, response=_response())
    if code != 4:
        fail(f"an empty response should exit 4, got {code}")
    code, _ = _run_main("PROMPT", SCHEMA, response=_response(_model_output("not json")))
    if code != 5:
        fail(f"non-JSON output should exit 5, got {code}")
    code, _ = _run_main("PROMPT", SCHEMA, response=_response(_model_output('["a"]')))
    if code != 5:
        fail(f"a non-object answer should exit 5, got {code}")
    code, _ = _run_main(
        "PROMPT", SCHEMA, error=gemini_classify.ApiError("HTTP 400: bad schema", status=400)
    )
    if code != 3:
        fail(f"a chain failure should exit 3, got {code}")
    ok("failure modes exit 4/5/3")

    saved = os.environ.pop(gemini_classify.API_KEY_ENV, None)
    try:
        code, _ = _run_main("PROMPT", SCHEMA, response=_response(_model_output(ANSWER)))
        if code != 2:
            fail(f"a missing {gemini_classify.API_KEY_ENV} should exit 2, got {code}")
    finally:
        if saved is not None:
            os.environ[gemini_classify.API_KEY_ENV] = saved
    ok(f"a missing {gemini_classify.API_KEY_ENV} is refused before any request")

    if gemini_classify.DEFAULT_FALLBACK_MODELS[0] != "gemini-3.6-flash":
        fail("the measured fallback (2026-09-16) is no longer the default")
    ok("gemini-3.6-flash is the default fallback for the measured spike")

    return 0


if __name__ == "__main__":
    if not os.environ.get(gemini_classify.API_KEY_ENV):
        # The stub never uses the key; set a placeholder so the success paths
        # exercise the same code the CI does.
        os.environ[gemini_classify.API_KEY_ENV] = "test-key"
    sys.exit(main())
