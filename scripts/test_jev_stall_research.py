#!/usr/bin/env python3
"""Tests for `scripts/jev_stall_research.py` - one web-search worker per stall.

Two halves. The **mocked** half pins the worker's argv, every answer shape this
`claude` CLI actually produces, and what a failure has to look like; it runs
everywhere, with no key and no network. The **live** half makes one real call and
is skipped unless `JEV_RESEARCH_LIVE=1`, because the suite runs in CI where a
model call is neither possible nor wanted:

    JEV_RESEARCH_LIVE=1 OPENROUTER_API_KEY=... python3 scripts/test_jev_stall_research.py

Why this file exists at all: F14.15's first pass spawned nine live workers and
none of them came back usable, and neither cause was the network.

* `--output-format json` on this CLI emits a JSON **array of stream events**,
  the answer text inside the last `{"type": "result", ...}` element - not the
  single `{"result": ...}` envelope the module parsed. A worker that answered
  perfectly was read as "answered no JSON object".
* The model the module pinned, `claude-deepseek-v4-flash[1m]`, is not a model
  this CLI knows (`[claude-code:unrecognized_model]` on stderr), and on the
  module's real prompt it returned **nothing at all** - empty stdout - while
  `sonnet` returned a usable proposal object. That is the stall, and the default
  is `sonnet` because of it, measured in `runs/diag/`.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import jev_stall_research  # noqa: E402

_FAILURES = []


def check(cond, name, detail=""):
    if cond:
        print(f"ok   {name}")
    else:
        print(f"FAIL {name}: {detail}")
        _FAILURES.append(name)


NG_REPORT = {
    "game": "Ninja Gaiden",
    "progress_field": "abs_x",
    "spot": {"position": 220.0,
             "state": {"abs_x": 220.0, "camera_x": 208.0, "hp": 2, "lives": 2}},
    "macros_available": {"RIGHT_15": "walk forward", "JUMP_15": "press A",
                         "ATTACK_LEFT_15": "slash facing left"},
    "tried": {"9.0": [{"macro": "JUMP_RIGHT_15", "reached": 220.0, "died": False},
                      {"macro": "JUMP_15", "reached": 220.0, "died": False}]},
    "loops": 1,
    "tips_ids": ["enemy-behind"],
    "stall_seconds": 3.0,
}

PROPOSAL = {"tips": [{"id": "slash-first", "tip": "slash once before the jump",
                      "macro": "ATTACK_LEFT", "when": [], "sources": []}],
            "macros": [], "query": "ninja gaiden stage 1-1 first gauntlet"}


def _result_event(text) -> str:
    """A `claude -p --output-format json` stdout, as this CLI writes it."""
    return json.dumps([
        {"type": "system", "subtype": "init", "tools": ["WebSearch"]},
        {"type": "assistant", "message": {"content": [
            {"type": "text", "text": "searching"}]}},
        {"type": "user", "message": {"content": [
            {"type": "tool_result", "content": "..."}]}},
        {"type": "result", "subtype": "success", "is_error": False,
         "result": text, "num_turns": 3, "total_cost_usd": 0.02},
    ])


def _runner(stdout="", *, returncode=0, stderr="", raises=None):
    def run(argv, **kwargs):
        run.seen = (argv, kwargs.get("input"), kwargs.get("timeout"))
        run.kwargs = kwargs
        if raises is not None:
            raise raises
        return subprocess.CompletedProcess(argv, returncode, stdout, stderr)
    run.seen = None
    run.kwargs = {}
    return run


# --------------------------------------------------------------------------
# the worker's argv
# --------------------------------------------------------------------------

def test_the_worker_argv_pins_the_model_and_a_headless_json_run(tmp):
    argv = jev_stall_research.claude_argv()
    check(argv[argv.index("--model") + 1] == jev_stall_research.MODEL,
          "the argv's model is the module's own default", " ".join(argv))
    check(jev_stall_research.MODEL == "sonnet",
          "the default model is `sonnet`: the pinned DeepSeek id is not a model "
          "this CLI knows (`unrecognized_model` on stderr) and answered nothing "
          "at all on this module's real prompt, where `sonnet` answered",
          jev_stall_research.MODEL)
    check(argv[argv.index("--allowedTools") + 1] == "WebSearch,WebFetch",
          "the worker gets web search and web fetch, and no other tool",
          " ".join(argv))
    check("-p" in argv and "--output-format" in argv,
          "the worker is headless and its answer is machine-readable",
          " ".join(argv))
    check(argv[:1] == ["claude"], "it is the CLI, called by name", argv[0])
    flags = [part for part in argv if part.startswith("--")]
    check("--model" in argv and len(flags) == len(set(flags)),
          "no flag is repeated", " ".join(flags))


def test_the_worker_can_only_reach_the_web_tools(tmp):
    """The argv has to *limit the built-in set*, not merely prefer a subset.

    F14.15's open problem 4: `--allowedTools WebSearch,WebFetch` is not enforced
    by this CLI - the worker's own init event listed every built-in tool, and a
    non-allow-listed tool executed. Measured 2026-09-26 with the same question
    and model on both argvs: `--allowedTools` alone answers with
    `["Task", "Bash", "CronCreate", ... 21 names]`
    (`runs/f1415-sec/probe-red.json`), and `--tools WebSearch,WebFetch` in front
    of it answers with `["WebFetch", "WebSearch"]`
    (`runs/f1415-sec/probe-tools.json`). Web text flows into this worker, so
    "the argv intends it" is not what ADR-0238 section 3 claims.
    """
    argv = jev_stall_research.claude_argv()
    check("--tools" in argv,
          "the argv carries `--tools`, the flag that limits the built-in set "
          "rather than the one that only allow-lists it", " ".join(argv))
    if "--tools" not in argv or "--disallowedTools" not in argv:
        return
    tools = argv[argv.index("--tools") + 1].split(",")
    check(sorted(tools) == ["WebFetch", "WebSearch"],
          "`--tools` names the two web tools and nothing else", str(tools))
    denied = argv[argv.index("--disallowedTools") + 1].split(",")
    for name in ("Bash", "Edit", "Write", "NotebookEdit", "Read", "Glob",
                 "Grep", "Task", "Agent", "Skill"):
        check(name in denied,
              f"`--disallowedTools` also denies {name}, so the restriction does "
              f"not rest on one flag's behaviour", str(denied))
    check("--permission-mode" not in argv
          and "--dangerously-skip-permissions" not in argv
          and "--allow-dangerously-skip-permissions" not in argv,
          "the worker is never handed a permission bypass on top of that",
          " ".join(argv))
    check("--safe-mode" in argv,
          "the worker loads no CLAUDE.md, hook, skill or plugin of ours: the "
          "machine's own configuration is not a thing web text should reach",
          " ".join(argv))


def test_the_key_is_not_in_the_workers_argv_environment_or_stdin(tmp):
    """ADR-0238 section 3: the key is never printed, logged, committed or passed
    on a command line. The worker does not need it - it runs on the CLI's own
    credentials - so this pins that the module never touches it."""
    source = (HERE / "jev_stall_research.py").read_text(encoding="utf-8")
    check("OPENROUTER" not in source,
          "the module does not read, name or forward the OpenRouter key at all",
          [line for line in source.splitlines() if "OPENROUTER" in line][:3])
    os.environ["OPENROUTER_API_KEY"] = "sk-or-v1-THIS-MUST-NOT-LEAK"
    try:
        runner = _runner(_result_event(json.dumps(PROPOSAL)))
        proposals = jev_stall_research.research(NG_REPORT, runner=runner)
        argv, sent, _timeout = runner.seen
        check(not any("sk-or-v1" in str(part) for part in argv),
              "the key is not in the argv even when the environment carries it",
              " ".join(argv))
        check("sk-or-v1" not in json.dumps(runner.kwargs),
              "and not in anything else the module passes to subprocess, an "
              "environment included",
              json.dumps(runner.kwargs)[:200])
        check("sk-or-v1" not in (sent or ""),
              "and not on the worker's stdin",
              (sent or "")[:200])
        check(not proposals.get("error"),
              "the pass works with no key at all, so it does not need one",
              str(proposals.get("error")))
    finally:
        del os.environ["OPENROUTER_API_KEY"]


def test_the_worker_gets_a_hard_cap_below_the_runs_own_budget(tmp):
    runner = _runner(_result_event(json.dumps(PROPOSAL)))
    jev_stall_research.research(NG_REPORT, runner=runner, timeout=45.0)
    check(runner.seen and runner.seen[2] == 45.0,
          "the timeout reaches subprocess.run, so a hung worker cannot outlive "
          "the pass", str(runner.seen[2] if runner.seen else None))
    check(jev_stall_research.DEFAULT_TIMEOUT <= 900.0,
          "the module's own default is a cap, not an absence of one",
          str(jev_stall_research.DEFAULT_TIMEOUT))


# --------------------------------------------------------------------------
# the answer shapes this CLI produces
# --------------------------------------------------------------------------

def test_the_answer_is_read_out_of_the_cli_event_stream(tmp):
    """`--output-format json` here is an array of stream events, and the answer
    is the `result` event's own `result` string."""
    stdout = _result_event("here it is:\n" + json.dumps(PROPOSAL) + "\n")
    check(isinstance(json.loads(stdout), list),
          "the fake stdout is the array shape the CLI writes")
    proposals = jev_stall_research.research(NG_REPORT, runner=_runner(stdout))
    check(proposals.get("tips") and proposals["tips"][0]["id"] == "slash-first",
          "a worker that answered through the event stream is read", str(proposals)[:200])
    check(proposals.get("query"),
          "and the proposal carries a query", str(proposals.get("query"))[:80])


