# ADR-0012: VGM/MIDI exporters are owned per-Emulator (WaveRecorder pattern), not process-global singletons

- Status: accepted
- Date: 2026-08-24

## Context
Raised during decompose, Execute/T1, Execute/T2 and auditor-b (consolidates former ADR-0016, ADR-0017, ADR-0020, ADR-0022, ADR-0024 — first-round ids, since retired and reissued; see ADR-0035). The T1/T2 briefs mandated a 'self-contained static-instance API, no plumbing through SoundMixer/Emulator', and that one constraint produced the cluster: the audit counted the same root cause as eight of eleven review issues across three consecutive reviews. Confirmed in code: (a) both VgmExporter and MidiExporter are file/class-scope static safe_ptr singletons, while every other recorder is emulator-scoped (WaveRecorder via SoundMixer, AviRecorder via VideoRenderer, MovieRecorder via MovieManager); (b) concurrent consoles — VS DualSystem sub-console (commit 0155e22f), Super Game Boy, RecordedRomTest, netplay/history-viewer — interleave into one stream with no attribution; (c) recording state survives ROM load/unload and reset with no defined behavior; (d) no lock protects the mutation of _stream/_lastEventTime/_trackData/_tickAccumulator — safe_ptr makes acquiring the pointer safe, never the call, and this applies to VgmExporter::LogWrite as much as MidiExporter::Log; (e) the no-timestamp signatures forced the timing defects handled in ADR-0013.

## Decision
Move ownership to SoundMixer/Emulator (safe_ptr member, mirroring _waveRecorder), reached through a thin static accessor / the existing console pointer so the chip write-sites written in AC-3/4/5 do not change; start/stop follows the WaveRecord/WaveStop lifecycle and Emulator/ROM-unload teardown calls StopRecording(). Do not ship the current state where the contract is neither enforced nor written down — it must land before the F1.3 UI action makes the feature reachable by users.

**Shipped:** the per-Emulator variant. `Core/Shared/Audio/SoundMixer.h` holds `safe_ptr<VgmExporter> _vgmExporter` and `safe_ptr<MidiExporter> _midiExporter` next to `_waveRecorder`, exposed through `GetVgmExporter()`/`GetMidiExporter()` (plain pointer loads; the null check is the whole guard). No static singleton remains in either exporter header.

## Consequences
Resolves lifetime finalization and cross-thread access in one move; captures become attributable per instance. Hot-path cost of emulator-scoped access is resolved by ADR-0011's cached per-console flag. Process lesson: a structural finding that recurs across consecutive task reviews belongs in a spec change, not re-litigated downstream.

## Alternatives
Keep the process-global singleton with a documented single-emulator contract (defers multi-instance correctness; solves neither attribution nor lifecycle); or add locking to the global (fixes races, not attribution or reset/unload semantics). The static facade with minimum hardening — single-active-console contract in both headers, teardown calling StopRecording(), and a lock around Log()/Stop() keyed per console tag — was the rejected interim.
