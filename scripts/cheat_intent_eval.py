#!/usr/bin/env python3
"""Measure `cheat_intent.py` on the fixed intent set (P.11, ADR-0245 Consequences).

Runs every case of `tests/fixtures/cheat-intent/intents.json` through one
backend and reports, per backend: the share answered with an acceptable entry
(NONE counts when NONE is the expected answer), the share discarded by the
closed-list check, latency, and cost. Writes one JSON with every case's result.

Jev spend stops *before* the cap: a call is skipped once the money spent plus
twice the most expensive call so far would pass `--budget`.

Usage:
  python3 scripts/cheat_intent_eval.py --backend ollama [--model M] [--loose] --out runs/x.json
  python3 scripts/cheat_intent_eval.py --backend jev --budget 0.10 --out runs/y.json

`--loose` asks Ollama for plain JSON instead of the enum-constrained schema, to
see how often an unconstrained model leaves the list (the check's own path).
"""
from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cheat_intent  # noqa: E402
import jev_client  # noqa: E402

CASES = cheat_intent.ROOT / "tests" / "fixtures" / "cheat-intent" / "intents.json"


def load_cases(path=CASES) -> list:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)["cases"]


def score(case, result) -> bool:
    accepted = set(case["accept"])
    if result["status"] == "match":
        return result["entry"]["id"] in accepted
    if result["status"] == "none":
        return cheat_intent.NONE_ID in accepted
    return False


def summarize(rows) -> dict:
    answered = [row for row in rows if row.get("result")]
    latencies = [row["result"]["latency_s"] for row in answered]
    n = len(answered)
    expect_none = [row for row in answered if row["accept"] == [cheat_intent.NONE_ID]]
    expect_entry = [row for row in answered if row["accept"] != [cheat_intent.NONE_ID]]
    return {
        "cases": n,
        "skipped": len(rows) - n,
        "correct": sum(row["correct"] for row in answered),
        "share_correct": round(sum(row["correct"] for row in answered) / n, 4) if n else None,
        "correct_when_entry_expected": f"{sum(r['correct'] for r in expect_entry)}/{len(expect_entry)}",
        "correct_when_none_expected": f"{sum(r['correct'] for r in expect_none)}/{len(expect_none)}",
        # The harmful miss: an entry offered that does something else. A wrong
        # NONE only sends the player back to the text search.
        "wrong_entry": sum(row["result"]["status"] == "match" and not row["correct"]
                           for row in answered),
        "discarded": sum(row["result"]["status"] == "discarded" for row in answered),
        "share_discarded": round(
            sum(row["result"]["status"] == "discarded" for row in answered) / n, 4) if n else None,
        "latency_s_median": round(statistics.median(latencies), 3) if latencies else None,
        "latency_s_max": round(max(latencies), 3) if latencies else None,
        "cost_usd_total": round(sum(row["result"]["cost_usd"] for row in answered), 8),
    }


def run(backend, cases, *, budget=None, client=None, progress=sys.stderr) -> list:
    games = {g["sha1"].upper(): g for g in cheat_intent.load_db()}
    rows = []
    max_call = 0.0
    for number, case in enumerate(cases, 1):
        row = {"game": case["game"], "intent": case["intent"], "accept": case["accept"]}
        if client is not None and budget is not None and client.spent_usd + 2 * max_call > budget:
            row["skipped"] = "budget"
            rows.append(row)
            continue
        game = games[case["sha1"].upper()]
        result = cheat_intent.answer(backend, game["name"], game["sha1"], case["intent"], game["cheats"])
        max_call = max(max_call, result["cost_usd"])
        row["result"] = result
        row["correct"] = score(case, result)
        rows.append(row)
        chosen = result["entry"]["id"] if result["entry"] else result["status"]
        print(f"[{number}/{len(cases)}] {'ok ' if row['correct'] else 'BAD'} "
              f"{case['game']} | {case['intent']} -> {chosen}", file=progress)
    return rows


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--backend", choices=("ollama", "jev"), default="ollama")
    parser.add_argument("--model", default=cheat_intent.DEFAULT_OLLAMA_MODEL)
    parser.add_argument("--loose", action="store_true", help="Ollama without the enum schema")
    parser.add_argument("--budget", type=float, default=0.10)
    parser.add_argument("--cases", default=str(CASES))
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)
    cases = load_cases(args.cases)
    client = None
    if args.backend == "ollama":
        backend = cheat_intent.OllamaBackend(args.model, constrain=not args.loose)
    else:
        try:
            client = jev_client.JevClient(budget_usd=args.budget, log_path=None)
        except jev_client.MissingKeyError as exc:
            print(f"cheat_intent_eval: {exc}", file=sys.stderr)
            return 2
        backend = cheat_intent.JevBackend(client)
    rows = run(backend, cases, budget=args.budget if client else None, client=client)
    summary = summarize(rows)
    summary.update({"backend": backend.name, "model": getattr(backend, "model", ""),
                    "constrained": not args.loose if args.backend == "ollama" else None})
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"summary": summary, "rows": rows}, indent=1, ensure_ascii=False),
                   encoding="utf-8")
    print(json.dumps(summary, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