def test_the_pass_reports_the_tool_list_the_cli_actually_gave_the_worker(tmp):
    """The argv is a request; the init event is what happened.

    `_result_event`'s init event carries `tools: ["WebSearch"]`, one short of the
    argv's pair, which is the point: what the module reports is read from the
    event, not echoed from the flags it wrote.
    """
    proposals = jev_stall_research.research(
        NG_REPORT, runner=_runner(_result_event(json.dumps(PROPOSAL))))
    check(proposals.get("tools") == ["WebSearch"],
          "the answer carries the init event's own tool list",
          str(proposals.get("tools")))
    check(proposals.get("tools_unexpected") == [],
          "and names nothing outside the web pair as unexpected",
          str(proposals.get("tools_unexpected")))
    wider = json.dumps([
        {"type": "system", "subtype": "init",
         "tools": ["Task", "Bash", "Edit", "Read", "WebSearch", "WebFetch"]},
        {"type": "result", "subtype": "success", "is_error": False,
         "result": json.dumps(PROPOSAL), "total_cost_usd": 0.02},
    ])
    leaked = jev_stall_research.research(NG_REPORT, runner=_runner(wider))
    check(leaked.get("tools_unexpected") == ["Task", "Bash", "Edit", "Read"],
          "a worker that *was* handed the shell and the file tools says so, in "
          "order, instead of looking restricted",
          str(leaked.get("tools_unexpected")))
    failed = jev_stall_research.research(
        NG_REPORT, runner=_runner("", returncode=1, stderr="boom"))
    check("tools_unexpected" in failed,
          "a failed pass reports the tool list too - the run that never "
          "answered is exactly the one a wide tool list would hide in",
          str(sorted(failed))[:200])


