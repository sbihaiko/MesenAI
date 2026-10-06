# ADR-0192: The generative repaint backend is retired as a project commitment until a measured run exists

- Status: accepted (2026-09-15; Phase 11 C.8 — closes the ADR-0154 `diffusion`
  debt; reflected in this ADR and the ADR-0154 Status line the same day)
- Date: 2026-09-15
- Related: ADR-0154 §2 Option A, `scripts/sheet_repaint.py`, PRD Phase 9
  validation test 8, Phase 11 C.8
- Supersedes / amends: ADR-0154 §2 (Option A — local diffusion + ControlNet
  as the chosen generative backend). The rest of ADR-0154 stands: the
  `generated` label, control-image contract, `passthrough`/`classical`/
  `esrgan` backends, and alpha rules.

## Context

ADR-0154 accepted Option A: a local ComfyUI/`diffusers` + ControlNet path as
the generative half of `sheet_repaint.py`. The scaffold shipped; the
unavailable path is tested; **the generation path has never executed**
(ADR-0154 Consequences). Running it needs multi-gigabyte weights and a GPU
this project does not provide, and PRD validation test 8 (the blind A/B) has
never been run. Phase 11 C.8 asked either for one measured diffusion run or
for Option A to be superseded — inventing a fake run is not allowed.

On this machine `torch` and `diffusers` are absent, and no loopback ComfyUI
endpoint answers. The unavailable probe correctly refuses; that is not a
measurement of generation quality.

## Decision

1. **Option A is superseded as a project commitment.** The supported,
   always-runnable backends are `passthrough` (control arm) and `classical`
   (in-repo Scale2x/Scale3x). `esrgan` stays an optional local install for
   artists who already have Real-ESRGAN.
2. **`diffusion` may remain in the tree** as code that refuses cleanly when
   no runtime is present, but it is **not** a deliverable, not a gate, and
   not evidence that generative repaint works. Nobody may read a green test
   suite as proof that it produces an image (same sentence ADR-0154 already
   had — now binding as retirement, not a debt).
3. **Re-opening generative repaint** requires a new ADR (or an amendment of
   this one) that cites a measured run: at least one sheet through
   `--backend diffusion` with captured input/output hashes, wall clock, and
   the unavailable-path still tested. Until then PRD test 8 stays a human
   panel over `classical` vs `passthrough` only, if it is ever scheduled.

## Consequences

- Phase 11 C.8's ADR-0154 debt is closed without a GPU.
- Artists who want generative upscale use their own tools; the fork's
  contract is the control image, the `generated` label, and the classical
  baseline.
- The `diffusion` code path is dead weight until reopened — acceptable
  because deleting it now would churn a large file for no product gain, and
  the availability probe documents what is missing.
