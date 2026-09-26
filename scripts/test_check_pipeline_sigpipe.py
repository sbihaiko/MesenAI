#!/usr/bin/env python3
"""Issue #516: no script may read a pipeline with an early-exit reader.

`set -o pipefail` makes a pipeline's status the last non-zero one, and a reader
that stops at the first answer closes its end of the pipe: `grep -q` on its
match, `head` after its line count, `read` after one line, `sed`/`awk` at their
`q`/`exit`. A writer that has not finished (`printf`, `echo`, another `grep`,
`sed`, `find`, `gh`) is then killed by SIGPIPE and the pipeline reports 141 -
which a check like scripts/checks/verify_community_pack_labels_script.sh reads
as "label missing", and which aborts a `VAR=$(...)` assignment outright under
`set -e`. A writer's output leaves the shell in as many writes as its buffer
needs, so losing the race needs the writer to be descheduled mid-stream: on
2026-09-25 that flaked twice under load average ~100, each time naming a
different label, with the checked file byte-identical across the run.

Two assertions:

  1. behaviourally, on a copy of the repo whose LABELS array is padded past the
     pipe buffer, so the reader always outruns the writer and the race stops
     being a race - RED before the fix, PASS after it;
  2. structurally, over every shell script under scripts/ and every inline
     shell block of the workflows in .github/workflows/, so the pattern cannot
     come back. This is what covers the membership loop in the labels check,
     whose input (15 short names) is too small to force the race from outside.

The rule has no carve-outs: a pipeline masked by `|| true` cannot fail a script,
but a masked writer still dies silently, and the rewrite that avoids both (read
to the end - drop `-q`, redirect to /dev/null, take the first line with
`sed -n '1p'`, drop `exit` from an awk program) is the same one line.

Run: python3 scripts/test_check_pipeline_sigpipe.py
"""
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REPO_ROOT / "scripts"
WORKFLOWS = REPO_ROOT / ".github" / "workflows"
LABELS_CHECK = SCRIPTS / "checks" / "verify_community_pack_labels_script.sh"
LABELS_SCRIPT = SCRIPTS / "ensure_community_pack_labels.sh"

# Long enough to put the LABELS array far past the 16 KiB a pipe holds on
# macOS, so printf blocks mid-write and the reader always gets its match (and
# exits) first.
PADDING = 20000

# The check under test reads its input with sed/grep; 40 shell files is a floor
# no healthy checkout can drop below, and it keeps a broken glob from passing.
MIN_SCANNED_FILES = 40

FAILURES = []


def ok(msg):
    print(f"PASS {msg}")


def fail(msg):
    print(f"FAIL {msg}")
    FAILURES.append(msg)


# --- assertion 1: the labels check itself -----------------------------------

def real_labels():
    """The (name, colour, description) triples the shipped script declares."""
    block = re.search(
        r"^LABELS=\((.*?)^\)$", LABELS_SCRIPT.read_text(encoding="utf-8"), re.S | re.M
    )
    if not block:
        return []
    return re.findall(r'"([^"|]+)\|([0-9A-Fa-f]{6})\|([^"]*)"', block.group(1))


def build_fixture(root, entries):
    """A repo carrying only the two files the labels check reads.

    The check resolves its paths from its own location, so a copy under
    <root>/scripts/checks/ verifies <root>'s labels script - which is what
    makes a padded array reachable without touching the real one.
    """
    (root / "scripts" / "checks").mkdir(parents=True)
    shutil.copy2(LABELS_CHECK, root / "scripts" / "checks" / LABELS_CHECK.name)
    lines = ["#!/usr/bin/env bash", "set -euo pipefail", "LABELS=("]
    lines += [f'  "{name}|{colour}|{description}"' for name, colour, description in entries]
    lines.append(")")
    (root / "scripts" / "ensure_community_pack_labels.sh").write_text(
        "\n".join(lines) + "\n", encoding="utf-8"
    )


def run_labels_check(root):
    check = root / "scripts" / "checks" / LABELS_CHECK.name
    return subprocess.run(
        ["bash", str(check)], capture_output=True, text=True, cwd=str(root)
    )


def vibe(proc):
    """The first line the check printed, for the failure message."""
    output = (proc.stderr + proc.stdout).strip()
    return output.splitlines()[0] if output else "<no output>"