def test_a_fenced_answer_inside_a_result_event_is_read(tmp):
    stdout = _result_event("```json\n" + json.dumps(PROPOSAL) + "\n```\n")
    proposals = jev_stall_research.research(NG_REPORT, runner=_runner(stdout))
    check(proposals.get("tips") and proposals["tips"][0]["id"] == "slash-first",
          "a fenced answer inside the event is read too", str(proposals)[:200])


def test_the_envelope_and_plain_text_shapes_still_parse(tmp):
    envelope = json.dumps({"type": "result", "result": json.dumps(PROPOSAL)})
    proposals = jev_stall_research.research(NG_REPORT, runner=_runner(envelope))
    check(proposals.get("tips") and proposals["tips"][0]["id"] == "slash-first",
          "a single envelope object - the shape the module used to expect - is "
          "still read", str(proposals)[:160])
    proposals = jev_stall_research.research(NG_REPORT, runner=_runner(json.dumps(PROPOSAL)))
    check(proposals.get("tips"), "so is a bare JSON answer with no envelope",
          str(proposals)[:160])


def test_a_result_event_that_is_an_error_is_an_error(tmp):
    stdout = json.dumps([{"type": "result", "subtype": "error_during_execution",
                          "is_error": True, "result": "the search tool failed"}])
    proposals = jev_stall_research.research(NG_REPORT, runner=_runner(stdout))
    check(proposals.get("error"),
          "an event stream whose result is an error is reported as one, not "
          "parsed for a JSON object that is not there", str(proposals)[:200])


def test_a_worker_that_times_out_is_an_error_not_a_crash(tmp):
    """A worker that outlives its cap has to come back as a failed pass.

    `subprocess.run(timeout=...)` raises `TimeoutExpired`, and the harness calls
    this module from inside a run: an exception here is not a failed research
    pass, it is the end of the run - which is what F14.15's first pass looked
    like from the outside, nine workers and no run.
    """
    runner = _runner(raises=subprocess.TimeoutExpired("claude", 45.0))
    proposals = jev_stall_research.research(NG_REPORT, runner=runner, timeout=45.0)
    check(isinstance(proposals, dict) and proposals.get("error"),
          "a timed-out worker is an error dict, not a raise", str(proposals)[:200])
    check("45" in json.dumps(proposals) or "timeout" in json.dumps(proposals).lower(),
          "and the error names the cap it was given", json.dumps(proposals)[:200])
    #The run must survive it: the same call one stall later still works.
    proposals = jev_stall_research.research(
        NG_REPORT, runner=_runner(_result_event(json.dumps(PROPOSAL))))
    check(proposals.get("tips"), "the next pass is unaffected", str(proposals)[:120])


