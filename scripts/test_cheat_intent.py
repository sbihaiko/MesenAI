#!/usr/bin/env python3
"""Unit tests for scripts/cheat_intent.py (P.11, ADR-0245 §4, ADR-0247). No network.

Every backend here is a fake or an injected transport: the closed Choice is
built from the bundled list, the check discards anything outside it, the JSON
the client reads carries the list's own text, and the OpenRouter key never
reaches argv, the request body, stdout, stderr or the decision log.
"""
import contextlib
import io
import json
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import cheat_intent  # noqa: E402
import cheat_intent_eval  # noqa: E402
import jev_client  # noqa: E402

KEY = "sk-or-v1-TESTKEY-cheat-intent-0123456789"
SMB = "FEFA1097449A3A11EBF8C6199E905996C5DC8FBD"  # Super Mario Bros. (World)
CHEATS = [
    {"desc": "Infinite lives", "code": "SXIOPO"},
    {"desc": "Start on World 5", "code": "YSAOPE;YEAOZA;GEAPYA"},
    {"desc": "Infinite lives (alt)", "code": "075A:09"},
]


def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def ok(msg):
    print(f"PASS: {msg}")


class FakeBackend:
    """Says whatever it is told to, and records what it was offered."""

    name = "fake"

    def __init__(self, choice):
        self.choice = choice
        self.offered = None

    def choose(self, game, intent, options):
        self.offered = options
        return cheat_intent.RawAnswer(self.choice, "fake-1", 0.01, 0.0)


def jev_response(choice, cost=7.0e-05):
    return {
        "model": "typesafe/jev-1.13-20260917",
        "answers": {cheat_intent.QUESTION: {
            "type": "choice", "choice": choice,
            "probabilities": {choice: 0.9, "NONE": 0.1}, "confidence": 0.5}},
        "usage": {"cost": cost},
        "id": "gen-test",
    }


def test_closed_choice_construction():
    options = cheat_intent.build_options(CHEATS)
    ids = [o.id for o in options]
    if ids != ["E0", "E1", "E2", "NONE"]:
        fail(f"options should be E<index> for each entry plus NONE last, got {ids}")
    if [o.desc for o in options[:3]] != [c["desc"] for c in CHEATS]:
        fail("option descriptions must be the list's own descriptions")
    for bad in ([], [{"desc": "", "code": "AAAA"}]):
        try:
            cheat_intent.build_options(bad)
        except cheat_intent.IntentError:
            continue
        fail(f"{bad!r} should be refused before any backend is asked")
    for intent in ("", "   ", "x" * (cheat_intent.MAX_INTENT_CHARS + 1)):
        try:
            cheat_intent.clean_intent(intent)
        except cheat_intent.IntentError:
            continue
        fail(f"intent of length {len(intent)} should be refused")
    game = cheat_intent.find_game(SMB.lower(), cheat_intent.load_db())
    if game["name"] != "Super Mario Bros. (World)":
        fail("find_game should match the bundled SHA-1 case-insensitively")
    fake = FakeBackend("E1")
    cheat_intent.answer(fake, game["name"], game["sha1"], "start on world 5", game["cheats"])
    if len(fake.offered) != len(game["cheats"]) + 1:
        fail("the backend must be offered exactly this game's entries plus NONE")
    ok("the Choice is this game's entries as E<index> plus NONE; empty lists and intents refused")


def test_discard_rule():
    options = cheat_intent.build_options(CHEATS)
    cases = {
        "E1": "match", "NONE": "none",
        "E3": "discarded",            # one past the list
        "e1": "discarded",            # near miss: not the exact name offered
        " E1": "discarded",
        "SXIOPO": "discarded",        # a listed code is still not an option name
        "GZXXXXXX": "discarded",      # an invented code
        "Infinite lives": "discarded",
        "": "discarded",
        None: "discarded",
        7: "discarded",
    }
    for choice, expected in cases.items():
        status, option = cheat_intent.resolve(options, choice)
        if status != expected:
            fail(f"choice {choice!r}: expected {expected}, got {status}")
        if (option is not None) != (expected == "match"):
            fail(f"choice {choice!r}: only a match may carry an entry")
    ok("only an exact offered option name passes; codes, near misses and free text are discarded")


