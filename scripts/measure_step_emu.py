#!/usr/bin/env python3
"""What one search candidate costs before and after the step-mode session.

F14.12, ADR-0238 section 1: the slice is picked by measurement, and so is the
claim that it was worth doing. The two drivers here do the same work for the
same candidate - a state in, one 29-frame window played, the RAM the search
judges on out, a state out - and differ only in whether the emulator survived
from one candidate to the next:

  before  one `scripts/headless_record` per candidate, which is what the
          scratch search did (`runs/route-*/solve.py`: subprocess.run per
          candidate, `state=` in and `save-state=` out). The archive ran 8 of
          them at once, so its per-candidate wall clock is the parallel number.
  after   one `headless_record ... session` for the whole run, with the
          candidate states kept in it (`scripts/step_emu.py`).

    python3 scripts/measure_step_emu.py --rom <rom> --work runs/f1412/measure

Ninja Gaiden's Act 1-1 is the case the ADR names: a 29-frame window from the
state `scripts/stages/ninjagaiden/mint-stage1.txt` writes, which is minted here
if the work folder has none (18 emulated seconds, about 3 s of wall clock).

`--archive-log <beam22.log>` adds the other half of the story: it runs the
ported search (`scripts/route_search.py`) for as many hops as the log holds and
diffs the per-hop standings against it. A faster search that explores a
different tree is not the same search, and this is the check that says so.
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import step_emu  # noqa: E402
import route_search  # noqa: E402
import mss_ram  # noqa: E402

BINARY = HERE / "headless_record"
MINT_SCRIPT = ROOT / "scripts" / "stages" / "ninjagaiden" / "mint-stage1.txt"
MINT_SECONDS = 18
CHUNK = 29
ARCHIVE_JOBS = 8  # runs/route-*/solve.py --jobs default


def one_shot(rom, prefix, seconds, state=None, script=None, save_state=None):
    args = [str(BINARY), str(rom), f"{seconds:.6f}", str(prefix),
            "mep-off", "hdpack-off"]
    if state:
        args.append(f"state={state}")
    if script:
        args.append(f"input={script}")
    if save_state:
        args.append(f"save-state={save_state}")
    done = subprocess.run(args, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"headless_record failed: {done.stdout}{done.stderr}")


def mint(rom, work):
    """The start state, from the committed mint script. `.mss` files are never
    versioned, so a measurement run mints its own."""
    state = work / "stage1-run.mss"
    if not state.exists():
        one_shot(rom, work / "mint" / "mint", MINT_SECONDS,
                 script=MINT_SCRIPT, save_state=state)
    return state


def window_text(action, chunk):
    """A candidate as the script lines that play it, which is one line for a
    single hold and a run of them for the wall techniques (F14.13)."""
    return "\n".join(route_search.window_lines(action, chunk)) + "\n"


def before_candidate(rom, work, slot, state, macro, chunk, keep=False):
    """One candidate the way the scratch search played it: a fresh process, a
    script file, a state file in and a state file out."""
    folder = work / f"slot{slot}"
    folder.mkdir(parents=True, exist_ok=True)
    script = folder / "window.txt"
    script.write_text(window_text(macro, chunk))
    out = folder / "window.mss"
    started = time.perf_counter()
    one_shot(rom, folder / "window", chunk / step_emu.FPS_NTSC,
             state=state, script=script, save_state=out)
    ram = mss_ram.ram(out)
    elapsed = time.perf_counter() - started
    if not keep:
        out.unlink(missing_ok=True)
    return elapsed, ram


def session_sweep(rom, work, state, plan, jobs):
    """`jobs` sessions in parallel, the plan split between them, and one
    session's per-request timings. Every session loads the same start state,
    which is what a search sharded across sessions does."""
    out = {}
    #One set of timings per worker, merged after the join: appending to a shared
    #list from several threads loses samples and, worse, reports a median over a
    #list that is not the plan's.
    timings = {}

    def worker(slot):
        steps = timings[slot] = {"restore": [], "play": [], "ram": [], "save": []}
        with step_emu.StepEmu(rom, work=work / f"session{slot}") as emu:
            handle = emu.load_file(state)
            started = time.perf_counter()
            for macro in plan[slot::jobs]:
                mark = time.perf_counter()
                emu.restore(handle)
                steps["restore"].append(time.perf_counter() - mark)
                mark = time.perf_counter()
                emu.load_script(window_text(macro, CHUNK))
                emu.run(CHUNK)
                steps["play"].append(time.perf_counter() - mark)
                mark = time.perf_counter()
                route_search.read_probes(emu)
                steps["ram"].append(time.perf_counter() - mark)
                mark = time.perf_counter()
                emu.drop(emu.save())
                steps["save"].append(time.perf_counter() - mark)
            out[slot] = time.perf_counter() - started

    threads = [threading.Thread(target=worker, args=(slot,)) for slot in range(jobs)]
    started = time.perf_counter()
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    wall = time.perf_counter() - started
    steps = {name: [sample for slot in sorted(timings)
                    for sample in timings[slot][name]]
             for name in ("restore", "play", "ram", "save")}
    return {"jobs": jobs, "total_s": wall,
            "per_candidate_s": wall / len(plan),
            "candidates_per_s": len(plan) / wall,
            "steps_s": {name: sum(times) / len(times)
                        for name, times in steps.items()}}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--rom", required=True)
    ap.add_argument("--work", required=True)
    #Eight hops' worth. A session is a process and pays its ROM load once, so
    #a batch of one hop would measure that startup instead of the per-candidate
    #cost the slice is about.
    ap.add_argument("--candidates", type=int, default=192)
    ap.add_argument("--hops", type=int, default=16)
    ap.add_argument("--archive-log", default=None,
                    help="a scratch beam search log to diff the ported search "
                         "against, one search standings line per hop")
    ap.add_argument("--json", default=None, help="write the numbers here too")
    a = ap.parse_args(argv)

    work = Path(a.work)
    work.mkdir(parents=True, exist_ok=True)
    #Every candidate plays the same macro set in the same order, so both
    #drivers see the same work and the numbers are comparable.
    macros = [action for _, action in route_search.CANDIDATES]
    plan = (macros * (a.candidates // len(macros) + 1))[:a.candidates]
    results = {"chunk": CHUNK, "candidates": a.candidates}

    state = mint(a.rom, work)
    print(f"start state: {state} ({state.stat().st_size} bytes)")

    # ---- before: one process per candidate ----
    started = time.perf_counter()
    for macro in plan:
        before_candidate(a.rom, work, 0, state, macro, CHUNK)
    results["before_serial_total_s"] = time.perf_counter() - started
    results["before_serial_per_candidate_s"] = (
        results["before_serial_total_s"] / a.candidates)
    shutil.rmtree(work / "slot0", ignore_errors=True)

    #The archive ran 8 at once and paid a wait per worker slot, so what it
    #could play per candidate is the total divided by the workers.
    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=ARCHIVE_JOBS) as pool:
        list(pool.map(
            lambda pair: before_candidate(a.rom, work, pair[0] + 1, state,
                                          pair[1], CHUNK),
            list(enumerate(plan))))
    results["before_parallel_total_s"] = time.perf_counter() - started
    results["before_parallel_per_candidate_s"] = (
        results["before_parallel_total_s"] / a.candidates)
    for slot in range(1, ARCHIVE_JOBS + 1):
        shutil.rmtree(work / f"slot{slot}", ignore_errors=True)
    print(f"before: {results['before_serial_per_candidate_s']:.3f} s per "
          f"candidate serial, {results['before_parallel_per_candidate_s']:.3f} s "
          f"with the archive's {ARCHIVE_JOBS} workers")

    # ---- after: sessions, one per worker slot ----
    #Both sides of the comparison are run at both worker counts: a session is
    #one process, so comparing one of them against the archive's 8 would be
    #comparing an engine to a fleet. What changes between the columns is the
    #worker, not the amount of parallelism.
    results["after"] = {}
    for jobs in (1, ARCHIVE_JOBS):
        results["after"][jobs] = session_sweep(a.rom, work, state, plan, jobs)
        row = results["after"][jobs]
        print(f"after:  {row['per_candidate_s']:.4f} s per candidate with "
              f"{jobs} session(s)")
    steps = results["after"][1]["steps_s"]
    print("        one candidate, in one session: "
          + ", ".join(f"{name} {value * 1000:.2f} ms"
                      for name, value in steps.items()))

    for jobs in results["after"]:
        results[f"speedup_vs_serial_{jobs}session"] = (
            results["before_serial_per_candidate_s"]
            / results["after"][jobs]["per_candidate_s"])
        results[f"speedup_vs_{ARCHIVE_JOBS}workers_{jobs}session"] = (
            results["before_parallel_per_candidate_s"]
            / results["after"][jobs]["per_candidate_s"])
    print(f"speedup, same workers: {ARCHIVE_JOBS} one-shot workers at "
          f"{results['before_parallel_per_candidate_s'] * 1000:.1f} ms -> "
          f"{ARCHIVE_JOBS} sessions at "
          f"{results['after'][ARCHIVE_JOBS]['per_candidate_s'] * 1000:.1f} ms "
          f"({results[f'speedup_vs_{ARCHIVE_JOBS}workers_{ARCHIVE_JOBS}session']:.1f}x)")

    # ---- the ported search does the same work ----
    if a.archive_log:
        archive = Path(a.archive_log)
        hops = [line for line in archive.read_text().splitlines()
                if line.startswith("hop")]
        if hops:
            #The search end to end, at both worker counts. Its own process:
            #the search narrates every hop, and that narration is the
            #evidence, not this tool's output.
            results["ported_search"] = {}
            for jobs in (1, ARCHIVE_JOBS):
                out = work / f"ported-route-{jobs}.txt"
                log = work / f"ported-search-{jobs}.log"
                started = time.perf_counter()
                done = subprocess.run(
                    [sys.executable, str(HERE / "route_search.py"),
                     "--rom", a.rom, "--state", str(state),
                     "--work", str(work / f"ported{jobs}"), "--out", str(out),
                     "--hops", str(len(hops)), "--chunk", str(CHUNK),
                     "--beam", "3", "--sessions", str(jobs),
                     #The archive's log is the scratch driver's own eight
                     #macros (`solve.py`). F14.13 added four more to the
                     #search, so the comparison asks the port for the eight it
                     #is being compared against.
                     "--only", ",".join(route_search.SCRATCH_MACROS),
                     "--log", str(log)], capture_output=True, text=True)
                if done.returncode != 0:
                    raise RuntimeError(
                        f"route_search failed: {done.stdout}{done.stderr}")
                mine = [line for line in log.read_text().splitlines()
                        if line.startswith("hop")]
                results["ported_search"][jobs] = {
                    "seconds": time.perf_counter() - started,
                    "hops_compared": min(len(mine), len(hops)),
                    "hops_identical": sum(1 for m, o in zip(mine, hops, strict=False)
                                          if standings(m) == standings(o))}
                row = results["ported_search"][jobs]
                print(f"ported search, {jobs} session(s): "
                      f"{row['hops_identical']}/{row['hops_compared']} hops match "
                      f"{archive.name} exactly, {row['seconds']:.1f} s")
            #Sharding must not change the tree the search explores: a search
            #that answers a different route faster is a different search.
            routes = [(work / f"ported-route-{jobs}.txt").read_text()
                      for jobs in (1, ARCHIVE_JOBS)]
            results["sessions_give_the_same_route"] = routes[0] == routes[1]
            print(f"          1 and {ARCHIVE_JOBS} sessions give the same route: "
                  f"{results['sessions_give_the_same_route']}")

    if a.json:
        Path(a.json).write_text(json.dumps(results, indent=2) + "\n")
        print(f"numbers written to {a.json}")
    return 0


#`y` is optional in the line and not compared: F14.13 added it to the search's
#log (a wall climb is invisible without it), and the archive's log predates it.
#Spacing and column widths are not compared either, which is the same rule.
STANDING = re.compile(r"(\w+) abs=\s*([\d.]+)(?: y=\s*\d+)? hp=(\w+) "
                      r"sc=\s*(\d+) st=(\w+) lives=(\d+)")


def standings(line):
    """The comparable core of a search log line: which labels ended where, in
    the order the search ranked them. Spacing and column widths are not."""
    return STANDING.findall(line)


if __name__ == "__main__":
    sys.exit(main())
