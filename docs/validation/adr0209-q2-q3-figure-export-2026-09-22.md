# ADR-0209 Q2/Q3 — a figure exported whole, painted, and back on its cells (2026-09-22)

Decision under test: ADR-0209 **Q2 = (e)** (what "mark a figure" exports is
the `sprNNN` unit of ADR-0168, reassembled through the pack's own offsets into
one PNG) and **Q3 = (i)** (the file comes back over the F12.4 asset name,
re-imported explicitly per ADR-0213 §4, kept only where cells differ from the
`*.orig.png` twin, shown by F12.3's reload). Q1 (who writes `label`) is not
part of this run.

Implementation: `scripts/mep_figure.py` (`export`, `import --verify`), suite
`scripts/test_mep_figure.py` (wired into `make doc-checks`). No Core change; no
emulator was run — what is measured here is the file contract, not the reload
(F12.3's own log, `docs/validation/f12.3-reload-repainted-images-2026-09-19.md`,
covers that half and is unchanged by this slice).

## Synthetic (the suite)

`python3 scripts/test_mep_figure.py` — 35 checks, all passing, on a pack built
in a temp dir at `<scale>2`:

- a `spr000` group whose `evidence[]` stacks two poses (node 3's strongest
  edge points at the slot node 1 holds) exports as **one 3x2 PNG** with the
  walked offsets, node 4 (below the count floor) reported under `unplaced`
  and not drawn;
- with `poses.json` present the layout is the pose's (2x3, all five members),
  the four members on the group sheet map to `spr000.json` cells and the fifth
  to `sprites.json`; naming `pose000` directly maps every cell to
  `sprites.json`;
- an unpainted import writes **zero** cells and changes no sheet PNG;
- painting one cell of the figure changes exactly **one** sheet PNG
  (`spr000.png`), and only inside that cell's rect; a second import of the same
  file is a no-op (`alreadyApplied: 1`); a re-export shows the paint on the
  surface and the original on the twin;
- `mep_build.py build` exits 0 on the repainted pack with the same six
  `(tileData, palette)` keys, the painted key's rule now points at
  `sheets/spr000.png`, `mep_lint.py` exits 0, `verify` passes both against the
  pack as is and against a control with the group sheet restored;
- an `objNNN` group exports at unit 16 through its walk; a figure whose canvas
  was resized is refused with the reason.

## Real: Contra, `spr000`

Source: `runs/golden-20260913-f923/contra-lr/Contra (1988) (Konami)/auto`
(not versioned; `textures/hires.txt` sha256 prefix `f3c830387b81cac4`, 1 767
recorded keys, `<scale>4`), copied to a scratch folder — the recording itself
was not written to.

```sh
python3 scripts/mep_figure.py export <copy> spr000 --out <scratch>/figures
python3 scripts/mep_figure.py import <copy> <scratch>/figures/spr000-figure.png        # unpainted
python3 scripts/mep_figure.py import <copy> <scratch>/figures/spr000-figure.png --verify  # one cell painted
python3 scripts/mep_build.py build <copy> --quiet
python3 scripts/mep_lint.py <copy> --quiet
```

| step | result |
|---|---|
| export | `spr000-figure.png` **10 cells, 3x6 at unit 8, scale 4x, layout from poses (pose002)**; 8 cells map to `spr000.json` (indices 0–7), 2 to `sprites.json` (indices 2, 3) — the pose holds two tiles the group sheet does not, exactly the S10.a under-grouping ADR-0168 measured; `unplaced: []`, `unresolved: []`; asset name `spr000-figure.png` valid |
| import, unpainted | `10 cells, 0 painted, 0 written`; no sheet PNG changed |
| import, one cell painted magenta | `1 painted, 1 written`; only `spr000.png` changed; `verify: build errors 0, keys 116 -> 116, 0 lost, 0 added: PASS` |
| `mep_build.py build` | exit 0; 116 keys carried, 1 651 dropped (the bootstrap-vs-sheets drift ADR-0172 accepted, reported by `build` itself as expected); the painted key's `<tile>` rule points at `sheets/spr000.png` |
| `mep_lint.py` | exit 0 (0 errors, 14 warnings, all about the recorder's own sheet sizes) |

The exported twin, viewed: a whole soldier — head, torso, legs and the two
vocabulary tiles beneath — where `spr000.png` on its own holds the same figure
cut into an L-shaped 3x4 sheet with four empty slots. That is the difference
Q2 (e) was chosen for.

## What this does not verify

- **The reload in the running game** (constraint 2 of ADR-0209). The import
  ends where F12.3 starts: a changed `spr000.png` on disk. F12.3's log measured
  that step on the same kind of file; it was not re-run here.
- **Photoshop / GIMP / Aseprite** were not opened. The paint step was a
  programmatic 32x32 magenta fill at the figure's scale. The name contract is
  asserted by `asset_names.require_asset_name`, not by a paint program.
- **`verify` on a recorded pack measures against a control rebuild**, not
  against the recorded `hires.txt`: a first `build` of a bootstrap pack drops
  the palette-agnostic defaultTile keys (1 767 → 116 here) whether or not a
  figure was imported. The report prints that drift separately so nobody reads
  it as the import's doing.
