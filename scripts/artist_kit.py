"""Turn a recorded pack into a painting surface of whole figures (F9.18).

The Phase 9 side-by-side (ADR-0174 §Context, ADR-0179 §Context) measured what
an artist meets when they open a recorded pack: `sprNNN` sheets that cut a
soldier at the waist, and a `poses.json` whose 60-odd whole figures are an
unordered list sorted by frame count. The community pack the panel compared us
against (`Contra80s` 1.1) hands the artist the opposite: **grids of whole
figures**, one row per animation, one column per phase.

This tool builds that surface out of what the recorder already wrote. It reads
`sheets/poses.json` (ADR-0170) plus the succession the recorder found on it
(ADR-0179 §3 `cycles[]`/`sequences[]`) and lays every pose out as a grid:

* one grid per cycle — the row *is* the cycle, its columns the phases in order;
* one grid per sequence the cycles did not already cover;
* one "rest" grid of everything else, most-seen first, wrapped into rows.

A variant (ADR-0179 §4 — a figure plus its shot) sits in the same row,
immediately after the pose it is a variant of; a fusion (ADR-0177 — two
figures that touched) is not laid out at all, because both of its halves are
entries of this same file and the artist would be painting a bystander.

Every cell of a grid is padded to the grid's largest figure box and the figure
is bottom-aligned and centred in it, so a row can be painted across without
re-measuring each phase.

The sheets are ordinary ADR-0153 composed sheets (`usrNNN.png` +
`usrNNN.orig.png` + `usrNNN.json`), written through `compose_engine.Pack.export`
— the same code path the composition editor exports through, so what lands here
is `mep_build.py build` input and nothing else. They are written *beside* the
recording (`<kit>/sheets/`), never into it, unless `--in-place` says otherwise.

This is the `sprites` part of the shared artist kit
(`runs/golden-20260913-f922/artist-kit-contract.md`): it writes its surfaces
under `<out>/sheets/` and one manifest fragment `kit-part-sprites.json`, and it
writes no `kit.json` and no `ARTIST.md` — the assembler merges the four parts.
Everything an artist must read before painting goes into the fragment's
`notes[]`. `--names` supplies human captions for files (never for ids, and
never invented here); an unnamed grid falls back to its run/pose id and the
counts the recording measured.

Run:
    python3 scripts/artist_kit.py <pack folder> [--out DIR] [--names F] [--verify]

`--verify` copies the pack to a temp dir, drops the generated sheets in, runs
`mep_build.py build` over it and asserts that the rebuilt `textures/hires.txt`
keys exactly the same (tile data, palette) pairs the recording already did:
nothing lost, nothing invented.
"""

import argparse
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asset_names as N  # noqa: E402 — the F12.4 painting-surface name contract
import compose_engine as E  # noqa: E402
import mep_build  # noqa: E402 — the rebuild `--verify` runs and its <tile> regex

#The rest grid's shape. A cycle's row is as wide as the cycle is long (the row
#*is* the animation, so wrapping it would break the promise this tool makes);
#only the unordered remainder is wrapped, and these are its defaults.
DEFAULT_COLUMNS = 6      # the artist's reference sheet runs six phases wide
DEFAULT_ROWS = 8         # rows per sheet before a continuation sheet is opened
SLOT_GAP = 1             # empty cells between two figure boxes, as `pose_cells` uses


class KitError(Exception):
    """A pack this tool cannot build a kit from — never a silent empty folder."""


def _sprites_classify_screen_fixed(pack) -> bool:
    """Whether this pack's `adjacency.json` classifies HUD at all (ADR-0173).

    `_SpriteNode.screen_fixed` defaults to False, so the loaded sidecar cannot
    tell "classified, not pinned" from "recorded before the ADR". The raw file
    can: the field is present on every node or on none. Absence is not a
    licence to guess — it means the kit cannot separate a score digit from a
    figure, and has to say so."""
    try:
        doc = json.loads((Path(pack.sheets_dir) / "adjacency.json")
                         .read_text(encoding="utf-8"))
        nodes = doc["sprites"]["nodes"]
    except (OSError, ValueError, KeyError, TypeError):
        return False
    return any(isinstance(n, dict) and "screenFixed" in n for n in nodes)


# ---- the grid model --------------------------------------------------------

class Cell:
    """One figure in a grid: the pose, where it sits, and what it is called."""

    __slots__ = ("pose", "row", "col", "run", "phase", "phases")

    def __init__(self, pose, row, col, run=None, phase=None, phases=None):
        self.pose = pose
        self.row = row
        self.col = col
        self.run = run          # PoseRun this cell's phase belongs to, or None
        self.phase = phase      # 1-based phase inside that run, or None
        self.phases = phases    # length of that run, or None


