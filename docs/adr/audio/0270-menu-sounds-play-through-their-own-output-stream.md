# ADR-0270: A menu tick plays through its own output stream, owned by the host audio layer — never as a second writer on the emulator's device

- Status: accepted (2026-10-09), by the autonomy panel (panel-adversary, Opus
  5.5 as the human proxy; not provisional), pick quoted verbatim:
  **"Accept D1–D10 as written: menu blips play through their own host-owned
  output stream, never through SoundMixer/IAudioDevice; #1126 implements it
  with host-free unit tests against a fake sink (FIFO silence-on-empty,
  one-blip drop, settle from queued frames, armed-at-start capability), and
  the Context/Measured claim that the exports are absent is corrected to
  'present as stubs returning false (InteropDLL/EmuApiWrapper.cpp:272-280)'
  before merge."**
  The decision comes from the spec on issue #1102 (slice 6, "Menu sounds").
  Nothing is implemented by this ADR yet: the slice is #1126 (the stream and
  its sinks in the audio layer, the two `InteropDLL` exports becoming real,
  the capability wiring already in place), with #1127 routing the keyboard
  path through the same seam as its follow-up. This ADR was itself ordered by
  the panel's recorded ruling on issue #1105 (Amendment 2026-10-09, Opus 5.5
  as the human proxy, challenger stance), quoted verbatim — "Narrow #1117 to
  the Settings row and pad wiring with the host entry point returning not
  available; the row stays hidden until a real audio path exists; the audio
  path gets its own ADR." — which ordered the ADR but picked no path; the
  pick above is the one that accepts the Decision. The options weighed and
  not taken are in Alternatives.
- Date: 2026-10-09
- Related: spec issue #1102 (slice 6, "Menu sounds"), issue #1105, PR #1117,
  issues #1126 and #1127, ADR-0256 (the pad belongs to the console while a
  game runs), ADR-0254 (the Play overlay and the lost-focus reason),
  ADR-0203 (Windows + Apple-Silicon macOS is what ships), ADR-0240 (how a
  `proposed` ADR becomes `accepted` here), `UI/Logic/MenuSounds.cs`,
  `UI/Windows/MenuSoundOutput.cs`, `Core/Shared/Audio/SoundMixer.cpp`,
  `Sdl/SdlSoundManager.cpp`, `Core/Shared/Audio/AsyncAudioDeviceOpen.h`
- Supersedes / amends: nothing. It reads #1102 slice 6's "through the
  existing audio output" as "through the host's existing output device and
  audio stack" — see Decision D2 — and records that reading rather than
  editing the spec.

## Context

### What is being decided

Play's optional tick on move / confirm / back is already rendered, gated and
wired to a seam. What it does not have is a place to come out. This ADR
decides that place: which module owns the device the blip is written to, on
which thread, under which volume rules, with which settle rule, and with
which failure mode. The register files by the decision's **subject**, and the
subject here is an output device's ownership, lifetime and threading, so this
is `audio/` even though its effect lands in Play's GUI (`gui/` would file it
by where the effect lands, which is the mistake the area rule names).

### The path as it stands (read at `3cbcab39d`)

- **The blip set is generated in the UI and is host-free.**
  `UI/Logic/MenuSounds.cs` renders three short sine blips at a fixed 15 % of
  full scale (`MenuSounds.Level`), interleaved stereo 16-bit at 48 kHz
  (`MenuSounds.SampleRate`); `MenuSounds.For` says which action sounds and
  `MenuSounds.ShouldPlay(enabled, gameRunningUnpaused)` says whether a sound
  exists at all. Both rules are pure and pinned in
  `UI.Tests/Play/MenuSoundsTests.cs`.
- **The press path and its gate.** `UI/Windows/PlayPadNavigationWiring.cs`
  runs, for a press, `Apply(action)` and then `PlayMenuSound(action)`, which
  asks only when `MenuSounds.ShouldPlay(Config.Audio.MenuSounds,
  EmuApi.IsRunning() && !EmuApi.IsPaused())`. Judging after `Apply` is
  deliberate: the press that starts or resumes a game has already left the
  game running unpaused, so it does not blip over it.
