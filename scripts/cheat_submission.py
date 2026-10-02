#!/usr/bin/env python3
"""Issue-level gate of ADR-0248 sections 1 and 3, for cheat-submitted.yml.

Given a `[Cheat]` submission issue (the Issue Form
.github/ISSUE_TEMPLATE/cheat-code.yml) it reads the form's four fields - Game
(SHA-1 and name), Console, Code, Description - and decides:

  * the verdict, `cheat:valid` when every structural check holds, else
    `cheat:invalid`:
      - `game-sha1`     the SHA-1 is 40 hex digits;
      - `unknown-game`  a game the bundled list or the repository's No-Intro
                        data (scripts/rom_target.py, docs/community-packs.json)
                        knows;
      - `console`       one of the form's consoles, and the one the bundled
                        list files the SHA-1 under (`console-mismatch`);
      - `code`          every `+`-joined part decodes for the console's cheat
                        types (scripts/cheat_decoder.py, the Core's decoders
                        ported and held to them by
                        scripts/test_cheat_decoder_parity.py);
      - `description`   one line, 1-80 characters, no links;
      - `duplicate`     no earlier live `cheat:valid` issue and no bundled
                        entry has the same SHA-1 and the same normalised code;
  * the console label (`console:nes`/`gb`/`gbc`/`sms`, ADR-0248 section 7);
  * the comment the workflow posts, naming every failed check and saying the
    code was "checked for form, not for effect";
  * the title, rewritten whole as `[Cheat] <game> — <description>` (section 1).

Nothing else the submitter wrote reaches a verdict: only the four fields are
read off the body. Nothing here talks to GitHub: the workflow passes the issue
and the live `cheat:valid` issues as files and applies the JSON this prints.

Usage (the workflow's call):
  python3 scripts/cheat_submission.py --body-file B --title T --number N
      [--labels "cheat,cheat:invalid"] [--live-file live.json]   > verdict.json

`live.json` is `gh issue list --label cheat:valid --state open --json number,body`.

Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from collections import namedtuple
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPTS))
import cheat_decoder as cd  # noqa: E402

ROOT = SCRIPTS.parent
BUNDLED = ROOT / "UI" / "Dependencies" / "Internal" / "CheatDb.Nes.json"
CATALOG = ROOT / "docs" / "community-packs.json"

LABEL_SUBMITTED = "cheat"
LABEL_VALID = "cheat:valid"
LABEL_INVALID = "cheat:invalid"

TITLE_PREFIX = "[Cheat] "
SEPARATOR = " — "
TITLE_MAX = 250
DESCRIPTION_MAX = 80
COMMENT_MARKER = "<!-- cheat-verdict -->"

# Issue Form labels (cheat-code.yml) -> field keys. Only these are read.
FIELD_LABELS = {
    "Game SHA-1": "sha1",
    "Game name": "game",
    "Console": "console",
    "Code": "code",
    "Description": "description",
}
# The form's dropdown options -> the console key and its label.
CONSOLES = {
    "NES": "nes",
    "Game Boy": "gb",
    "Game Boy Color": "gbc",
    "Master System / Game Gear": "sms",
}
CONSOLE_LABELS = {key: f"console:{key}" for key in ("nes", "gb", "gbc", "sms")}

SHA1_RE = re.compile(r"^[0-9A-F]{40}$")
_HEADING_RE = re.compile(r"^###[ \t]+(.+?)[ \t]*$", re.M)
# A description must not carry a link: a scheme, `www.`, a Markdown/HTML link,
# or a bare host name on a common top-level domain.
_LINK_RES = (
    re.compile(r"[a-z][a-z0-9+.-]*://", re.I),
    re.compile(r"\bwww\.", re.I),
    re.compile(r"\]\(|<a\s|<https?:", re.I),
    re.compile(r"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|net|org|io|gg|ly|co|me|tv|xyz|info|biz|app|dev|site|link|to|be|cc|us|uk|de|br|ru|jp)\b(/|$|\s|[.,;:!?)])", re.I),
)

Verdict = namedtuple("Verdict", "verdict title title_changed labels_add labels_remove comment fields")


def parse_form(body):
    """{field key: value} for the five form fields, from the Markdown GitHub
    renders for an Issue Form submission (`### <label>` then the value).
    `_No response_` is an empty field."""
    fields = {key: "" for key in FIELD_LABELS.values()}
    matches = list(_HEADING_RE.finditer(body or ""))
    for i, m in enumerate(matches):
        key = FIELD_LABELS.get(m.group(1).strip())
        if key is None:
            continue
        end = matches[i + 1].start() if i + 1 < len(matches) else len(body)
        value = body[m.end():end].strip()
        fields[key] = "" if value == "_No response_" else value
    return fields


def load_bundled(path=BUNDLED):
    """(names by SHA-1, normalised codes by SHA-1) from the bundled NES list.
    Each entry is decoded the way the UI loads it; an entry the Core refuses
    has no effect to duplicate and is skipped."""
    names, codes = {}, {}
    try:
        games = json.loads(Path(path).read_text(encoding="utf-8-sig"))["games"]
    except (OSError, ValueError, KeyError):
        return names, codes
    for game in games:
        sha1 = (game.get("sha1") or "").upper()
        names[sha1] = game.get("name", "")
        for cheat in game.get("cheats", []):
            entry = cheat.get("code", "")
            t = cd.bundled_type(entry)
            decoded = [cd.decode(t, part) for part in cd.bundled_parts(entry)]
            if decoded and all(decoded):
                codes.setdefault(sha1, set()).add(cd.normalise(decoded))
    return names, codes


def load_no_intro(catalog_path=CATALOG):
    """{SHA-1: game name} from the repository's No-Intro data: the ROM-target
    map of scripts/rom_target.py and the accepted pack catalog's ROM hashes."""
    known = {}
    try:
        import rom_target
        for name, target in rom_target.NO_INTRO_TARGETS.items():
            for sha1 in [target.get("sha1", "")] + list(target.get("alt_sha1", [])):
                if sha1:
                    known.setdefault(sha1.upper(), name)
    except ImportError:
        pass
    try:
        rows = json.loads(Path(catalog_path).read_text(encoding="utf-8")).get("packs", [])
    except (OSError, ValueError):
        rows = []
    for row in rows:
        rom = row.get("rom") or {}
        for sha1 in [rom.get("sha1", "")] + list(rom.get("sha1s", []) or []):
            if sha1:
                known.setdefault(sha1.upper(), row.get("game", ""))
    return known