class Grid:
    """A sheet's worth of cells: rows of poses, plus what to call the sheet."""

    def __init__(self, kind: str, title: str, run=None):
        self.kind = kind        # "cycle" | "sequence" | "rest"
        self.title = title
        self.run = run
        self.rows = []          # [[Cell]]
        self.name = None        # usrNNN, filled in by export
        self.boxes = []         # the figure box of each row, in 8 px cells
        self.columns = 0        # 8 px cells across the exported sheet

    @property
    def cells(self):
        return [c for row in self.rows for c in row]

    def poses(self):
        return [c.pose for c in self.cells]


# ---- building the grids ----------------------------------------------------

class KitBuilder:
    """Reads a `Pack` and answers with grids. Host-free and disk-free until
    `export` — everything here is decided from `poses.json`."""

    def __init__(self, pack, columns=DEFAULT_COLUMNS, rows=DEFAULT_ROWS):
        if pack.poses is None:
            raise KitError(
                f"{pack.sheets_dir}: no usable sheets/poses.json — this pack was recorded "
                "before ADR-0170, or its sidecar is unreadable. There is no whole-figure "
                "surface to lay out; re-record it with a build that has the pose sidecar.")
        self.pack = pack
        self.columns = max(1, int(columns))
        self.max_rows = max(1, int(rows))
        self.poses = pack.poses
        self._art_ok = {}
        self.excluded_blank = []   # poses no sheet draws a single tile of
        self._placed = set()
        # ADR-0173: nodes the recorder saw pinned to the screen for the whole
        # capture — a score digit, a life bar. Their `floors[]` are where they
        # are painted, not a ground they stand on, and they are not figures.
        self.hud_nodes = {n for n, node in pack.adjacency.sp.items() if node.screen_fixed}
        self.hud_classified = _sprites_classify_screen_fixed(pack)
        self.excluded_fusions = [p.id for p in self.poses.entries if p.fused]
        self.excluded_hud = [p.id for p in self.poses.entries
                             if not p.fused and self.is_hud(p)]

    def is_hud(self, pose) -> bool:
        """Whether every drawn member of `pose` is a screen-pinned node.

        Whole-figure, not any-member: a figure that overlaps the life bar for a
        frame shares no node with it, but a pose built only out of pinned nodes
        *is* the bar. On a pack recorded before ADR-0173 nothing is classified,
        so nothing is excluded and the kit says so rather than guessing."""
        if not self.hud_classified:
            return False
        tiles = self.drawable(pose)
        return bool(tiles) and all(n in self.hud_nodes for n in tiles)

    # -- art resolution ---------------------------------------------------

    def _has_art(self, node: int) -> bool:
        """`Pack.has_node_art`, memoised: `poses.json` indexes the whole sprite
        vocabulary while the sheets only draw the cells the recorder routed, so
        this is asked once per node and then thousands of times."""
        hit = self._art_ok.get(node)
        if hit is None:
            hit = self.pack.has_node_art(node, sprite=True)
            self._art_ok[node] = hit
        return hit

    def drawable(self, pose):
        """`{node: (dx, dy)}` normalised to the drawn figure's own top-left, or
        `{}` when no sheet draws any of its tiles. A member with no pixels is
        dropped rather than blanked — ADR-0164 §3's rule: a hole is honest."""
        tiles = {n: xy for n, xy in pose.tiles.items() if self._has_art(n)}
        if not tiles:
            return {}
        x0 = min(dx for dx, _dy in tiles.values())
        y0 = min(dy for _dx, dy in tiles.values())
        return {n: (dx - x0, dy - y0) for n, (dx, dy) in tiles.items()}

    @staticmethod
    def extent(tiles):
        if not tiles:
            return (0, 0)
        return (max(dx for dx, _dy in tiles.values()) + 1,
                max(dy for _dx, dy in tiles.values()) + 1)

    # -- pose selection ----------------------------------------------------

    def _variants_of(self, pose_id):
        """The kept variants of a pose, in the file's most-seen-first order."""
        return [p for p in self.poses.entries
                if p.variant_of == pose_id and not p.fused and p.id not in self._placed]

    def _take(self, pose, row, col, run=None, phase=None):
        """Place one pose plus its unplaced variants, left to right. Returns the
        cells produced and the next free column."""
        out = []
        if pose.id in self._placed or pose.fused:
            return out, col
        if self.is_hud(pose):
            self._placed.add(pose.id)   # a HUD element is not a figure (ADR-0173)
            return out, col
        if not self.drawable(pose):
            self.excluded_blank.append(pose.id)
            self._placed.add(pose.id)
            return out, col
        self._placed.add(pose.id)
        out.append(Cell(pose, row, col,
                        run=run, phase=phase,
                        phases=len(run.poses) if run is not None else None))
        col += 1
        # ADR-0179 §4: the satellite belongs beside the figure it is a variant
        # of, not in a bucket of its own — it is the same drawing plus a shot.
        for sat in self._variants_of(pose.id):
            if not self.drawable(sat):
                self.excluded_blank.append(sat.id)
                self._placed.add(sat.id)
                continue
            self._placed.add(sat.id)
            out.append(Cell(sat, row, col))
            col += 1
        return out, col

    def _run_grid(self, run, kind):
        """One row: the run's poses in phase order, each followed by its
        variants. A run whose poses are all placed already produces nothing."""
        grid = Grid(kind, run.id, run=run)
        row, col = [], 0
        for phase, pid in enumerate(run.poses, start=1):
            pose = self.poses.by_id(pid)
            if pose is None:
                continue
            cells, col = self._take(pose, 0, col, run=run, phase=phase)
            row.extend(cells)
        if not row:
            return None
        grid.rows.append(row)
        return grid

    def build(self):
        """The grids, in the order an artist should read them."""
        grids = []
        for run in self.poses.cycles:
            grid = self._run_grid(run, "cycle")
            if grid is not None:
                grids.append(grid)
        for run in self.poses.sequences:
            # A sequence every cycle already covered is not a second animation,
            # it is the same phases seen once more: laying it out again would
            # hand the artist the same figures to paint twice.
            if all(pid in self._placed for pid in run.poses):
                continue
            grid = self._run_grid(run, "sequence")
            if grid is not None:
                grids.append(grid)
        grids.extend(self._rest_grids())
        return grids

    def _rest_grids(self):
        """Everything no run ordered, binned by figure box and wrapped into rows
        of `columns`, then split across sheets every `max_rows`.

        The bin is the figure's exact box, so a row is uniform and nothing is
        padded to a neighbour's size — a 6x9 boss and a 1x2 pickup do not share
        a cell size. Bins are ordered by their most-seen member and each bin
        keeps the file's most-seen-first order inside it, which is as close to
        "most-seen first" as binning can stay; a variant still follows its base,
        which is why a row can hold a box larger than its bin's."""
        pending = []
        for pose in self.poses.entries:
            if pose.id in self._placed or pose.fused:
                continue
            if self.is_hud(pose):
                self._placed.add(pose.id)
                continue
            if pose.variant:
                base = self.poses.by_id(pose.variant_of)
                if base is not None and base.id not in self._placed and not base.fused:
                    continue  # its base is still to come; it follows the base
            pending.append(pose)
        bins = {}
        for pose in pending:
            tiles = self.drawable(pose)
            if not tiles:
                continue  # `_take` records it as excluded when it gets there
            bins.setdefault(self.extent(tiles), []).append(pose)
        order = sorted(bins, key=lambda box: (-max(p.frames for p in bins[box]),
                                              -box[0] * box[1], box))
        rest, row, col, rowno = [], [], 0, 0
        for box in order:
            if row:                      # a new box starts a new row
                rest.append(row)
                row, col, rowno = [], 0, rowno + 1
            for pose in bins[box]:
                cells, col = self._take(pose, rowno, col)
                row.extend(cells)
                if col >= self.columns:
                    rest.append(row)
                    row, col, rowno = [], 0, rowno + 1
        if row:
            rest.append(row)
        #Anything `pending` skipped and `_take` never reached (a pose with no
        #drawable tile) is still owed its exclusion record.
        for pose in pending:
            if pose.id not in self._placed:
                self.excluded_blank.append(pose.id)
                self._placed.add(pose.id)
        grids = []
        for start in range(0, len(rest), self.max_rows):
            chunk = rest[start:start + self.max_rows]
            part = start // self.max_rows + 1
            total = (len(rest) + self.max_rows - 1) // self.max_rows
            title = "rest" if total == 1 else f"rest {part}/{total}"
            grid = Grid("rest", title)
            for r, cells in enumerate(chunk):
                grid.rows.append([Cell(c.pose, r, c.col) for c in cells])
            grids.append(grid)
        return grids

    # -- layout ------------------------------------------------------------

    def placements(self, grid):
        """`[(node, cx, cy)]` for a grid, in cell coordinates, plus the per-row
        boxes it used.

        A figure is padded to **its own row's** box, centred horizontally and
        aligned on its bottom row — poses of one animation share a baseline the
        way they share a floor on screen, so a row can be painted across without
        re-measuring each phase. The box is the row's, not the sheet's: a 6x9
        boss must not set the cell size for a 1x2 pickup two rows below it, and
        the rest grid bins its figures by box precisely so a row is uniform.
        One empty cell separates two boxes, the same margin `Pack.pose_cells`
        leaves between two poses."""
        layouts = {}
        for cell in grid.cells:
            layouts[id(cell)] = self.drawable(cell.pose)
        boxes = []
        for row in grid.rows:
            sizes = [self.extent(layouts[id(c)]) for c in row if layouts[id(c)]]
            boxes.append((max((w for w, _h in sizes), default=0),
                          max((h for _w, h in sizes), default=0)))
        if not any(w and h for w, h in boxes):
            return [], []
        tops, y = [], 0
        for _w, h in boxes:
            tops.append(y)
            y += h + SLOT_GAP
        out = []
        for r, row in enumerate(grid.rows):
            box_w, box_h = boxes[r]
            for cell in row:
                tiles = layouts[id(cell)]
                if not tiles:
                    continue
                cols, rows = self.extent(tiles)
                ox = cell.col * (box_w + SLOT_GAP) + (box_w - cols) // 2
                oy = tops[r] + (box_h - rows)
                for node, (dx, dy) in sorted(tiles.items(), key=lambda kv: (kv[1][1], kv[1][0])):
                    out.append((node, ox + dx, oy + dy))
        return out, boxes