- **The seam is declared, and the host side is a stub.** `UI/Windows/MenuSoundOutput.cs`
  renders the PCM and calls the host entry point `EmuApi.PlayMenuSound(pcm,
  frames, rate)`, with `EmuApi.MenuSoundsAvailable()` as the capability. Both
  exports exist in `InteropDLL/EmuApiWrapper.cpp:272-280` as stubs that return
  `false` without touching a device or a lock (the same stubs are on
  `origin/main`), so nothing carries a blip and
  `MenuSoundOutput.HostAvailable()` answers false on the stub's own answer —
  not because a P/Invoke threw `EntryPointNotFoundException`, which stays
  only as the guard for a build that ships no core at all.
  `UI/App.axaml.cs` wires that answer into
  `PlayerSettingsEssentials.MenuSoundsAvailable` (default `() => false`), and
  while it is false the Audio sheet keeps three rows and its height (340 px)
  and the fourth row, *Menu sounds*, stays hidden (`UI/Logic/PlayerSettingsEssentials.cs`,
  `UI.HeadlessTests/WireframeCoverageRenderTests.cs`).
- **The existing output is fed by the emulation thread only.**
  `SoundMixer::PlayAudioBuffer` is called by the console APUs
  (`Core/NES/NesSoundMixer.cpp`, `Core/Gameboy/APU/GbApu.cpp`,
  `Core/GBA/APU/GbaApu.cpp`, `Core/SMS/SmsPsg.cpp`) and is the only caller of
  `IAudioDevice::PlayBuffer`. It flushes the device only while
  `!_emu->IsPaused()` and `cfg.EnableAudio`; master volume
  (`AudioConfig::MasterVolume`, the audio player's own volume, and the
  background / turbo / rewind reductions) is applied inside it.
- **While Play is up, nothing feeds it.** With no console loaded,
  `Emulator::Run` returns at once; while paused it parks in
  `WaitForPauseEnd()` (`Emulator::OnBeforePause` → `SoundMixer::StopAudio(false)`
  → `IAudioDevice::Pause()`, then a 30 ms sleep loop). A menu blip plays, by
  rule, exactly when no game runs unpaused: the two states are disjoint, so
  the game's flush is never available to carry a blip.