def plain(text):
    """Submitter text for a title: control and format (bidi) characters
    dropped, whitespace collapsed."""
    kept = "".join(" " if unicodedata.category(c) in ("Zl", "Zp") else c
                   for c in (text or "") if unicodedata.category(c) not in ("Cc", "Cf"))
    return " ".join(kept.split())


def defang(text):
    """Text echoed in a comment: no code-span break-out, no @mention, no #ref."""
    return plain(text).replace("`", "'").replace("@", "@​").replace("#", "#​")


def has_link(text):
    return any(r.search(text) for r in _LINK_RES)


def check_description(text):
    """The one editorial rule (ADR-0248 section 1): a reason, or None."""
    if not text:
        return "the description is empty: say in one line what the code does"
    if "\n" in text or "\r" in text:
        return "the description must be one line"
    if any(unicodedata.category(c) in ("Cc", "Cf", "Zl", "Zp") for c in text):
        return "the description contains control or formatting characters"
    if len(text) > DESCRIPTION_MAX:
        return f"the description is {len(text)} characters; the limit is {DESCRIPTION_MAX}"
    if has_link(text):
        return "the description contains a link; links are not allowed"
    return None


def split_code(code):
    """The parts of one effect: joined with `+`, whitespace around them ignored."""
    return [p.strip() for p in (code or "").split("+")]


def decode_submission(console, code):
    """([Decoded], [reason]) for every part of a submitted code."""
    decoded, reasons = [], []
    parts = split_code(code)
    if not code or not code.strip():
        return [], ["no code was given"]
    for i, part in enumerate(parts, 1):
        label = f"part {i} (`{defang(part)}`)" if len(parts) > 1 else f"`{defang(part)}`"
        if not part:
            reasons.append(f"part {i} is empty: join codes with a single `+`")
            continue
        if not part.isascii() or len(part) > cd.INTEROP_CODE_BYTES:
            reasons.append(f"{label} is not a cheat code")
            continue
        d = cd.decode_for_console(console, part)
        if d is None:
            types = ", ".join(cd.TYPE_NAMES[t] for t in cd.CONSOLE_TYPES[console])
            reasons.append(f"{label} does not decode as any of: {types}")
        else:
            decoded.append(d)
    return decoded, reasons


def live_duplicate(sha1, normalised, number, live):
    """The lowest-numbered earlier live `cheat:valid` issue with the same SHA-1
    and normalised code, else None. Earlier only: re-validating the first
    submission never makes it a duplicate of a later copy."""
    for issue in sorted(live, key=lambda i: i.get("number", 0)):
        other = issue.get("number", 0)
        if other == number or (number and other > number):
            continue
        f = parse_form(issue.get("body", ""))
        console = CONSOLES.get(f["console"])
        if console is None or f["sha1"].strip().upper() != sha1:
            continue
        decoded, reasons = decode_submission(console, f["code"])
        if not reasons and cd.normalise(decoded) == normalised:
            return other
    return None


def build_title(game, description, issue_title):
    game = plain(game) or "Unknown game"
    desc = plain(description)
    if len(desc) > DESCRIPTION_MAX:
        desc = desc[: DESCRIPTION_MAX - 1].rstrip() + "…"
    title = TITLE_PREFIX + game + (SEPARATOR + desc if desc else "")
    if len(title) > TITLE_MAX:
        title = title[: TITLE_MAX - 1].rstrip() + "…"
    return title