def test_json_output():
    for choice, status in (("E2", "match"), ("NONE", "none"), ("Here is a code: GZXXXXXX", "discarded")):
        result = cheat_intent.answer(FakeBackend(choice), "Game", "ABC", "  infinite   lives ", CHEATS)
        text = json.dumps(result)
        if result["schema"] != cheat_intent.SCHEMA or result["status"] != status:
            fail(f"{choice!r}: unexpected header {result}")
        if result["intent"] != "infinite lives":
            fail("the intent should be whitespace-normalized")
        if status == "match":
            if result["entry"] != {"id": "E2", "index": 2, "desc": "Infinite lives (alt)", "code": "075A:09"}:
                fail(f"the entry must be copied from the list, got {result['entry']}")
        elif result["entry"] is not None:
            fail(f"{status}: entry must be null")
        if status == "discarded" and "GZXXXXXX" in text:
            fail("a discarded answer's text must never reach the client")
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cheat_intent.main(
            ["--sha1", SMB, "--intent", "start on world 5"],
            transport=lambda url, body, timeout: {"model": "m", "message": {"content": '{"choice": "E42"}'}},
        )
    if code != 0:
        fail(f"main should exit 0, got {code}: {err.getvalue()}")
    result = json.loads(out.getvalue())
    if result["entry"]["desc"] != "Start on World 5" or result["backend"] != "ollama":
        fail(f"main's JSON should name the bundled entry, got {result}")
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        if cheat_intent.main(["--sha1", "00" * 20, "--intent", "x"]) != 2:
            fail("an unknown game should exit 2")
        if cheat_intent.main(["--sha1", SMB, "--intent", " "]) != 2:
            fail("an empty intent should exit 2")
    ok("the client JSON carries the list's desc/code for a match, null otherwise, never discarded text")


def test_ollama_backend():
    seen = {}

    def transport(url, body, timeout):
        seen["url"], seen["body"] = url, body
        return {"model": "qwen2.5:7b-instruct", "message": {"content": seen.get("reply", '{"choice": "E0"}')}}

    backend = cheat_intent.OllamaBackend(transport=transport)
    options = cheat_intent.build_options(CHEATS)
    raw = backend.choose("Game", "infinite lives", options)
    body = seen["body"]
    if "tools" in body or "tool_choice" in body:
        fail("the Ollama request must be tool-free")
    if body["format"]["properties"]["choice"]["enum"] != [o.id for o in options]:
        fail("the output schema's enum must be exactly the offered option names")
    if body["options"]["temperature"] != 0 or body["stream"] is not False:
        fail("the request should be deterministic and unstreamed")
    if not seen["url"].startswith("http://127.0.0.1:"):
        fail(f"unexpected Ollama URL {seen['url']}")
    if raw.choice != "E0":
        fail(f"expected E0, got {raw.choice}")
    for reply in ("not json", '["E0"]', '{"choice": 3}', '{"answer": "E0"}'):
        seen["reply"] = reply
        if backend.choose("Game", "x", options).choice is not None:
            fail(f"reply {reply!r} should come back as no choice (discarded)")
    loose = cheat_intent.OllamaBackend(transport=transport, constrain=False)
    loose.choose("Game", "x", options)
    if seen["body"]["format"] != "json":
        fail("--loose should ask for plain JSON")
    for host in ("http://192.168.1.5:11434", "https://ollama.example.com"):
        try:
            cheat_intent.OllamaBackend(host=host)
        except cheat_intent.IntentError:
            continue
        fail(f"a non-loopback Ollama host {host} must be refused")

    def broken(url, body, timeout):
        raise OSError("connection refused")

    try:
        cheat_intent.OllamaBackend(transport=broken).choose("Game", "x", options)
    except cheat_intent.BackendError:
        pass
    else:
        fail("a dead Ollama should be a BackendError")
    ok("Ollama: tool-free, enum-constrained, loopback only; unparsable replies become discards")


def test_jev_backend(tmp):
    bodies = []

    def transport_for(choice):
        def transport(api_key, body, timeout):
            bodies.append(body)
            return 200, json.dumps(jev_response(choice))
        return transport

    options = cheat_intent.build_options(CHEATS)
    client = jev_client.JevClient(KEY, log_path=None, transport=transport_for("E1"))
    raw = cheat_intent.JevBackend(client).choose("Game", "start on world 5", options)
    criteria = bodies[-1]["questions"][cheat_intent.QUESTION]["criteria"]
    if list(criteria) != [o.id for o in options]:
        fail(f"Jev's criteria must be the offered option names, got {list(criteria)}")
    if raw.choice != "E1" or raw.cost_usd != 7.0e-05:
        fail(f"unexpected Jev answer {raw}")
    client = jev_client.JevClient(KEY, log_path=None, transport=transport_for("E99"))
    raw = cheat_intent.JevBackend(client).choose("Game", "x", options)
    if raw.choice is not None or client.spent_usd != 7.0e-05:
        fail("an off-list Jev answer is discarded, and its cost still counted")
    status, _ = cheat_intent.resolve(options, raw.choice)
    if status != "discarded":
        fail("an off-list Jev answer must resolve to discarded")
    ok("Jev: criteria are the offered names; an off-list answer is discarded and still paid for")


