#!/usr/bin/env python3
"""Every renderer that runs a librashader chain must consult IsLookCompare.

ADR-0246 §5 (Hold to Compare): while the button is held the shader chain is
bypassed rather than rebuilt, because the measured swap stutters (crt-geom
`SetShader` 67.6 ms and 17.6 ms against a 16.7 ms frame, `make
metal-presenter-tests`). macOS kept that promise from the start - its renderer
sets the presenter's bypass - and Windows and Linux did not, so §5 held on one
platform of three with nothing to notice it. Closed 2026-10-05: both renderers
now read the flag.

The applier set is found by scanning the whole repo, not a list of platform
directories, so a chain added under a directory nobody thought of is still
found. Two things are written down instead of inferred, and both are checked:

  * REQUIRED_DIRS - the directories that must still yield an applier. Without
    it a rewrite that hides the call behind a macro, a stored function pointer
    or a renamed entry point would make the applier set empty, and an empty set
    would pass. Each of these directories must produce at least one applier.
  * DELEGATED_READ - the one applier whose flag read lives in another file.
    macOS is the case: `MetalPresenter.mm` runs the chain and
    `MacOSMetalRenderer.mm` reads the flag and sets the presenter's bypass. It
    is an explicit map rather than a directory-wide rule, because a
    directory-wide rule lets a second renderer added to an existing directory
    pass on the strength of the first one's read.

Comments are stripped before matching, so deleting a real read while leaving a
comment that names it is a failure, not a pass.

This is still a presence guard. It cannot see whether the flag is consulted on
the right branch, nor whether the read is live (`if(false && ...)` passes). The
macOS behaviour is pinned by `make metal-presenter-tests`; Windows and Linux are
compiled in CI but have no renderer harness, and neither can be run here.
"""

import pathlib
import re
import sys

#Directories that must each yield at least one applier - see the module comment.
REQUIRED_DIRS = ("Windows", "Linux", "MacOS")

#The vendored librashader bindings are nothing but *_filter_chain_frame typedefs
#and declarations, not a renderer. The graph cache is generated, not source.
EXCLUDED_PATHS = ("Utilities/Video/", "graphify-out/")
SKIPPED_DIRS = {"obj", "bin", ".git", "node_modules", "player-renders"}

SOURCES = ("*.cpp", "*.h", "*.hpp", "*.mm", "*.m", "*.cc", "*.c", "*.cxx", "*.inl")

APPLIES = re.compile(r"\w+_filter_chain_frame\s*\(")
READS = re.compile(r"\bIsLookCompare\s*\(")

#Applier file -> the file that holds its read, for the one delegation there is.
DELEGATED_READ = {
	"MacOS/MetalPresenter.mm": "MacOS/MacOSMetalRenderer.mm",
}


def strip_comments(text: str) -> str:
	"""Remove // and /* */ comments, leaving string and char literals alone.

	A plain regex would cut a line at the // of a URL inside a string and could
	hide a real read behind it, so the scan walks the text and tracks whether it
	is inside a literal.
	"""
	out = []
	i, n = 0, len(text)
	quote = ""
	while i < n:
		ch = text[i]
		if quote:
			out.append(ch)
			if ch == "\\" and i + 1 < n:
				out.append(text[i + 1])
				i += 2
				continue
			if ch == quote:
				quote = ""
			i += 1
			continue
		if ch in "\"'":
			quote = ch
			out.append(ch)
			i += 1
			continue
		if ch == "/" and i + 1 < n and text[i + 1] == "/":
			while i < n and text[i] != "\n":
				i += 1
			continue
		if ch == "/" and i + 1 < n and text[i + 1] == "*":
			i += 2
			while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
				i += 1
			i += 2
			continue
		out.append(ch)
		i += 1
	return "".join(out)


def sources_in(root: pathlib.Path):
	for pattern in SOURCES:
		for path in root.rglob(pattern):
			relative = path.relative_to(root).as_posix()
			if any(part in relative for part in EXCLUDED_PATHS):
				continue
			if SKIPPED_DIRS.intersection(path.parts):
				continue
			yield path, relative


def main() -> int:
	root = pathlib.Path(__file__).resolve().parents[2]
	appliers = {}
	readers = set()

	for path, relative in sources_in(root):
		text = strip_comments(path.read_text(encoding="utf-8", errors="replace"))
		if APPLIES.search(text):
			appliers[relative] = pathlib.PurePosixPath(relative).parent.as_posix()
		if READS.search(text):
			readers.add(relative)

	failures = []

	for applier, directory in sorted(appliers.items()):
		if applier in readers:
			continue
		delegated = DELEGATED_READ.get(applier)
		if delegated and delegated in readers:
			continue
		if delegated:
			failures.append(
				f"{applier} runs a librashader chain and delegates its flag read to "
				f"{delegated}, which no longer reads IsLookCompare()."
			)
		else:
			failures.append(
				f"{applier} runs a librashader chain but does not read IsLookCompare(), "
				f"and is not a known delegation, so Hold to Compare (ADR-0246 §5) "
				f"presents the filtered frame there."
			)

	for name in REQUIRED_DIRS:
		if not any(directory == name or directory.startswith(name + "/") for directory in appliers.values()):
			failures.append(
				f"{name}/ no longer yields a renderer that calls a "
				f"*_filter_chain_frame - either its chain is gone or the call is "
				f"hidden from this search, and this guard cannot tell the two apart."
			)

	if failures:
		for failure in failures:
			print(f"FAIL: {failure}", file=sys.stderr)
		return 1

	for applier, directory in sorted(appliers.items()):
		where = "same file" if applier in readers else DELEGATED_READ[applier]
		print(f"ok: {applier} presents a shader chain and IsLookCompare() is read in {where}")
	print(f"ok: {len(REQUIRED_DIRS)} required platform directories all yield an applier")
	return 0


if __name__ == "__main__":
	sys.exit(main())
