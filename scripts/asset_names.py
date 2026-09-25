"""The name a painting surface is written under, so the artist's own program
can export back onto it (F12.4, ADR-0213).

ADR-0209 gives MesenAI two jobs — **selection** (which art, named) and
**return** (the edited file coming back and rendering) — and delegates painting
to whatever program the artist already uses. F12.3 shipped the return: a PNG
overwritten on disk is re-decoded in place and rendered without reopening the
ROM. What that leaves is the file name itself, and a name is not a detail here:
the paint program reads it as a directive.

Three readers have to agree on every name the kit writes:

* **Photoshop's *Generate Image Assets*** turns a layer named `usr000.png`
  into a file of that name on every save. Its layer-name grammar is
  `[scale] name.ext[quality]`, comma-separated for several assets per layer —
  so a comma, a leading `2x `, or a `.png24` suffix silently change what is
  written, or write two files and neither is the surface.
* **Aseprite, Krita and GIMP** export to a path the artist picks once and
  repeat with one shortcut (`Repeat last export`, `Overwrite <file>`), so for
  them the name only has to be a name the file system keeps.
* **The file system**, including a Windows one the kit was not built on. A kit
  travels; a name that only opens here is not a name.

This module is the single place those rules live. Every generator that writes a
surface checks its name through `check_asset_name`, and anything whose name is
derived from outside input — a route file's stem, a recorded map's id — passes
it through `sanitize_asset_stem` first.

Two classes of caller, on purpose:

* a name **we** compose (`usr000.png`, `Chr_0.png`) must already be valid, and
  `require_asset_name` raises when it is not — that is our bug, not the
  artist's, and silently renaming it would move a file the manifest points at;
* a name **derived from input** is sanitized, and the caller records the
  original when it changed.

What this module does not do: it does not decide where the artist's program
writes. Photoshop's generator always writes into `<document>-assets/` beside
the `.psd` and that folder is not configurable, so on Photoshop the exported
file has the right *name* and lands one copy away from the kit — see
`docs/remastering-a-game.md`. Aseprite, Krita and GIMP overwrite the kit file
in place. The stop rule this module serves is names; the landing path is the
artist's program's business and the doc says so plainly.
"""

import re
from pathlib import PurePosixPath

# The only extension a painting surface is written under. Photoshop reads a
# trailing bit depth (`.png8`, `.png24`, `.png32`) as a directive: `.png24`
# generates a file still called `.png` but *without alpha*, which would flatten
# the transparency every surface depends on. Requiring the bare form keeps the
# default, which is PNG32.
SURFACE_EXT = ".png"

# Twins written beside a surface. They are read, never exported onto, so they
# are named by us and follow the same rules -- an artist who points their
# program at one of these by mistake should still not be able to produce a file
# name the kit cannot hold.
RESERVED_TWINS = (".orig.png", ".legend.png")

# Everything Windows refuses in a file name, plus the separators. `/` is listed
# for a second reason: in a Photoshop layer name it means a subfolder, so a
# surface named with one would be generated somewhere the artist did not look.
_ILLEGAL_CHARS = set('<>:"|?*\\/')

# CON, PRN, AUX, NUL, COM1-9, LPT1-9 -- reserved by Windows whatever the
# extension, so `aux.png` cannot be created there at all.
_RESERVED_STEMS = (
    {"CON", "PRN", "AUX", "NUL"}
    | {f"COM{i}" for i in range(1, 10)}
    | {f"LPT{i}" for i in range(1, 10)}
)

# Photoshop's relative-scale prefix: `200% name.png`, `2x name.png`,
# `100x50 name.png`. It is recognised only before a space, so a name with no
# space cannot trip it -- but a sanitized stem could grow one.
_SCALE_PREFIX = re.compile(r"^\s*\d+(\.\d+)?(%|[xX])\s")
_FIXED_SIZE_PREFIX = re.compile(r"^\s*\d+\s*[xX]\s*\d+\s")

# The longest name we let a generator emit. A kit folder plus a Photoshop
# `-assets` sibling plus this has to fit inside Windows' 260-character path
# limit with room for the artist's own directory; 100 leaves that room and is
# far above anything the generators compose.
MAX_NAME = 100

# Characters a sanitized stem may keep. Deliberately narrow: a kit travels
# through zips, a Windows share and three paint programs, and none of that is
# worth debugging for the sake of an accented route name.
_SAFE_STEM = re.compile(r"[^A-Za-z0-9._-]+")