def test_padded_labels_array_passes():
    entries = real_labels()
    if len(entries) < 15:
        fail(f"could not read the LABELS array from {LABELS_SCRIPT.relative_to(REPO_ROOT)}")
        return

    padded = [(n, c, d + "x" * PADDING) for n, c, d in entries]
    # assets:external first: the check's own `grep -qF '"assets:external|'` then
    # matches in its first read and exits while the writer still has ~300 KiB
    # queued - #516's flake, made deterministic.
    padded.sort(key=lambda entry: entry[0] != "assets:external")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        build_fixture(root, padded)
        size = (root / "scripts" / "ensure_community_pack_labels.sh").stat().st_size
        proc = run_labels_check(root)

    if proc.returncode == 0:
        ok(f"the labels check survives a {size}-byte LABELS array (a pipe holds 16 KiB)")
    else:
        fail(
            f"the labels check failed on a padded LABELS array (exit {proc.returncode}): "
            f"{vibe(proc)}"
        )


def test_duplicate_label_still_fails():
    """Teeth: the padded fixture's PASS must not come from a check gone silent."""
    entries = real_labels()
    if len(entries) < 15:
        fail(f"could not read the LABELS array from {LABELS_SCRIPT.relative_to(REPO_ROOT)}")
        return

    # Still 15 entries, one name twice: only the uniqueness guard can catch it.
    padded = [(n, c, d + "x" * PADDING) for n, c, d in entries[:-1] + [entries[0]]]

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        build_fixture(root, padded)
        proc = run_labels_check(root)

    combined = proc.stdout + proc.stderr
    if proc.returncode != 0 and "unique names" in combined:
        ok("a duplicated label name still fails, naming the uniqueness rule")
    else:
        fail(
            f"a LABELS array with 15 entries and 14 unique names passed (exit "
            f"{proc.returncode}): {vibe(proc)}"
        )


# --- assertion 2: no pipeline stage stops reading before its input ends -----

KEYWORDS = {
    "if", "then", "elif", "else", "while", "until", "do", "done", "!", "{", "}",
    "(", ")", "time", "local", "export", "command", "exec", "return", "eval",
}
TOKEN_RE = re.compile(r"'([^']*)'|\"([^\"]*)\"|(\S+)")

# sed's `q` command, and awk's `exit`, as statements rather than as text inside
# a pattern: a `q` right after `/` is part of a regex (`sed -n '/q/p'`), and so
# is an `exit` (`awk '/exit/ {print}'`).
SED_QUIT_RE = re.compile(r"(?:^|[;{/\s])[0-9]*q(?:[;}]|$|\s)")
AWK_EXIT_RE = re.compile(r"(?:^|[;{\s])exit(?:[;}\s]|$)")


def strip_quotes(segment):
    """Drop quoted content so a literal '-q' argument is not read as a flag."""
    out = []
    quote = None
    for ch in segment:
        if quote:
            if ch == quote:
                quote = None
            continue
        if ch in "'\"":
            quote = ch
            out.append(" ")
            continue
        out.append(ch)
    return "".join(out)


def command_word(segment):
    """The command a segment runs, skipping keywords, `!` and `VAR=x` prefixes."""
    for token in segment.replace("$(", " ").split():
        word = token.strip("!(){}\"'`;")
        if not word or word in KEYWORDS or word.startswith("-") or "=" in word:
            continue
        return word
    return None


def flags_of(segment):
    """grep's option tokens, stopping at `--` (its end-of-options marker)."""
    tokens = strip_quotes(segment).split()
    flags = []
    seen_command = False
    for token in tokens:
        word = token.strip("!(){}\"'`;")
        if not seen_command:
            if not word or word in KEYWORDS or word.startswith("-") or "=" in word:
                continue
            seen_command = True
            continue
        if token == "--":
            break
        if token.startswith("-") and token != "-":
            flags.append(token)
    return flags


def words(segment):
    """Tokens with their quotes removed, so a quoted sed/awk program stays readable.

    Deliberately not the flag scanner's input: a grep *pattern* is data (a
    literal '-q' argument is not the option), while a sed/awk *program* is the
    thing being inspected.
    """
    return [next(group for group in match.groups() if group is not None)
            for match in TOKEN_RE.finditer(segment)]


