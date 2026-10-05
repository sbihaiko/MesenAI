# ADR-0014: MIDI export's dependency on EnableEnhancedAudio is decided explicitly before the F1.3 menu action ships

- Status: accepted
- Date: 2026-08-24
- Related: ADR-0001 (the note-onset heuristic this dependency decision sits on)

## Context
T4 says the wrappers feed an EnhancedSynthEngine::Input snapshot on every MixAudio flush whenever MIDI recording is active, but Core/NES/EnhancedSynth.cpp:93-104 returns before building it whenever cfg.EnableEnhancedAudio is false (the GB/SMS wrappers mirror this; the early-return also covers run-ahead frames). MidiExporter::LogFrame is fed exactly that snapshot, so a user who wants a MIDI transcription but leaves Enhanced Audio off — a plausible default — gets a silently empty file with no error: a valid .vgm next to a 3-track header-only .mid. This is the only user-visible functional gap among the decompose-round findings and the cheapest to fix; the audit round surfaced a second silent failure (an unwritable output path destroys the capture, ADR-0033) and one adjacent hazard in the same handler: both output filenames derive from a single prompt via Path.ChangeExtension, so a ROM name containing a dot ('Zelda v1.2') silently truncates at the wrong separator for both files.
Consolidates two findings that shared an id: a decompose-phase ADR-0027, retired and reissued (ADR-0035), and audit-round ADR-0023, ADR-0026 and ADR-0027, from the reused 0022–0032 range.

## Decision
Fix at the cheapest point: the combined Record Music OnClick checks the active console's EnableEnhancedAudio at start time and surfaces a one-line MessageManager notice when MIDI capture will be empty — do not restructure the gate and do not split the menu into per-format entries. Document the dependency in MidiExporter.h and keep the IsRunAheadFrame() exclusion so replayed frames are not double-logged. While in that handler, derive the two filenames dot-safely (append the extensions rather than Path.ChangeExtension over the shared prompt result). This lands before the F1.3 menu action makes the feature reachable by users.

## Consequences
No silently-empty exports without feedback, at the cost of a one-line notice instead of a structural change; the divergent activation contracts remain a documented v1 limitation. The dot-in-ROM-name truncation stops corrupting both output paths.

## Alternatives
Build and feed the Input snapshot whenever MIDI recording is active even with the synth disabled, skipping only Render(): removes the limitation but costs snapshot construction and restructures a gate the spec explicitly accepted for v1. Gate/disable the MIDI half of the menu action: heavier UI dependency for the same information. Ship as-is and document in release notes: users hit an empty-file failure with no feedback, violating the MuseScore success criterion for the most likely default configuration.
