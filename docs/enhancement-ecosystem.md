# MesenAI Community Enhancement Ecosystem

*Status: maintained front-door narrative. The consolidated, binding roadmap
(Part A: pack/core; Part B: player GUI) and its shipped record live in
[docs/roadmap/PRD-mesence-enhancement-ecosystem.md](roadmap/PRD-mesence-enhancement-ecosystem.md);
the open specs live in [docs/specs/](specs/). This page is the short
why/vision — where it drifts from the PRD or a spec, the PRD and the spec win.*

MesenAI is a platform for **extracting, authoring and consuming community
enhancement packs** — textures, music, synth presets and a border frame — while keeping the
emulator itself legally clean. The thesis is proven: the relaunched
[SUPER ZSNES](https://www.zsnes.com/) built its whole product around per-game
curated enhancements (hand-drawn hi-res art, audio replacement, overclock),
each individually toggleable, with enhancement data kept free of copyrighted
content. MesenAI already ships the foundations needed to do the same as an
open ecosystem:

| Foundation | Where | What it provides |
|---|---|---|
| Tile replacement (HDNes `hires.txt`) | `Core/NES/HdPacks/` (NES), `Core/Shared/HdPacks/` (GB/GBC, SMS/GG) | Per-context conditions, the **HD Pack Builder** (in-emulator tile recorder), and OGG audio replacement on NES |
| MEP pack envelope + community catalog | `Core/Shared/EnhancementPacks/`, `UI/Services/CommunityPackCatalogFetcher.cs` | A pack carrying textures, audio, synth presets and — since MEP v1.5 §5.4 — a border frame, keyed by the ROM's No-Intro hash and linted by the pack CI, listed in the catalog and installed automatically for the matching ROM (ADR-0146, ADR-0148, ADR-0149) |
| Enhanced Synth Engine + music log | `Core/Shared/Audio/EnhancedSynthEngine.*`, `MidiExporter.*`, `VgmExporter.*` | A live tap that converts chip register state into note/voice abstractions; the MIDI exporter (SMF type 1 + GM) consumes it, a VGM 1.71 exporter logs the raw chip writes, and both ship behind **Tools → Record Music (MIDI/VGM)** |

## Principles

1. **Ship the tool, never the content.** Extractors are legal (interoperability);
   their outputs stay on the user's machine. Extracted tiles and transcribed music are
   still copyrighted works (a MIDI of a game tune is a transcription of the
   composition, like sheet music — changing format never clears the musical work).
2. **The official channel carries only clean data:** synth presets, ROM-hash mappings,
   index manifests, tools, and original compositions with explicit licenses. One
   deliberate exception: short before/after demonstration excerpts and gameplay
   screenshots in `docs/media/` — the same de facto practice every emulator's
   documentation relies on — kept brief, credited where a community author is
   involved, and never full tracks or complete asset sets.
3. **Derivative content lives in the existing community hubs** (Zeldix for SNES audio
   packs, VGMusic for MIDI, romhack.ing / individual GitHub repos for texture packs) —
   the same separation bsnes keeps from the MSU-1 pack sites.
4. **The emulator is content-agnostic.** It reads the project's catalog of pack
   URLs and hashes (an MEI manifest) and installs what that catalog lists; it
   never bundles, hosts, endorses, or embeds any P2P distribution of derivative
   content (inducement liability — *MGM v. Grokster*, 2005; the 2024 Yuzu settlement).

## Where the detail lives

- **Standards** (adopted + proposed), product consoles in scope, roadmap phases,
  and the shipped record: the PRD — Part A §1–§5. Don't maintain a second
  enumeration here.
- **Open specs** (CC0, RFC 2119, golden files): [`docs/specs/`](specs/) —
  `ESP-v1`, `MEP-v1`, `MEI-v1`, `MEP-recipe-v1`, `hires-gbsms-v1` (draft); see
  [`docs/specs/README.md`](specs/README.md) for the index.
- **Redrawing a game's art, step by step:** [`docs/remastering-a-game.md`](remastering-a-game.md);
  **submitting the result:** [`docs/hd-pack-authoring.md`](hd-pack-authoring.md).
- **Where the inherited upstream toolchain still serves an author better,
  and the slice that answers each row:** [`docs/hd-pack-toolchain-comparison.md`](hd-pack-toolchain-comparison.md)
  ("Gaps this table names") → PRD Part A §4 (Phase 12 delivered those slices).
  Live in the PRD: Phase 14 (proof at scale), Phase 12's F12.11 human row,
  Phase 13's shared replays (ADR-0205, R.1/R.2 — nothing implemented), and
  Phase 7's P.8 (shaders on macOS, ADR-0237 — accepted, unbuilt).
- **Community catalog:** [`docs/community-packs.md`](community-packs.md) (+ `.json`).

## Non-goals

- Hosting or distributing derivative content (extracted MIDIs, covers, redrawn
  third-party textures) in any project repository.
- Any embedded P2P/torrent sharing mechanism.
- Monetizing packs or the distribution channel.
- Compatibility with the closed SUPER ZSNES enhancement data format.
