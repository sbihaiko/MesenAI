# ADR-0224: A recorded screen does not hide a behind-background sprite over empty canvas — opt-in per pack

- Status: accepted (2026-09-22; amended 2026-09-22), shipped the same day as PRD Part A F12.15 ([log](../../validation/slices/f12.15-behind-bg-sprites-2026-09-22.md)). User's labels, verbatim: *"Opt-in por pack (Recommended)"* for the scope, *"Sim, na mesma fatia (Recommended)"* for splitting the overdraw tool's count, *"So a ADR agora (Recommended)"* for the timing, *"Tag nova em hires.txt (Recommended)"* for where it lives. Build go-ahead, verbatim: *"libera a F12.15, dispara as três partes em paralelo."* Stop conditions (1), (3), (4) met; **(2) partially met**: the predicate, parser and writer have unit cases, but `HdNesPack::GetPixels` has no direct unit test (outside the `core-unit-tests` link set, needs `NesConsole`), so the tag-on render path rests on the headless renders (0 `erased sprite` on 41 s–45 s, 36/36 + 9/9 byte-identical without the tag). §2's "equivalently" form is what shipped.
- Date: 2026-09-22
- Related: issue #339 (its second cause) and ADR-0223 (the first cause, "The fight screen"; this ADR closes what it cannot); ADR-0221 (its render-path non-goal); ADR-0050 (`<background>` at priority 20, "sprites still draw on top"); ADR-0156 (a captured screen owns its cells); ADR-0004 (the hires.txt draft); ADR-0005 (MEP `textures/` delegates to `HdPackLoader`); ADR-0146 (every accepted pack auto-loads, so a global change reaches every player); issue #328; `docs/validation/slices/f12.13-variant-kind-rule-2026-09-22.md`.
- Supersedes / amends: amends ADR-0050's "sprites still draw on top" — true for front sprites, false for behind-background ones until this tag is present; amends ADR-0221's "the render path is untouched" for F12.15 only.

## Context