def check_asset_name(name):
    """Return the reasons `name` cannot be used as a painting surface.

    An empty list means the name is usable by all three readers. Each reason is
    a full sentence naming the reader it would break, because these are read in
    a test failure and in a generator's traceback, not by someone holding this
    file open.
    """
    reasons = []

    if not isinstance(name, str) or not name:
        return ["the name is empty"]

    if name != name.strip():
        reasons.append(
            "the name has leading or trailing whitespace, which Photoshop "
            "trims and Windows refuses"
        )

    if "/" in name or "\\" in name:
        reasons.append(
            "the name contains a path separator; in a Photoshop layer name "
            "`/` means a subfolder, so the asset would be generated somewhere "
            "else"
        )

    if "," in name:
        reasons.append(
            "the name contains a comma, which Photoshop reads as the "
            "separator between two assets on one layer"
        )

    bad = sorted(set(name) & (_ILLEGAL_CHARS - {"/", "\\"}))
    if bad:
        reasons.append(
            "the name contains " + " ".join(repr(c) for c in bad)
            + ", which Windows refuses in a file name"
        )

    if any(ord(c) < 0x20 or ord(c) == 0x7F for c in name):
        reasons.append("the name contains a control character")

    if any(ord(c) > 0x7E for c in name):
        reasons.append(
            "the name is not ASCII; a kit travels through zips and file "
            "systems that disagree about encoding"
        )

    if _SCALE_PREFIX.match(name) or _FIXED_SIZE_PREFIX.match(name):
        reasons.append(
            "the name starts with what Photoshop reads as a scale directive "
            "(`2x `, `200% ` or `100x50 `), so it would export a resized "
            "image under a shorter name"
        )

    if not name.endswith(SURFACE_EXT):
        reasons.append(
            f"the name does not end in `{SURFACE_EXT}`; Photoshop generates an "
            "asset only for a layer whose name carries a known extension, and "
            "`.png8`/`.png24` would drop the alpha channel"
        )
    else:
        stem = name[: -len(SURFACE_EXT)]
        if not stem:
            reasons.append("the name has no stem, only an extension")
        if stem.endswith(" ") or stem.endswith("."):
            reasons.append(
                "the stem ends in a space or a dot, which Windows silently "
                "strips, so the file would not be the one the manifest names"
            )
        if stem.split(".")[0].upper() in _RESERVED_STEMS:
            reasons.append(
                f"`{stem.split('.')[0]}` is a reserved device name on Windows "
                "and cannot be a file there whatever the extension"
            )

    if len(name) > MAX_NAME:
        reasons.append(
            f"the name is {len(name)} characters, over the {MAX_NAME} this "
            "contract allows so a kit still opens inside Windows' path limit"
        )

    return reasons


def is_asset_name(name):
    """True when `name` is usable as a painting surface by all three readers."""
    return not check_asset_name(name)


def require_asset_name(name, where=""):
    """Raise `AssetNameError` unless `name` is usable. For names *we* compose.

    A generator calls this on every surface it writes. A failure here is our
    bug: renaming behind the manifest's back would leave the kit pointing at a
    file that is not there, which is worse than stopping.
    """
    reasons = check_asset_name(name)
    if reasons:
        at = f" ({where})" if where else ""
        raise AssetNameError(
            f"`{name}` cannot be a painting surface{at}: " + "; ".join(reasons)
        )
    return name


def check_asset_set(names):
    """Return the reasons a *folder* of surface names is not usable.

    The per-name rules do not catch the one that only exists between names:
    macOS and Windows compare file names case-insensitively, so `Chr_0.png` and
    `chr_0.png` are one file there and two here. A kit that writes both loses
    one of them the moment it is copied to the artist's machine.
    """
    reasons = []
    seen = {}
    for name in names:
        key = name.lower()
        if key in seen and seen[key] != name:
            reasons.append(
                f"`{name}` and `{seen[key]}` differ only in case; macOS and "
                "Windows would treat them as one file"
            )
        seen.setdefault(key, name)
    return reasons


def sanitize_asset_stem(stem, fallback="surface"):
    """Turn outside input into a stem that survives `check_asset_name`.

    For names derived from a route file, a recorded map id or anything else the
    kit did not compose. The result is not meant to be pretty: it is meant to
    be the same on every machine. The caller records the original when the two
    differ, so nothing is quietly lost.
    """
    if not isinstance(stem, str):
        stem = str(stem)
    out = _SAFE_STEM.sub("-", stem).strip(" .-")
    # A leading digit followed by `x` or `%` and a separator is the only way a
    # sanitized stem can still read as a Photoshop scale directive; `-` is not
    # whitespace, so the prefix cannot match, but a stem that *is* a reserved
    # device name still can.
    if out.split(".")[0].upper() in _RESERVED_STEMS:
        out = out + "-page"
    if len(out) > MAX_NAME - len(SURFACE_EXT):
        out = out[: MAX_NAME - len(SURFACE_EXT)].rstrip(" .-")
    return out or fallback


def asset_name_for(path):
    """The string an artist pastes as a Photoshop layer name, for a kit path.

    The kit's manifest carries paths relative to the kit (`sheets/usr000.png`),
    but Photoshop's generator prefixes its own `-assets` folder and reads a `/`
    as a subfolder under it. So the layer name is the *base* name only, and the
    doc tells the artist which folder to copy it from.
    """
    return PurePosixPath(str(path)).name


class AssetNameError(ValueError):
    """A surface was about to be written under a name a paint program cannot
    export back onto."""