def is_early_exit_reader(segment):
    """What makes this segment stop reading before its input ends, if anything."""
    command = command_word(strip_quotes(segment))
    if command in ("grep", "egrep", "fgrep"):
        for flag in flags_of(segment):
            if flag.startswith("--"):
                if flag in ("--quiet", "--silent") or flag.startswith("--max-count"):
                    return flag
                continue
            # grep has no other short option spelled with q, l or m.
            if any(ch in "qlm" for ch in flag[1:]):
                return flag
        return None
    if command == "head":
        return "head"
    if command == "read":
        return "read"
    tokens = words(segment)[1:]  # the command word (or a keyword before it)
    if command == "sed" and any(SED_QUIT_RE.search(token) for token in tokens):
        return "sed q"
    if command in ("awk", "gawk", "mawk") and any(AWK_EXIT_RE.search(token) for token in tokens):
        return "awk exit"
    return None


def split_pipelines(line):
    """The pipelines in one logical line: a list of segment lists.

    Quotes are honoured, so a `|` inside a pattern (`grep -qE 'a|b'`) or inside
    `gh --jq '.[].name'` is literal. A command substitution is not: the pipes in
    `X="$(grep -n f | head -1 | cut -d: -f1)"` are real ones, which is what a
    quote-only splitter misses. `||`, `&&`, `&` and `;` end the pipeline in
    progress, like the shell does.
    """
    pipelines, current, buf = [], [], []
    stack = []  # "'" and '"' for open quotes, "sub" for a $( ) opened inside one
    escaped = False
    i = 0
    while i < len(line):
        ch = line[i]
        top = stack[-1] if stack else None
        if top == "'":
            buf.append(ch)
            if ch == "'":
                stack.pop()
            i += 1
            continue
        if top == '"':
            buf.append(ch)
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == "$" and line[i + 1:i + 2] == "(":
                stack.append("sub")
                buf.append("(")
                i += 2
                continue
            elif ch == '"':
                stack.pop()
            i += 1
            continue
        if ch == "\\":
            buf.append(ch)
            if i + 1 < len(line):
                buf.append(line[i + 1])
            i += 2
            continue
        if ch in "'\"":
            stack.append(ch)
            buf.append(ch)
            i += 1
            continue
        if ch == "$" and line[i + 1:i + 2] == "(":
            stack.append("sub")
            buf.append(ch)
            buf.append("(")
            i += 2
            continue
        if top == "sub" and ch == ")":
            stack.pop()
            buf.append(ch)
            i += 1
            continue
        if ch == "|" and line[i + 1:i + 2] != "|":
            current.append("".join(buf))
            buf = []
            i += 1
            continue
        if ch in "|;&":
            current.append("".join(buf))
            buf = []
            pipelines.append(current)
            current = []
            i += 2 if line[i + 1:i + 2] == ch else 1
            continue
        buf.append(ch)
        i += 1
    current.append("".join(buf))
    pipelines.append(current)
    return [segments for segments in pipelines if len(segments) >= 2]


def logical_lines(text):
    """(first lineno, text) with backslash continuations folded in."""
    out = []
    pending = None
    for lineno, raw in enumerate(text.splitlines(), start=1):
        if pending is None:
            pending = [lineno, raw]
        else:
            pending[1] += raw
        if raw.rstrip().endswith("\\"):
            pending[1] = pending[1].rstrip()[:-1]
            continue
        out.append((pending[0], pending[1]))
        pending = None
    if pending is not None:
        out.append((pending[0], pending[1]))
    return out


def scan_script(path):
    """Every (lineno, line, reader) where a non-first pipeline stage stops early.

    Line-based, so it works on a shell script and on the indented shell inside a
    workflow's `run:` block alike; a YAML comment and a shell comment are both
    skipped by the same '#'. `run: |` itself is not a pipeline (its second
    segment is empty), so block-scalar markers pass through.
    """
    findings = []
    for lineno, line in logical_lines(path.read_text(encoding="utf-8", errors="replace")):
        if line.lstrip().startswith("#"):
            continue
        for segments in split_pipelines(line):
            for segment in segments[1:]:
                reader = is_early_exit_reader(segment)
                if reader:
                    findings.append((lineno, line.strip(), reader))
                    break
    return findings