# ---- export ----------------------------------------------------------------

def export_grid(pack, builder, grid, out_dir: Path):
    """Write one grid as a composed sprite sheet. Returns its `usrNNN` stem.

    The whole figure is what gets painted, so a tile shared by two phases is
    emitted in **both** cells — `mep_build` settles the duplicate itself (one
    crop owns the key, a painted one beats an untouched one), and the
    alternative would be phases with holes where the shared torso should be."""
    cells, boxes = builder.placements(grid)
    if not cells:
        return None
    nodes = [n for n, _cx, _cy in cells]
    anchors = []
    for pose in grid.poses():
        anchor = pack.pose_anchor(pose)
        if anchor not in anchors:
            anchors.append(anchor)
    name = claim_name(pack, out_dir)
    try:
        pack.export("sprite", nodes, seed=anchors[0] if anchors else None,
                    locked=anchors[1:], to_dir=out_dir, placements=cells,
                    poses=[p.id for p in grid.poses()], name=name)
    except Exception:
        (out_dir / f"{name}.json").unlink(missing_ok=True)   # release the claim
        raise
    grid.name = name
    grid.boxes = boxes
    grid.columns = max(cx for _n, cx, _cy in cells) + 1
    return name


def claim_name(pack, out_dir: Path) -> str:
    """Reserve the next free `usrNNN` stem by *creating* its sidecar, empty.

    `Pack.next_free_name` only looks; two generators writing into one
    `<kit>/sheets/` at the same time would both be handed the same number and
    one would overwrite the other. Creating the file with O_EXCL makes the
    claim the same act as the check, so the order the parts run in stops being
    a rule anyone has to remember."""
    out_dir.mkdir(parents=True, exist_ok=True)
    for _attempt in range(1000):
        name = pack.next_free_name(out_dir)
        try:
            fd = os.open(out_dir / f"{name}.json", os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            continue   # somebody else took it between the scan and the create
        os.close(fd)
        return name
    raise KitError(f"{out_dir}: no free usrNNN name")


# ---- names (the shared contract's optional caption file) --------------------

class Names:
    """`--names <file.json>`: human captions for poses, cycles and subjects.

    A caption is never evidence — the pack's own ids stay authoritative, and a
    name is used for a file's `title` only. Nothing here invents one: an id the
    file does not mention falls back to itself plus its frame count."""

    def __init__(self, doc=None):
        doc = doc if isinstance(doc, dict) else {}
        self.subjects = doc.get("subjects") if isinstance(doc.get("subjects"), dict) else {}
        self.poses = doc.get("poses") if isinstance(doc.get("poses"), dict) else {}
        self.cycles = doc.get("cycles") if isinstance(doc.get("cycles"), dict) else {}

    @staticmethod
    def load(path):
        if not path:
            return Names()
        try:
            return Names(json.loads(Path(path).read_text(encoding="utf-8")))
        except (OSError, ValueError) as e:
            raise KitError(f"{path}: unreadable names file ({e})")

    def run(self, run_id):
        name = self.cycles.get(run_id)
        return name if isinstance(name, str) and name else None

    def pose(self, pose_id):
        entry = self.poses.get(pose_id)
        if isinstance(entry, dict):
            name = entry.get("name")
        else:
            name = entry
        return name if isinstance(name, str) and name else None

    def subject_of(self, pose_id):
        """The subject key `--names` files this pose under, or None."""
        entry = self.poses.get(pose_id)
        if not isinstance(entry, dict):
            return None
        key = entry.get("subject")
        return key if isinstance(key, str) and key else None

    @staticmethod
    def subject_label(key) -> str:
        """The key as a caption reads it: `green-soldier` -> `green soldier`.

        The key, not the `subjects` description: the description is a sentence
        the human wrote to *identify* the subject ("the green-uniformed enemy
        of the base interior") and belongs in the notes once, not inside every
        file's one-line caption."""
        return str(key).replace("-", " ").replace("_", " ").strip()

    def describe(self, key):
        """The human's description of a subject, or None when there is none."""
        text = self.subjects.get(key)
        return text if isinstance(text, str) and text else None


def sheet_subjects(grid, names):
    """The subjects on a sheet, most cells first, as `--names` describes them.

    Read off the names file and nowhere else: a subject is never inferred from
    a palette, a box size or a thumbnail. Ties keep the sheet's reading order,
    so the caption is stable. Empty when the file names none of these poses —
    the caller must then keep the id-based caption rather than invent one."""
    counts, order = {}, []
    for cell in grid.cells:
        subject = names.subject_of(cell.pose.id)
        if subject is None:
            continue
        if subject not in counts:
            counts[subject] = 0
            order.append(subject)
        counts[subject] += 1
    return sorted(order, key=lambda s: (-counts[s], order.index(s)))


def grid_title(grid, names):
    """The file's caption: what is on the sheet, then what the recording
    measured about it.

    The name of the run itself wins when `--names` gives one. Failing that the
    **subjects** of the poses on the sheet are the caption — `seq021` tells an
    artist nothing, "green soldier" tells them what to paint. With nothing
    named at all the caption is the run/pose id plus the counts, which is the
    honest fallback and is never dressed up as a name."""
    who = ", ".join(names.subject_label(k) for k in sheet_subjects(grid, names))
    run = grid.run
    if run is not None:
        named = names.run(run.id)
        if named:
            return named
        kind = "loop" if grid.kind == "cycle" else "ordered run"
        tail = f"a {len(run.poses)}-phase {kind}, seen {run.repeats} time(s)"
        return f"{who or run.id} — {tail}"
    cells = grid.cells
    if len(cells) == 1:
        pose = cells[0].pose
        named = names.pose(pose.id)
        if named:
            return named
        if who:
            return f"{who} — one figure, seen in {pose.frames} frame(s)"
        return f"{pose.id} — seen in {pose.frames} frame(s)"
    frames = sum(c.pose.frames for c in cells)
    if who:
        return f"{who} — {len(cells)} figures no cycle or sequence ordered"
    return (f"figures no cycle or sequence ordered — {len(cells)} of them, "
            f"seen in {frames} frame(s) between them")


# ---- the manifest fragment (artist-kit-contract.md) ------------------------

def _file_record(grid, names):
    return {
        "path": f"sheets/{N.require_asset_name(grid.name + N.SURFACE_EXT, 'artist_kit.py')}",
        "title": grid_title(grid, names),
        "unit": "grid",
        "rows": len(grid.rows),
        #The contract counts a grid in **figures**, not in 8 px cells: one
        #column is one figure. The sheet's own cell geometry is the sidecar's
        #business (`columns`/`gridUnit`/`gutter` there), not the artist's.
        "columns": max((len(r) for r in grid.rows), default=0),
        "cells": len(grid.cells),
        "ids": [c.pose.id for c in grid.cells],
        #Every pixel here was recorded: a pose is a silhouette the recorder saw
        #in one OAM frame, and no tile is filled in from the ROM or guessed.
        "seen": True,
    }


def _dropped(builder):
    out = []
    for pid in sorted(builder.excluded_fusions):
        pose = builder.poses.by_id(pid)
        parts = ", ".join(pose.fusion_of) if pose is not None else ""
        out.append({"path": pid,
                    "why": f"the recorder classified it as two figures that touched "
                           f"({parts or 'a fusion'}) — both halves are laid out on their own, "
                           f"so painting this one would paint a bystander (ADR-0177)"})
    for pid in sorted(builder.excluded_hud):
        out.append({"path": pid,
                    "why": "every tile of it is a node the recorder saw pinned to the screen "
                           "for the whole capture (ADR-0173) — a score digit, a life-bar "
                           "segment: HUD, not a figure"})
    for pid in sorted(builder.excluded_blank):
        out.append({"path": pid,
                    "why": "no sheet of this pack draws a single tile of it — the pose sidecar "
                           "indexes the whole sprite vocabulary, the sheets only draw what the "
                           "recorder routed"})
    return out


def _notes(pack, builder, grids, names, pack_arg):
    """What an artist has to read before painting. The assembler folds these
    into ARTIST.md; this generator writes no prose file of its own."""
    notes = [
        "Sprite surfaces (sheets/usr*.png) are grids of whole figures: one row is one "
        "animation, one column is one of its phases, read left to right. Each figure is "
        "centred in a fixed box and stands on the same baseline as the rest of its row, so "
        "a row can be repainted straight across without re-measuring each phase.",
        "Open the cycles first (a loop the recorder saw repeat), then the sequences (an "
        "ordered run that does not loop), then the grids of figures no run ordered — that "
        "is the order they are listed in.",
        "usr*.orig.png is the reference, never paint it: it is the untouched recording, and "
        "the build compares your sheet against it to decide which cells you actually "
        "painted. Painting the reference makes your work invisible to the build.",
        "usr*.json is the slicing contract — do not move, resize or reorder a cell, and do "
        f"not resize the PNG; it is already at this pack's <scale> ({pack.scale}x), and every "
        "sheet of a pack must share one scale.",
        "Paint only inside a figure's own box. The one empty cell between two boxes belongs "
        "to neither figure. Each row has its own box, sized to that row's largest figure, so "
        "a boss two rows up does not decide how much space a pickup gets.",
        "A figure is laid out once, on the first run that ordered it, so a row can hold fewer "
        "columns than its animation has phases: a phase whose silhouette repeats (the same "
        "drawing twice in one loop) is one column, and a phase already drawn on an earlier "
        "sheet is not repeated here. files[].ids is always exactly what is on the sheet, in "
        "reading order, row by row.",
        "A column is a phase or a variant of the phase to its left — a variant is the same "
        "figure plus something small the recorder saw attached to it (a muzzle flash, a shot), "
        "and it sits immediately after the figure it varies (ADR-0179).",
        "A tile shared by two phases is one tile. The same 8x8 cell often recurs across the "
        "phases of an animation (a torso that never moved); every phase shows it so the "
        "figure is whole, but the game has only one copy — paint it the same way everywhere, "
        "or the build keeps just one of your versions.",
        f"Rebuild after painting: copy sheets/usr* into \"{pack_arg}/textures/sheets/\" and run "
        f"python3 scripts/mep_build.py build \"{pack_arg}\".",
    ]
    if builder.excluded_fusions or builder.excluded_blank or builder.excluded_hud:
        notes.append(
            f"{len(builder.excluded_fusions)} fused pose(s), {len(builder.excluded_hud)} "
            f"HUD element(s) and {len(builder.excluded_blank)} pose(s) with no art on any "
            "sheet are deliberately absent from the grids; dropped[] in this fragment says "
            "which and why.")
    if not builder.hud_classified:
        notes.append(
            "This recording does not classify HUD: its adjacency.json carries no "
            "screenFixed field, so it was made before ADR-0173 and nothing here can tell a "
            "score digit or a life-bar segment from a figure. Expect some cells of the "
            "unordered grids to be HUD; they are not marked, because the data does not say "
            "so and this tool does not guess. Re-record the pack to get them separated.")
    if not builder.poses.cycles and not builder.poses.sequences:
        notes.append(
            f"The recorder found no cycle and no sequence in this recording's "
            f"{len(builder.poses.entries)} pose(s) (ADR-0179 finds them on a track of "
            "successive OAM frames, and a run that never repeats leaves none), so there is "
            "no animation to make a row out of: every sheet here is a grid of figures binned "
            "by size, most-seen first, and the \"a row is one animation\" reading does not "
            "apply to this kit. Re-record with a run that repeats an animation to get "
            "animation rows.")
    elif not builder.poses.cycles:
        notes.append(
            "The recorder found no looping cycle in this recording, only "
            f"{len(builder.poses.sequences)} ordered sequence(s) — the rows here are runs "
            "that were seen in order but never seen to loop.")
    used = []
    for grid in grids:
        for key in sheet_subjects(grid, names):
            if key not in used:
                used.append(key)
    described = [f"{names.subject_label(k)} — {names.describe(k)}"
                 for k in used if names.describe(k)]
    if described:
        notes.append(
            "The subjects these sheets hold, as the naming file describes them: "
            + "; ".join(described) + ".")
    unnamed = [c.pose.id for g in grids for c in g.cells if not names.pose(c.pose.id)]
    if unnamed:
        notes.append(
            f"{len(unnamed)} figure(s) carry no human caption — their cells are addressed by "
            "pose id only. The ids in files[].ids are in reading order, row by row.")
    return notes


def write_fragment(pack, builder, grids, out_dir: Path, pack_arg: str, names,
                   verify_report=None):
    doc = {
        "part": "sprites",
        "generator": "scripts/artist_kit.py",
        "pack": pack_arg,
        "files": [_file_record(g, names) for g in grids],
        "dropped": _dropped(builder),
        "notes": _notes(pack, builder, grids, names, pack_arg),
        "verify": verify_report or {"ran": False},
    }
    (out_dir / "kit-part-sprites.json").write_text(json.dumps(doc, indent=2) + "\n",
                                                   encoding="utf-8")
    return doc


# ---- verification ----------------------------------------------------------

def hires_keys(path: Path):
    """The `(tileData, palette)` pairs a `hires.txt` keys, as a set."""
    keys = set()
    if not path.is_file():
        return keys
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = mep_build._TILE_RE.match(line.strip())
        if not m:
            continue
        fields = [f.strip() for f in m.group(2).split(",")]
        if len(fields) >= 3:
            keys.add((fields[1].upper(), fields[2].upper()))
    return keys


def verify(pack_dir: Path, sheets_out: Path, scratch=None):
    """Rebuild a copy of the pack with the kit sheets dropped in and compare
    what it keys against the recording. Returns `(ok, report)`.

    The control rebuild (the same copy without the kit) is run too: a recording
    whose own `hires.txt` no longer matches its own sheets would otherwise read
    as a failure of this tool."""
    report = {"ran": True}
    base_keys = hires_keys(pack_dir / "textures" / "hires.txt")
    report["keys_recorded"] = len(base_keys)
    with tempfile.TemporaryDirectory(dir=scratch) as td:
        tmp = Path(td)
        control = tmp / "control"
        shutil.copytree(pack_dir, control)
        rc_control = mep_build.main(["build", str(control), "--quiet"])
        control_keys = hires_keys(control / "textures" / "hires.txt")
        report["control_errors"] = rc_control
        report["keys_before"] = len(control_keys)

        work = tmp / "withkit"
        shutil.copytree(pack_dir, work)
        sheets = work / "textures" / "sheets"
        added = 0
        for src in sorted(sheets_out.glob("usr*")):
            if src.suffix in (".png", ".json"):
                shutil.copy2(src, sheets / src.name)
                added += 1
        report["files_added"] = added
        rc = mep_build.main(["build", str(work), "--quiet"])
        report["errors"] = rc
        kit_keys = hires_keys(work / "textures" / "hires.txt")
        report["keys_after"] = len(kit_keys)
        report["lost"] = len(control_keys - kit_keys)
        report["added"] = len(kit_keys - control_keys)
    report["drift_from_recording"] = len(base_keys ^ control_keys)
    #A recording that does not rebuild on its own cannot answer the question
    #this check asks: `build` stops before it writes `hires.txt`, so the two key
    #sets are both just the file already on disk. Say so — "blocked by the
    #recording" is a different fact from "the kit broke the pack", and the kit
    #adding no error of its own is what can still be asserted.
    report["blocked"] = rc_control != 0
    report["adds_no_error"] = rc == rc_control
    ok = (rc == 0 and report["lost"] == 0 and report["added"] == 0)
    report["ok"] = ok
    return ok, report


# ---- CLI -------------------------------------------------------------------

def build_kit(pack_dir: Path, sheets_out: Path, columns, rows, pack_arg):
    pack = E.Pack(pack_dir)
    builder = KitBuilder(pack, columns=columns, rows=rows)
    grids = builder.build()
    sheets_out.mkdir(parents=True, exist_ok=True)
    kept = []
    for grid in grids:
        if export_grid(pack, builder, grid, sheets_out) is not None:
            kept.append(grid)
    return pack, builder, kept


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        description="Lay a recorded pack's poses out as artist grids of whole figures "
                    "(rows = animations, columns = phases).")
    ap.add_argument("pack", help="the recorded pack folder (the one holding textures/)")
    ap.add_argument("--out", help="the kit folder (default: kit/ beside the pack); sheets "
                                  "land in <out>/sheets/ per artist-kit-contract.md")
    ap.add_argument("--in-place", action="store_true",
                    help="write the sheets into the pack's own textures/sheets/ "
                         "(default: never touch the recording)")
    ap.add_argument("--names", help="optional caption file (subjects/poses/cycles), the schema "
                                    "of names-contra-stage2-base.json")
    ap.add_argument("--columns", type=int, default=DEFAULT_COLUMNS,
                    help=f"columns of the rest grid; a cycle's row is always as wide as the "
                         f"cycle is long (default: {DEFAULT_COLUMNS})")
    ap.add_argument("--rows", type=int, default=DEFAULT_ROWS,
                    help=f"rows per rest sheet before a continuation sheet is opened "
                         f"(default: {DEFAULT_ROWS})")
    ap.add_argument("--verify", action="store_true",
                    help="rebuild a throwaway copy of the pack with the kit in it and "
                         "assert the tile keys are unchanged")
    ap.add_argument("--scratch", help="directory for --verify's temporary copies")
    args = ap.parse_args(argv)

    pack_dir = Path(args.pack)
    if not pack_dir.is_dir():
        print(f"error: {pack_dir}: not a folder", file=sys.stderr)
        return 2
    out_dir = Path(args.out) if args.out else pack_dir.parent / "kit"
    #`--in-place` puts the painting surfaces straight into the recording; the
    #fragment still goes to the kit folder, because it describes a kit and not
    #a pack.
    sheets_out = (pack_dir / "textures" / "sheets") if args.in_place else out_dir / "sheets"

    try:
        names = Names.load(args.names)
        pack, builder, grids = build_kit(pack_dir, sheets_out, args.columns,
                                         args.rows, str(pack_dir))
    except (E.ComposeError, KitError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2

    cells = sum(len(g.cells) for g in grids)
    rows = sum(len(g.rows) for g in grids)
    print(f"{pack_dir}")
    print(f"  poses: {len(builder.poses.entries)} total, {cells} laid out, "
          f"{len(builder.excluded_fusions)} fusion(s) excluded, "
          f"{len(builder.excluded_blank)} with no art excluded")
    print(f"  runs: {len(builder.poses.cycles)} cycle(s), {len(builder.poses.sequences)} sequence(s)")
    print(f"  grids: {len(grids)} sheet(s), {rows} row(s), {cells} figure(s), scale {pack.scale}x")
    for grid in grids:
        wide = max((len(r) for r in grid.rows), default=0)
        variants = sum(1 for c in grid.cells if c.pose.variant)
        seen_boxes = sorted({b for b in grid.boxes if b[0] and b[1]},
                            key=lambda b: (-b[0] * b[1], b))
        shown = ", ".join(f"{w}x{h}" for w, h in seen_boxes[:3])
        if len(seen_boxes) > 3:
            shown += f" (+{len(seen_boxes) - 3} more)"
        print(f"    {grid.name}  {grid.kind:8s} {len(grid.rows)}x{wide} figures "
              f"({variants} of them variants), row box(es) {shown} cells, "
              f"sheet {grid.columns} cells wide  |  {grid_title(grid, names)}")
    print(f"  sheets written to: {sheets_out}")

    report, ok = None, True
    if args.verify:
        ok, report = verify(pack_dir, sheets_out, scratch=args.scratch)
    out_dir.mkdir(parents=True, exist_ok=True)
    write_fragment(pack, builder, grids, out_dir, str(pack_dir), names, verify_report=report)
    print(f"  fragment: {out_dir / 'kit-part-sprites.json'}")

    if report is not None:
        print(f"  verify: build exit {report['errors']}, "
              f"{report['keys_recorded']} key(s) in the recording, "
              f"{report['keys_before']} after a control rebuild, "
              f"{report['keys_after']} with the kit; "
              f"{report['lost']} lost, {report['added']} invented "
              f"({report['files_added']} file(s) added)")
        if report["drift_from_recording"]:
            print(f"  note: the recording's own hires.txt differs from a control rebuild of it "
                  f"by {report['drift_from_recording']} key(s) — that drift predates this tool")
        if report["blocked"]:
            print(f"  verify: BLOCKED — this recording already fails "
                  f"`mep_build.py build` on its own (control exit {report['control_errors']}), "
                  f"so the key sets above are the file on disk and prove nothing. The kit "
                  f"{'adds no error of its own' if report['adds_no_error'] else 'adds errors'}; "
                  f"fix the recording first.")
        else:
            print(f"  verify: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