def test_a_worker_that_dies_is_an_error_and_the_out_dir_is_written_once(tmp):
    failing = _runner("", returncode=1, stderr="boom")
    proposals = jev_stall_research.research(NG_REPORT, runner=failing)
    check(proposals.get("error"), "a non-zero worker is an error", str(proposals)[:160])
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "research"
        jev_stall_research.research(
            NG_REPORT, out_dir=out, runner=_runner(_result_event(json.dumps(PROPOSAL))))
        check((out / "proposals.json").exists() and (out / "query.txt").exists(),
              "a good pass keeps its proposals and its query under runs/",
              str(sorted(p.name for p in out.iterdir())))
        failed = Path(tmp) / "failed"
        jev_stall_research.research(NG_REPORT, out_dir=failed, runner=failing)
        check(not (failed / "proposals.json").exists(),
              "a failed pass leaves no proposals file for a run to pick up",
              str(sorted(p.name for p in failed.iterdir())) if failed.is_dir() else "(none)")


# --------------------------------------------------------------------------
# the live smoke
# --------------------------------------------------------------------------

def test_live_smoke_one_real_pass_on_the_ninja_gaiden_stall(tmp):
    """One real worker, one real answer, inside a minute.

    Skipped unless `JEV_RESEARCH_LIVE=1`: it opens the network and spends money,
    and neither belongs in the suite's default run.
    """
    if os.environ.get("JEV_RESEARCH_LIVE") != "1":
        print("skip the live smoke: set JEV_RESEARCH_LIVE=1 to run it")
        return
    out = Path(tmp) / "live"
    started = time.monotonic()
    proposals = jev_stall_research.research(NG_REPORT, out_dir=out, timeout=60.0)
    wall = time.monotonic() - started
    print(f"     live pass: {wall:.1f}s wall, "
          f"{json.dumps({k: proposals.get(k) for k in ('error', 'query')})[:200]}")
    check(wall <= 60.0, "the live pass answered inside its 60 s cap", f"{wall:.1f}s")
    check(not proposals.get("error"), "and it answered something usable",
          str(proposals.get("error"))[:200]
          + " | " + str(proposals.get("raw"))[:200])
    check(isinstance(proposals.get("tips"), list) and isinstance(proposals.get("macros"), list),
          "the answer carries the two proposal lists the harness merges",
          str(sorted(proposals))[:200])
    #The one thing the mocked half cannot check: what the CLI really offers a
    #worker whose argv restricts it. F14.15 section 9.4 is this assertion.
    check(not proposals.get("tools_unexpected"),
          "and the live worker's init event lists no tool outside the web pair",
          f"tools={proposals.get('tools')} "
          f"unexpected={proposals.get('tools_unexpected')}")
    check(sorted(proposals.get("tools") or []) == ["WebFetch", "WebSearch"],
          "the two web tools are the whole set the worker was given",
          str(proposals.get("tools")))


def main():
    tests = [
        test_the_worker_argv_pins_the_model_and_a_headless_json_run,
        test_the_worker_can_only_reach_the_web_tools,
        test_the_key_is_not_in_the_workers_argv_environment_or_stdin,
        test_the_worker_gets_a_hard_cap_below_the_runs_own_budget,
        test_the_answer_is_read_out_of_the_cli_event_stream,
        test_the_pass_reports_the_tool_list_the_cli_actually_gave_the_worker,
        test_a_fenced_answer_inside_a_result_event_is_read,
        test_the_envelope_and_plain_text_shapes_still_parse,
        test_a_result_event_that_is_an_error_is_an_error,
        test_a_worker_that_times_out_is_an_error_not_a_crash,
        test_a_worker_that_dies_is_an_error_and_the_out_dir_is_written_once,
        test_live_smoke_one_real_pass_on_the_ninja_gaiden_stall,
    ]
    for test in tests:
        print(f"--- {test.__name__}")
        with tempfile.TemporaryDirectory(prefix="jev-research-") as tmp:
            try:
                test(tmp)
            except Exception as error:  # noqa: BLE001 - a raised test is a failure
                import traceback
                traceback.print_exc()
                check(False, test.__name__, f"{type(error).__name__}: {error}")
    print(f"\n{len(_FAILURES)} failure(s)")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