def test_key_never_leaks(tmp):
    """argv, request body, stdout, stderr and the JSONL log: none may hold the key."""
    argv_seen, inputs_seen = [], []

    def fake_run(argv, input=None, **kwargs):
        argv_seen.append(list(argv))
        inputs_seen.append(input or "")
        out_path = argv[argv.index("--output") + 1]
        Path(out_path).write_text(json.dumps(jev_response("E0")), encoding="utf-8")

        class Done:
            returncode = 0
            stdout = "200"
            stderr = ""
        return Done()

    def transport(api_key, body, timeout):
        return jev_client.curl_post(api_key, body, timeout, run=fake_run)

    log = Path(tmp) / "decisions.jsonl"
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cheat_intent.main(
            ["--sha1", SMB, "--intent", "infinite lives", "--backend", "jev", "--log", str(log)],
            env={jev_client.API_KEY_ENV: KEY}, repo_root=tmp, transport=transport,
        )
    if code != 0:
        fail(f"the Jev run should exit 0, got {code}: {err.getvalue()}")
    sinks = {
        "argv": json.dumps(argv_seen), "request body": json.dumps(inputs_seen),
        "stdout": out.getvalue(), "stderr": err.getvalue(),
        "log": log.read_text(encoding="utf-8"),
    }
    for sink, text in sinks.items():
        if KEY in text:
            fail(f"the key reached {sink}")
    if not sinks["log"].strip():
        fail("the decision log should have been written (or the leak check proves nothing)")
    # A failure on the way out: the vendor echoes the key in its error body.
    def echoing(api_key, body, timeout):
        return 401, f"bad key {api_key}"

    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cheat_intent.main(
            ["--sha1", SMB, "--intent", "x", "--backend", "jev"],
            env={jev_client.API_KEY_ENV: KEY}, repo_root=tmp, transport=echoing,
        )
    if code != 3 or KEY in out.getvalue() + err.getvalue():
        fail(f"an HTTP failure should exit 3 with the key scrubbed (exit {code})")
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        code = cheat_intent.main(["--sha1", SMB, "--intent", "x", "--backend", "jev"],
                                 env={}, repo_root=tmp)
    if code != 2:
        fail(f"no key should exit 2, got {code}")
    ok("the key never reaches argv, the body, stdout, stderr or the log; no key exits 2")


def test_intent_set_matches_bundled_list():
    path = cheat_intent_eval.CASES
    doc = json.loads(path.read_text(encoding="utf-8"))
    if not doc.get("how_chosen"):
        fail("the intent set must document how the expected answers were chosen")
    games = {g["sha1"].upper(): g for g in cheat_intent.load_db()}
    cases = doc["cases"]
    if not 30 <= len(cases) <= 80:
        fail(f"the intent set should hold 30-80 cases, has {len(cases)}")
    if len({c['sha1'] for c in cases}) < 10:
        fail("the intent set should span at least 10 games")
    if not any(c["accept"] == ["NONE"] for c in cases):
        fail("the intent set needs negatives where NONE is correct")
    for case in cases:
        game = games.get(case["sha1"].upper())
        if game is None or game["name"] != case["game"]:
            fail(f"{case['game']}: SHA-1 not in the bundled list under that name")
        if len(case["accept"]) != len(case["accept_desc"]):
            fail(f"{case['game']} / {case['intent']}: accept and accept_desc differ in length")
        for option_id, desc in zip(case["accept"], case["accept_desc"]):
            if option_id == "NONE":
                continue
            index = int(option_id[1:])
            if index >= len(game["cheats"]) or game["cheats"][index]["desc"] != desc:
                fail(f"{case['game']} / {case['intent']}: {option_id} is not {desc!r} in the list")
    ok(f"the {len(cases)}-case intent set agrees with the bundled list")


def test_eval_scoring():
    case_entry = {"accept": ["E1", "E2"]}
    case_none = {"accept": ["NONE"]}
    match = {"status": "match", "entry": {"id": "E2"}}
    if not cheat_intent_eval.score(case_entry, match):
        fail("an accepted entry scores")
    if cheat_intent_eval.score(case_none, match):
        fail("an entry where NONE is expected does not score")
    if not cheat_intent_eval.score(case_none, {"status": "none", "entry": None}):
        fail("NONE where NONE is expected scores")
    if cheat_intent_eval.score(case_entry, {"status": "discarded", "entry": None}):
        fail("a discarded answer never scores")
    rows = cheat_intent_eval.run(FakeBackend("NONE"), cheat_intent_eval.load_cases()[:3],
                                 progress=io.StringIO())
    summary = cheat_intent_eval.summarize(rows)
    if summary["cases"] != 3 or summary["discarded"] != 0 or summary["wrong_entry"] != 0:
        fail(f"unexpected summary {summary}")
    # Case 1 is SMB "don't die"; E2 is "Infinite time".
    rows = cheat_intent_eval.run(FakeBackend("E2"), cheat_intent_eval.load_cases()[:1],
                                 progress=io.StringIO())
    if cheat_intent_eval.summarize(rows)["wrong_entry"] != 1:
        fail("an offered entry outside the accepted set counts as a wrong entry")
    ok("the evaluation scores matches, NONE and discards as documented")


def main():
    test_closed_choice_construction()
    test_discard_rule()
    test_json_output()
    test_ollama_backend()
    test_eval_scoring()
    test_intent_set_matches_bundled_list()
    for test in (test_jev_backend, test_key_never_leaks):
        with tempfile.TemporaryDirectory() as tmp:
            test(tmp)
    return 0


if __name__ == "__main__":
    sys.exit(main())