- **The device's reader loops, it does not drain.** In `Sdl/SdlSoundManager.cpp`
  the output is a raw circular byte buffer with plain `uint32_t` read/write
  positions, and the callback wraps and keeps reading whether or not a writer
  advanced. That is why the game pauses the device at all ("Prevent audio from
  looping endlessly while game is paused"), why a caught-up read counts a
  buffer-underrun event, and why `ProcessEndOfFrame` may `Stop()` (clearing
  both positions) when the measured latency drifts more than 50 ms from the
  configured one. Consequence: a blip written into that ring while the device
  is idle is followed by up to a buffer of stale game audio, and a pause that
  lands mid-blip leaves a stale tail for the next one.
- **It is one device, per platform, behind `IAudioDevice`.** `SdlSoundManager`
  (macOS and Linux) and `Windows/WasapiSoundManager` / `Windows/DirectSoundManager`
  are chosen from `AudioConfig::AudioBackend` (`Windows/SoundManager.h`,
  `InteropDLL/EmuApiWrapper.cpp`'s `InitSoundManager`). Opening can take
  seconds on a bad CoreAudio device, which is why the open has its own
  host-free unit, `Core/Shared/Audio/AsyncAudioDeviceOpen.h`, driven by a fake
  in `scripts/core_unit_tests.cpp`.
- **The rejected attempt is on the record.** PR #1117 built the path as a
  second writer on that device: the UI thread wrote the blip into the game's
  ring, a separate settler thread paused the device afterwards, and the open
  ran asynchronously. Four review rounds, each finding a new class of defect:
  the UI-thread writer racing the emulation thread; a device that never
  paused; the async open dropping the first blip; the settler calling `Pause`
  on a device it did not own and that could be freed under it; a blocking wait
  under a lock; stale ring contents. The panel then narrowed #1117 to the row
  and the pad wiring and ordered the path to be decided here (#1105).

### Non-goals

The blip set's sound design and level; the Settings row's own layout; the pad
tick (#1112, #1121, #1122) and its host-side aimability, which is a different
device and a different decision; the keyboard path (#1127) beyond the hook it
reuses; and any change to the emulator's mix chain, its device, its ring
semantics, its latency feedback or its pause ownership.

## Decision

**D1 — The seam stays the one already declared.** `EmuApi.PlayMenuSound(short[]
pcm, uint32 frameCount, uint32 sampleRate)` and `EmuApi.MenuSoundsAvailable()`
keep their names, their argument order and their meaning: the first submits
one whole blip (`frameCount` is frames — pairs of `int16_t` — at the rate
given, the PCM exactly as `MenuSounds.Render` produced it), the second answers
whether a menu-sound output exists at all. The UI keeps rendering the blip, so
nothing binary ships and the level stays the UI's fixed constant.

**D2 — The menu blip gets its own output stream, owned by the host audio
layer.** One module in the audio layer owns a menu-sound stream: its own
output device, its own small FIFO and its own thread. It never registers with
`SoundMixer`, never appears in `IAudioDevice`'s contract, never touches the
emulator's device, its ring, its latency statistics or its pause ownership,
and never enters the game's mix chain (equalizer, reverb, crossfeed, pitch
adjust, the recording taps). The emulator's device keeps exactly the owners it
has today: one writer (the emulation thread) and one owner of play/pause (the
game path). This is the reading of #1102 slice 6's "through the existing audio
output": the blip goes out through the host's existing output device and
audio stack, the same one the player picked — not into the emulator's mix,
which by the last Context bullet cannot carry it.

**D3 — The device is not the emulator's, and the reader is a FIFO.** The
stream's reader emits silence when its queue is empty; it never repeats the
last block. That property is what makes every other rule here safe: a pause
that lands late outputs nothing (unlike the game ring, where it replays stale
audio), and a pause that lands a frame or two early costs at most the tail of
a 40–90 ms blip.

**D4 — One producer, one owner of everything else.** The caller — the UI
thread, or any later input path — may only *submit*: an allocation-free,
lock-free, bounded push that never blocks, never opens, pauses, closes or
waits on a device, and returns at once (false when there is nothing to do
about it: no stream, audio off, or a blip already in flight). Opening the
device, unpausing, settling, pausing and releasing belong to the stream's own
thread, which is also the thread that joins before the device is released.
No lock is held across an open, and no caller ever holds a device pointer.
This is the single rule that answers all six #1117 defects.

**D5 — At most one blip exists at a time, and it is whole.** The queue holds
one submitted block of frames. A submit while one is queued or playing is
dropped, not stacked (a repeat on a held direction is a new press, and
stacking would turn it into a machine-gun). The queue is emptied whenever the
stream pauses, stops or is closed, so a later blip can never play a stale
tail. A blip is written frame by frame from its start; it is never resumed
from the middle.

**D6 — Master volume applies; the game's ducking does not.** The blip is
scaled by `AudioConfig::MasterVolume` with the same arithmetic the game path
uses (`sample * MasterVolume / 100`), read from the config the audio layer
already owns, at fill time. While `AudioConfig::EnableAudio` is false the
submit is refused outright and nothing is queued — audio off means the app is
silent, game and menu alike. The audio player's own volume, the
mute/reduce-in-background rules and the turbo/rewind reductions are the game
path's and do not apply: they exist to duck a *running game*, and a blip only
ever fires while none runs unpaused.

**D7 — Whether a sound exists stays `MenuSounds.ShouldPlay`, in one place.**
The gate is evaluated on the caller's side, at the moment of the press, on the
state the press left — the rule that is already pure, already pinned by
`UI.Tests/Play/MenuSoundsTests.cs`, and unchanged by this ADR. The stream adds
one cut of its own, on the same predicate read in Core: while the emulator is
running unpaused it does not start a queued blip. A blip that has already
started is not cut — a mid-waveform stop steps the signal — so the worst case
is one blip of at most 90 ms overlapping the first frames of a game the player
launched mid-blip; the launch press itself never blips, because the gate is
judged after `Apply`.

**D8 — The settle is timed from the queued amount.** After a blip is queued
the stream's own thread unpauses the device, and pauses it again only once the
queue is empty *and* the frames it queued have had time to drain: the delay is
derived from the submitted frame count at the blip's own rate (plus the sink's
own buffer), never from a fixed constant and never from a guess about the
backend's latency. A submit that arrives during the wait simply restarts it
with a full queue. The device is left open for the session and paused when
idle — never opened and closed per blip, and never left running to output
silence (which would hold an output client active and wake the audio path
forty-odd times a second for nothing).

**D9 — Arming, capability and failure.** The stream is armed at app start, off
the UI thread, through the same asynchronous open the game device uses
(`Core/Shared/Audio/AsyncAudioDeviceOpen.h` — the open must never run on the UI
thread and must never be waited on under a lock); it is never armed lazily by
the first blip, which is exactly the "async open dropping the first blip"
defect of #1117. The stream exists only when the host asked for audio at all
(the same flag that gates the game device — the headless test runner passes
`noAudio: true`, and there the capability is false). `MenuSoundsAvailable()`
answers true only while the stream is up: a failed open leaves the row hidden,
and a stream that dies mid-session flips the capability back to false and
makes submits no-ops. A blip is never a dialog, never a log line per press,
and never a retry loop on the caller's thread. The sink follows the backend the
game device would use; a backend with no menu sink answers unavailable rather
than routing the blip through a different audio API than the player chose.

**D10 — What #1126 and #1127 may implement without re-opening the path.**
#1126 implements this ADR and nothing else of it: the stream and its sinks in
the audio layer, the two `InteropDLL` exports becoming real, and the
capability wiring already in place
(`PlayerSettingsEssentials.MenuSoundsAvailable`, the Audio sheet's fourth row
and its height). It keeps `MenuSounds.For` / `ShouldPlay` / `Render`, the
`MenuSoundOutput` sink seam and the pad wiring as they are, and it must not
move the gate, add a second one, or touch `SoundMixer`, `IAudioDevice` or any
emulation-thread code. #1127 routes the keyboard path through the same
`MenuSoundOutput.Play` behind the same `MenuSounds.ShouldPlay`: no second
submit path, no second gate, no new sink. If either ticket needs a fact this
ADR does not fix, that is a new ADR, not a widened one.

## Consequences

- **The emulator's audio path is untouched.** No second writer on its ring, no
  second owner of its pause state, no UI blip in its underrun and latency
  statistics, no change to its read semantics. The game path cannot regress
  because of a UI sound, which is the property the four review rounds bought.
- **One extra output client per session.** A device is opened at app start and
  held open (paused) for the session, even though the tick is off by default
  and the row is hidden until the stream is up. That cost is the price of a
  capability that is true rather than optimistic, and it is why the lazy open
  and the "keep it running" variants are rejected below. On a machine whose
  audio device answers slowly this is one more background open, never a stall.
- **The blip's latency is its own.** The menu device is not sized from
  `AudioConfig::AudioLatency`; it uses a small fixed buffer, because a UI tick
  wants an immediate answer and a game's latency preference is about the game.
- **The committed wireframe does not change.** The render harness runs with
  `noAudio: true`, so `MenuSoundsAvailable()` stays false there, the Audio
  sheet keeps three rows, and W-P8b and its hidden-row assertion stay true.
  The fourth-row case keeps being proven through the capability seam, as it is
  today.
- **Bounded overlap on a launch press.** One blip of at most 90 ms can overlap
  the first frames of a game launched from a press that arrived mid-blip (D7).
- **Traps left behind.** The stream must be re-armed if the audio device or
  backend setting changes, and re-arming means the old device is released only
  after its own thread is joined; the capability is read by a settings sheet
  build, so it must be answerable at any time from any thread without opening
  anything; and a sink that is added per backend is a place where a new
  backend can ship silently without a sound path, which is why the capability
  is per-backend rather than per-build.

## Alternatives

- **A second writer on the emulator's device (what PR #1117 shipped).**
  Rejected by four review rounds, one defect class each: the UI thread racing
  the emulation thread on the ring's positions; a device that never paused; an
  async open dropping the first blip; a settler thread calling `Pause` on a
  device it did not own; a blocking wait under a lock; stale ring contents.
  Every one of those is a consequence of the same thing — a device with one
  owner gaining a second — which D2 and D4 remove rather than patch.
- **Mixed into the existing output by the audio owner: the game device's
  reader mixes a small UI queue in.** This is the closer reading of #1102's
  sentence, and it is rejected on three counts. It needs the game ring's read
  semantics changed to "silence once the reader has caught the writer" in
  every backend, or unpausing the device for a blip replays up to a buffer of
  stale game audio (Context); it makes "the device is running" a state with
  two owners again, since the menu path would unpause and the settle would
  pause a device the game path also drives; and it puts UI blips into the
  game's underrun counter and latency average, which `ProcessEndOfFrame` turns
  into a `Stop()`. It buys one fewer device and costs the game path its
  invariants — the wrong side of the trade for a UI tick.
- **Open the menu device lazily on the first blip.** Rejected: it is the
  "async open dropping the first blip" defect of #1117 by construction. The
  player turns the row on, presses once, hears nothing, and concludes the
  feature is broken.
- **Open and close a device per blip.** Rejected: an open can take seconds on
  a bad CoreAudio device, and start/stop churn around every 40–90 ms blip
  produces pops on some backends.
- **Leave the menu device running while the app is up and output silence
  between blips.** Rejected on power and resource grounds: it holds an output
  client active for the whole session and wakes the audio path forty-odd times
  a second to write zeros, for a feature that is off by default.
- **Make the tick silent, or ship the sound as a file asset instead of
  generated PCM.** Not decided here: the row exists, the renderer is pure and
  pinned, and this ADR is only about where the samples go.

## Measured / unverified

Verified by reading at `3cbcab39d`: `UI/Logic/MenuSounds.cs`,
`UI/Windows/MenuSoundOutput.cs`, `UI/Interop/EmuApi.cs` (the two `extern`
declarations; their host side is the pair of stubs at
`InteropDLL/EmuApiWrapper.cpp:272-280`, returning `false` with no device and no
lock — `grep -rn "PlayMenuSound\|MenuSoundsAvailable" Core/ InteropDLL/` finds
those two definitions and nothing else; an earlier draft of this section read
the pair as absent, and both places are corrected),
`UI/Windows/PlayPadNavigationWiring.cs`,
`UI/Logic/PlayerSettingsEssentials.cs`, `UI/App.axaml.cs`,
`Core/Shared/Audio/SoundMixer.cpp:73-180`, `Core/Shared/Emulator.cpp`
(`Run`, `OnBeforePause`, `WaitForPauseEnd`), `Sdl/SdlSoundManager.cpp`,
`Core/Shared/Audio/BaseSoundManager.h`, `Core/Shared/Interfaces/IAudioDevice.h`,
`Windows/SoundManager.h`, `Windows/WasapiSoundManager.h`,
`Core/Shared/Audio/AsyncAudioDeviceOpen.h`,
`scripts/core_unit_tests.cpp` (the #733 fakes),
`UI.Tests/Play/MenuSoundsTests.cs`,
`UI.HeadlessTests/MenuSoundsTests.cs`,
`UI.HeadlessTests/WireframeCoverageRenderTests.cs`, `UI/Utilities/TestRunner.cs`
(`noAudio: true`).

Not run and not measured: no audible check exists or was made — no blip was
heard on any device, and no device was opened twice on one machine; the
platform cost of a second output client (macOS keeps a client on the device,
WASAPI shared mode mixes two, DirectSound is not covered) is an expectation
from the platform documents, not a measurement here; #1117's own diff was not
re-read, so its defect list is taken from the recorded findings on #1105 and
#1125; and whether a specific backend answers 48 kHz s16 stereo without
conversion inside SDL was not probed.
