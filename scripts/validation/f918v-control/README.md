# F9.18-V negative-control pack

Validator tooling, not a product or artist tool. It answers "which
`hires.txt` rule won for this key in this frame?" from the pack side, with
no change to `Core/`: the built pack is copied and each line class is
re-pointed at a cell whose colour names the outcome (gated hit = cyan,
gated miss falling to the bare twin = magenta, no gate = orange,
mirror-flagged key = solid four-quadrant marker whose order reads the OAM
flip). Rationale and the run that used it:
`docs/validation/f918v-current-binary-painting-2026-09-15.md` §4.

```
python3 scripts/validation/f918v-control/make_control_pack.py <built-pack> <out-pack> [<kit>/sheets]
python3 scripts/mep_lint.py <out-pack>
# install <out-pack>/textures as <rom-sibling>/mep/textures, then per instant:
scripts/headless_record <rom> <seconds> <prefix> screenshot state=<stage.mss> input=<route.txt>
python3 scripts/validation/f918v-control/analyze_control.py   <screenshots...>   # C/M/O census + band orientation
python3 scripts/validation/f918v-control/find_mirror_marker.py <screenshots...>  # quadrant marker + H/V flip
```

Do not pass `hdpack` to `headless_record` here: it is the builder's
*recording* flag and disables pack rendering (native 256x240 frames).
Restore the original `mep/textures` after the sweep.
