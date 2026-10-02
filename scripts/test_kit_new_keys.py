#!/usr/bin/env python3
"""scripts/kit_new_keys.py: the clause-2 key measurement (F14.20, ADR-0238 sec. 5).

No ROM and no emulator: every pack is a hires.txt written here.
"""
import io
import sys
import tempfile
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kit_new_keys  # noqa: E402

FAILURES = []

P1 = "007CC6C6C6FEC6C6FF83393939013939"
P2 = "00FCC6C6FCC6C6FCFF03393903393903"
P3 = "00400000040000420000002000800800"


def check(cond, msg):
    print(("ok   " if cond else "FAIL ") + msg)
    if not cond:
        FAILURES.append(msg)


def write(path: Path, version: int, *tiles):
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"<ver>{version}", "<scale>4", "<img>chr/Chr_0.png"]
    lines += [f"<tile>0,{tile},{pal},0,0,1,N" for tile, pal in tiles]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def test_keys_ignore_conditions_and_case(tmp: Path):
    path = tmp / "cond" / "hires.txt"
    write(path, 109, (P1, "0F20112C"))
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(f"[stage2]<tile>0,{P1.lower()},0f20112c,32,0,1,N\n")
    rules, keys = kit_new_keys.hires_keys(path)
    check(rules == 2 and len(keys) == 1,
          "a conditioned rule and a lowercase spelling are the same key (2 rules, 1 key)")


def test_index_is_decimal_below_ver_103(tmp: Path):
    old = write(tmp / "old" / "hires.txt", 101, ("16", "0F302C15"))
    new = write(tmp / "new" / "hires.txt", 106, ("10", "0F302C15"))
    _, old_keys = kit_new_keys.hires_keys(old)
    _, new_keys = kit_new_keys.hires_keys(new)
    check(old_keys == new_keys == {("#16", "0F302C15")},
          "index 16 written decimal under <ver>101 equals 10 written hex under <ver>106")


def test_new_keys_against_the_baseline_union(tmp: Path):
    cand = write(tmp / "cand" / "hires.txt", 109, (P1, "0F20112C"), (P2, "0F20112C"), (P3, "0F0F0F0F"))
    base_a = write(tmp / "a" / "hires.txt", 109, (P1, "0F20112C"))
    base_b = write(tmp / "b" / "hires.txt", 109, (P2, "0F20112C"), (P2, "0F0F0F0F"))
    report = kit_new_keys.measure([kit_new_keys.load("cand", str(cand))],
                                  [kit_new_keys.load("a", str(base_a)), kit_new_keys.load("b", str(base_b))])
    cand_row = report["candidates"][0]
    check(cand_row["new_vs_baselines"] == 1, "one key (P3) is in no baseline")
    check(report["baseline_union_keys"] == 3, "the baselines' union has 3 keys")
    unique = {row["label"]: row["unique"] for row in report["packs"]}
    check(unique == {"cand": 1, "a": 0, "b": 1}, f"per-pack unique keys {unique}")
    #The same palette on another tile, or the same tile under another palette, is a new key
    check(report["packs"][0]["namespace"] == "pattern", "a 32-digit tileData is a CHR RAM pattern")


def test_a_project_unions_every_recording(tmp: Path):
    project = tmp / "Game"
    write(project / "auto" / "textures" / "hires.txt", 109, (P1, "0F20112C"))
    write(project / "auto" / "rec-002" / "textures" / "hires.txt", 109, (P2, "0F20112C"))
    files = kit_new_keys.hires_files(project)
    check(len(files) == 2, "a project reads the bare auto/textures (rec-001) and rec-002")
    pack = kit_new_keys.load("game", str(project))
    check(len(pack["keys"]) == 2, "and unions their keys")


def test_cli_prints_the_tables(tmp: Path):
    cand = write(tmp / "c2" / "hires.txt", 109, (P1, "0F20112C"), (P3, "0F0F0F0F"))
    base = write(tmp / "b2" / "hires.txt", 109, (P1, "0F20112C"))
    out = io.StringIO()
    with redirect_stdout(out):
        code = kit_new_keys.main(["--candidate", f"ai={cand}", "--baseline", f"route={base}",
                                  "--json", str(tmp / "report.json")])
    text = out.getvalue()
    check(code == 0 and "| `ai` | 2 | 1 | 1 |" in text, "the candidate row reads 2 keys, 1 new")
    check((tmp / "report.json").is_file(), "--json writes the report")


def main() -> int:
    with tempfile.TemporaryDirectory() as raw:
        tmp = Path(raw)
        for test in (test_keys_ignore_conditions_and_case, test_index_is_decimal_below_ver_103,
                     test_new_keys_against_the_baseline_union, test_a_project_unions_every_recording,
                     test_cli_prints_the_tables):
            sub = tmp / test.__name__
            sub.mkdir()
            test(sub)
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