def _comment(valid, findings, decoded, console_key, game, sha1):
    blocks = [COMMENT_MARKER]
    if valid:
        effect = "; ".join(_describe(d) for d in decoded)
        blocks.append(f"**Accepted.** The code decodes for {_console_name(console_key)} "
                      f"({effect}) on **{defang(game)}** (`{sha1}`), the description is within bounds, "
                      "and neither the bundled list nor an earlier community submission has it.")
    else:
        blocks.append("**Not accepted.** The submission failed the check(s) below. "
                      "Edit the issue to fix them; the bot re-checks on every edit, or comment `/revalidate`.")
        blocks.append("\n".join(f"- `{check}`: {message}" for check, message in findings))
    caveat = ("Checked for form, not for effect: CI has no ROM, so this bot verified the code's structure, "
              "the game's SHA-1, the description and that the code is new - not that it works in the game "
              "(ADR-0248).")
    if valid:
        caveat += " If it worked for you, add a 👍 to this issue."
    blocks.append(caveat)
    return "\n\n".join(blocks)


def _console_name(key):
    return {v: k for k, v in CONSOLES.items()}.get(key, key)


def _describe(d):
    text = f"{cd.TYPE_NAMES.get(d.type, d.type)}: ${d.address:04X} = ${d.value:02X}"
    if d.compare >= 0:
        text += f" if ${d.compare:02X}"
    return text


def evaluate(issue_body, issue_title, issue_labels, number, live, bundled, no_intro):
    """Pure verdict for one submission. `bundled` is load_bundled()'s pair,
    `no_intro` load_no_intro()'s map, `live` the open `cheat:valid` issues."""
    names, bundled_codes = bundled
    f = parse_form(issue_body)
    findings = []

    sha1 = f["sha1"].strip().upper()
    sha1_ok = bool(SHA1_RE.match(sha1))
    if not sha1_ok:
        findings.append(("game-sha1", "the Game SHA-1 must be 40 hexadecimal digits "
                         f"(got {len(sha1)} characters)"))
    elif sha1 not in names and sha1 not in no_intro:
        findings.append(("unknown-game", "neither the bundled cheat list nor the repository's No-Intro data "
                         "knows this SHA-1; check that it is the hash of the ROM the cheat list uses"))

    console = CONSOLES.get(f["console"].strip())
    if console is None:
        findings.append(("console", f"pick one of the form's consoles: {', '.join(CONSOLES)}"))
    elif sha1_ok and sha1 in names and console != "nes":
        findings.append(("console-mismatch", f"this SHA-1 is an NES game in the bundled list, "
                         f"not {_console_name(console)}"))

    decoded = []
    if console is not None:
        decoded, reasons = decode_submission(console, f["code"])
        findings.extend(("code", r) for r in reasons)

    reason = check_description(f["description"].strip())
    if reason:
        findings.append(("description", reason))

    if sha1_ok and console is not None and decoded and not any(c == "code" for c, _ in findings):
        normalised = cd.normalise(decoded)
        if normalised in bundled_codes.get(sha1, set()):
            findings.append(("duplicate", "already in the bundled list for this game"))
        else:
            earlier = live_duplicate(sha1, normalised, number, live)
            if earlier is not None:
                findings.append(("duplicate", f"the same code for this game was already shared in #{earlier}"))

    valid = not findings
    typed = f["game"] if len(f["game"]) <= DESCRIPTION_MAX and not has_link(f["game"]) else ""
    game = names.get(sha1) or no_intro.get(sha1) or typed
    # A description that failed its bounds (a link, an overlong line) is not
    # copied into the title; the title then names the game alone.
    title = build_title(game, "" if reason else f["description"], issue_title)
    add = [LABEL_VALID if valid else LABEL_INVALID]
    remove = [LABEL_INVALID if valid else LABEL_VALID]
    for key, label in CONSOLE_LABELS.items():
        (add if key == console else remove).append(label)
    return Verdict(
        verdict="valid" if valid else "invalid",
        title=title,
        title_changed=title != issue_title,
        labels_add=add,
        labels_remove=remove,
        comment=_comment(valid, findings, decoded, console, game, sha1),
        fields=f,
    )


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--body-file", required=True)
    ap.add_argument("--title", required=True)
    ap.add_argument("--number", type=int, required=True)
    ap.add_argument("--labels", default="")
    ap.add_argument("--live-file", help="JSON list of the open cheat:valid issues (number, body)")
    args = ap.parse_args(argv[1:])

    body = Path(args.body_file).read_text(encoding="utf-8")
    labels = [x.strip() for x in args.labels.split(",") if x.strip()]
    live = json.loads(Path(args.live_file).read_text(encoding="utf-8")) if args.live_file else []
    verdict = evaluate(body, args.title, labels, args.number, live, load_bundled(), load_no_intro())
    print(json.dumps(verdict._asdict(), indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