def reader_on(line):
    """The reader the scanner reports for a synthetic line, or None."""
    for segments in split_pipelines(line):
        for segment in segments[1:]:
            reader = is_early_exit_reader(segment)
            if reader:
                return reader
    return None


def test_scanner_rules():
    """Pins what the scanner does see and what it must not.

    Without this the guard could go vacuous (a rule that never fires) or start
    flagging lookalikes (a `q` inside a sed regex, a first-stage head) and the
    suite would still say PASS.
    """
    cases = [
        # A reader that stops early, in each of its forms.
        ("""printf '%s\\n' "$x" | grep -q foo""", "-q"),
        ("""grep -n foo file | head -1 | cut -d: -f1""", "head"),
        ("""find "$out" -name '*.png' | head -1""", "head"),
        # No carve-out for a masked pipeline: the writer still dies silently.
        ("""cmd | head -n1 || true""", "head"),
        ("""cat f | read -r x""", "read"),
        ("""cat f | sed -n '1p;q'""", "sed q"),
        ("""otool -l "$1" | awk '/LC_UUID/ { f = 1 } f && $1 == "uuid" { print $2; exit }'""", "awk exit"),
        # gh's own -q is not a grep flag, and the grep after it is.
        ("""gh label list --json name -q '.[].name' | grep -qx label""", "-qx"),
        # The fixes, and the lookalikes that must stay quiet.
        ("""find "$out" -name '*.png' | sed -n '1p'""", None),
        ("""cat f | sed -n '/q/p'""", None),
        ("""cat f | awk '/exit/ {print}'""", None),
        ("""head -1 file | grep foo""", None),
        ("""python3 x.py | grep -E 'a|b' >/dev/null""", None),
        # A pipeline inside a command substitution is a pipeline, even when the
        # substitution sits inside a double-quoted string (the blind spot that
        # let the first head mutation through).
        ("""LINE="$(grep -n foo "$DOC" | head -1 | cut -d: -f1)\"""", "head"),
        ("""X="$(grep -vE '^#' f | grep -qE 'a|b')\"""", "-qE"),
        ("""X="$(grep -E "a|b" f)\"""", None),
        ("""X="$(cat f | sed -n '1p')\"""", None),
        # A workflow block scalar is not a pipeline.
        ("        run: |", None),
    ]
    wrong = []
    for line, expected in cases:
        got = reader_on(line)
        if got != expected:
            wrong.append(f"{line!r}: expected {expected!r}, got {got!r}")
    if wrong:
        fail("the scanner's rules are wrong:\n  " + "\n  ".join(wrong))
    else:
        ok(f"the scanner fires on all {len(cases)} reader forms and on none of the lookalikes")


def test_no_early_exit_reader_in_a_pipeline():
    scanned = sorted(SCRIPTS.rglob("*.sh")) + sorted(WORKFLOWS.glob("*.yml"))
    if LABELS_CHECK not in scanned:
        fail(f"{LABELS_CHECK.relative_to(REPO_ROOT)} is not under the scanned tree")
        return
    if len(scanned) < MIN_SCANNED_FILES:
        fail(f"only {len(scanned)} shell files found under scripts/ and .github/workflows/, expected at least {MIN_SCANNED_FILES}")
        return

    findings = []
    for path in scanned:
        for lineno, line, reader in scan_script(path):
            findings.append(f"{path.relative_to(REPO_ROOT)}:{lineno}: {reader} stops reading a pipeline - {line}")

    if findings:
        fail(
            "a pipeline stage stops reading before its input ends; under `set -o "
            "pipefail` the writer's SIGPIPE becomes exit 141, which a check reads "
            "as a missing string and `VAR=$(...)` reads as a fatal error (#516). "
            "Read to the end instead - drop -q and redirect to /dev/null, take "
            "the first line with `sed -n '1p'`, drop `exit` from the awk "
            "program:\n  " + "\n  ".join(findings)
        )
    else:
        ok(f"no pipeline stage stops reading early in the {len(scanned)} shell files scanned")


def main():
    if shutil.which("bash") is None:
        print("SKIP bash not available")
        return 0
    test_padded_labels_array_passes()
    test_duplicate_label_still_fails()
    test_scanner_rules()
    test_no_early_exit_reader_in_a_pipeline()
    print()
    if FAILURES:
        print(f"{len(FAILURES)} failure(s)")
        return 1
    print("All check-pipeline SIGPIPE checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