The F12.13 trace (ADR-0223, "The fight screen") read the shadow OAM off the Punch-Out!! route: every cell `scripts/measure_capture_overdraw.py` counts as erased between 41 s and 45 s is a sprite with OAM attribute bit 5 set (Glass Joe's shorts and legs), none on ROM background detail.

The cause is draw order. `HdNesPack::GetPixels` paints: backdrop; layer 0 (0–9); behind-background sprites; layer 1 (10–19); the tile (`<tile>`); layer 2 (20–29, `BehindFgSpritesPriority`); front sprites; layer 3 (30–39). A recorded `<background>` sits in layer 2 (ADR-0050), on top of the sprite; on hardware it is hidden only where the background is opaque. The layer is *named* "behind foreground sprites", and ADR-0146 loads every accepted pack, so a global fix and an opt-out default are ruled out. The tool compounds this: it scores a cell "erased" without asking background or sprite, so ADR-0221's "0 erased cells" was written against it.

Non-goals: no recorder capture/gate change (ADR-0050, ADR-0159, ADR-0221, ADR-0223 stay); no change to layers 0/1/3, `<tile>` rules or front sprites; not GB/SMS (`HdNesPack` is NES-only, its own priority model, ADR-0036/ADR-0037); no change to an existing pack unless it opts in.

## Decision

1. **A new hires.txt tag, MesenAI extension, opt-in.** `<bgPreservesBehindBgSprites>` keeps a behind-background sprite visible where the ROM's background pixel is colour 0, even under a layer-2 (priority 20–29) background. No arguments; on or off for the pack. It is a tag, not an `<options>` token, because `HdPackLoader::ProcessOptionTag` logs an unknown token as `Invalid option` and counts a load error, while the tag dispatch has no final `else` and skips an unknown tag in silence — so a recorded pack still opens in Mesen 2 and HD Mesen, without the effect. `<options>` tokens are not gained, lost or reordered (`HdPackOptionsToString` unchanged). MEP packs get it through `textures/hires.txt` by ADR-0005; `pack.json` untouched.

2. **Renderer semantics, `HdNesPack::GetPixels`.** With the tag, in the layer-2 pass a pixel is left as the behind-background sprite pass drew it when all of: the pixel has a behind-background sprite with `SpriteColorIndex != 0` (`lowestBgSprite != 999` already records it); the ROM background pixel has `BgColorIndex == 0`; and layer 2 would otherwise paint it. Equivalently: `DrawBehindBgSprites` is re-applied after layer 2 on colour-0 pixels when `HdBehindBgSpriteRule::KeepsBehindBgSprite` holds. Everything else — opaque background, no sprite, front sprites, layers 0/1/3 — draws as today; the issue-#328 counters are scoped per draw. Without the tag, byte-identical output (a unit test asserts it). Trap: `lowestBgSprite` is set only when the sprite colour index is non-zero — do not read `BackgroundPriority` alone, and do not gate on the HD tile's alpha; the rule is the ROM's background colour index.

3. **The recorder writes the tag on every pack it produces** (`HdPackBuilder`, next to `<options>`), bootstrap `auto/` included. Hand-written and community packs are untouched; `mep_lint.py` accepts the tag and says nothing about its absence.

4. **The overdraw tool splits its count.** `scripts/measure_capture_overdraw.py` reads the OAM `K`/`P`/entry lines (ADR-0222) or the `M` RAM line and reports two columns per frame — `erased background` (ROM background detail under a flat render) and `erased sprite` (flat render under sprite detail only) — plus the total. The F12.15 stop condition is stated on the split columns, never the total; content-over-content overpaint is out of scope (ADR-0223, unmeasured).

5. **Spec and docs.** The tag joins the hires.txt draft under ADR-0004 (`docs/specs/hires-gbsms-v1-draft.md` §7), `docs/remastering-a-game.md`, and the kit's `ARTIST.md` (one sentence, so nobody deletes it as noise).

## Consequences

- **Closes #339's second cause** for packs recorded here, and only those; the first cause (the card) stays with ADR-0223.
- **A third divergence from HD Mesen** (after `<ver>` and the GB/SMS draft), safe by construction — ignored where unknown; the spec entry is the answer.
- **Render cost, measured** ([log](../../validation/adr/adr0224-30rom-tag-sweep-2026-09-22.md)): tag on vs off, 30 ROMs x 4 timestamps, 234 480 frames per configuration — 3.711 vs 3.741 ms/frame, no consistent direction; `erased background` 6 vs 6, `erased sprite` 1 vs 369; 110 of 120 frame pairs byte-identical. "measure on the 30-ROM library before and after, not assume" is answered.
- **Re-record is not required.** Bootstrap packs keep today's look until re-recorded; a user can add the line by hand; the catalog's packs until their authors opt in.
- **The measured number for ADR-0223 changes**: option A is judged on `erased background` alone.

## Amended 2026-09-22: layer 3 (priority 30–39) over a restored behind-background pixel

Sonnet verification of F12.15 left one edge open: what a **layer-3** `<background>` does to a pixel the tag just restored. Read off `HdNesPack::GetPixels` as shipped, the answer is fixed by pass order:

1. backdrop → layer 0 → behind-background sprites → layer 1 → tile → **layer 2** → the ADR-0224 re-apply (only when `KeepsBehindBgSprite` holds, i.e. layer 2 painted the pixel) → **front sprites** → **layer 3** → the issue-#328 bookkeeping.
2. Layer 3 runs **unconditionally, last**, through the same `DrawBackgroundLayer`. Where its pixel is opaque (`Alpha` blend) it replaces whatever is under it — restored behind-background pixel and front sprite alike; with `Add`/`Subtract` it blends onto both. Where transparent, the restored sprite shows.
3. With no layer-2 paint (`layer2Painted == false`) nothing is re-applied, and layer 3 hides the sprite as today. The tag never fights layer 3.

**Declared edge, proposed as intended.** The tag's contract is layer 2, ADR-0050's priority 20, "behind foreground sprites", and it restores the hardware's rule *for that layer only*. Layer 3 is the format's "foreground" layer, above every sprite of either priority; letting the tag punch through would show a behind-background sprite where a front sprite is hidden, a priority inversion no hardware rule supports. The recorder writes no layer-3 background, so only a hand-written pack meeting the edge can, and that author has asked for a foreground.

**Alternative considered and not taken:** extend the predicate to "any layer ≥ 2 painted" and re-apply after layer 3. Rejected for the inversion above.

**Evidence.** `scripts/core_unit_tests.cpp` BlocoP4, `TestBehindBgSpriteRuleYieldsToALayer3Background`: the model pins (a) opaque layer 3 over a restored pixel → layer 3, tag or no tag; (b) the same over a front sprite → layer 3; (c) no layer-2 paint → no re-apply; (d) transparent layer 3 → the restored sprite shows. `HdNesPack` stays outside the `make core-unit-tests` link set (needs `NesConsole`), so stop condition (2) stays "partially met". Docs: `docs/specs/hires-gbsms-v1-draft.md` §7, `docs/remastering-a-game.md`, the F12.15 log's "What this log does not deliver".
