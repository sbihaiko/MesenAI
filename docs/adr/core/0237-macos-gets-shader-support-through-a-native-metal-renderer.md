# ADR-0237: macOS gets shader support through a native Metal renderer running the librashader Metal filter chain

- Status: accepted (2026-09-26, user's pick verbatim: *"escreva o ADR utilizando o natinvo no Metal"*); slice P.8 in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part A §4, Phase 7) **implemented 2026-10-02** under a go-ahead relayed by the coordinating session that day, except acceptance item 3 (the human check on a real display), which is not evaluated. **Stop condition (3) remains unevaluated by the owner's decision (2026-10-05, verbatim: *"Deixa pendente e documentado"*)** — asked how to resolve the human-only acceptance, the owner chose to leave it pending and documented, so leaving it open is a decision, not an omission. §1, §2 and acceptance items 1–2 are reflected in code: `MacOS/MacOSMetalRenderer`, `MacOS/MetalPresenter`, `make metal-presenter-tests`, `scripts/check_headless_shader_invariance.sh`. **§3 amendment accepted (2026-10-02, user's pick verbatim: *"Criar release e aceitar (Recomendado)"*):** it records what the slice actually ships — a prebuilt, sha256-pinned SourMesen CI dylib, mirrored as an asset of this repo's release `librashader-macos-arm64-01febce6` — in place of the original "built from source". The mirror release was created the same day (`gh release create ... --latest=false`; `scripts/fetch_librashader_macos.sh --source mirror` resolves it and the sha256 matches the pin); the script falls back to the original artifact until it expires on 2026-12-04. The original §3 text is kept below.
- Date: 2026-09-26
- Related: ADR-0163 (fork–upstream coexistence tiers), ADR-0203 (CI builds Windows and macOS Apple Silicon), ADR-0204 (download channel), upstream `392421500` "Added support for shaders (Windows + Linux) (#270)", merged here by PR #440
- Supersedes / amends: nothing; amended in place 2026-10-02 (§3, accepted — see Status)
- Amended by: ADR-0246 (accepted 2026-10-02) — non-goals: a short bundled list of named looks is allowed. ADR-0246 §4 carries the decision and its rationale; the original clause is kept in git history.

## Context

Upstream commit `392421500` added RetroArch-format shader presets through
librashader on Windows (the Direct3D `Windows/Renderer`) and Linux (the new
`Linux/LinuxOglRenderer`); the fork's macOS build got none of it:

- `InteropDLL/EmuApiWrapper.cpp` `InitRenderer()` returns a `SoftwareRenderer`
  under `__APPLE__`, so the Core hands finished frames to the UI as pixel buffers
  (`SoftwareRendererView`, a `DynamicBitmap`) and no GPU sees them.
- `Utilities/Video/LibrashaderUtilities.h` `IsShaderSupportEnabled()` returns
  `false` under `__APPLE__`, so `CheckShaderSupport()` is false and
  `VideoConfigViewModel.ShowShaderConfig` hides the shader group.
- `.github/workflows/build.yml` fetches `librashader.dll` and `librashader.so`
  from SourMesen's librashader fork; there is no macOS artifact. (Wrong, found
  2026-10-02: the same workflow publishes `librashader-arm-macos`; see §3.)

Already in place: `Utilities/Video/librashader_ld.h` declares the Metal runtime
(`libra_mtl_filter_chain_create`, `…_frame`, `…_free` and the rest) and
`dlopen`s `librashader.dylib` on Apple; the shader settings, per-shader parameter
files (`ShaderConfig`) and config window are platform-neutral C#. macOS Apple
Silicon is a published release target (ADR-0203) and the maintainer's own
platform, so the feature is invisible to the person who develops and tests the
fork.

Alternatives: **filter inside the software path** (offscreen Metal texture,
readback into the UI bitmap) — rejected, because a CRT shader must run at the
display's resolution, not the NES's 256×240: on a Retina or 4K panel that is 6–8
million pixels (about 33 MB) per frame against a `DynamicBitmap` path sized for
frames of about 1 MB, adding stutter and a frame of input latency, and it would
edit the inherited `SoftwareRenderer` into ADR-0163's modified-by-us tier. **Wait
for upstream** — rejected; the maintainer's platform would stay hidden.

Non-goals:

- Shaders never touch recording, capture, HD-pack building or any measurement
  surface: `headless_record` screenshots, OAM dumps and the artist kit stay
  unfiltered. A shader is a display effect only.
- No shader authoring or per-game policy; the user points at an existing RetroArch
  `.slangp`, as upstream does.
  - **Amended 2026-10-02 (user's decision, *"Sim, 2–3 estilos"*):** the "no
    bundled preset catalogue" clause is lifted for a **short named list** of two
    or three looks (for example *CRT TV* and *Handheld LCD*), offered by Settings
    › Look › Screen (PRD Part B §13, W-P10). Each is a `.slangp` plus its passes,
    with a GPL-3.0-compatible license; the slice records each preset's source,
    license and sha256. The display-only rule stands, and adding a look is a code
    change, not a catalogue browser or download path.
- No Intel macOS leg (ADR-0203 narrowed the release to Apple Silicon).

## Decision

1. **A native Metal renderer.** A new `MacOSMetalRenderer` (Objective-C++, under
   `MacOS/`) implements `IRenderingDevice` the way `Windows/Renderer` and
   `Linux/LinuxOglRenderer` do:
   - presents into a `CAMetalLayer` on the viewer's native view, the view whose
     handle `InitializeEmu` already receives;
   - uploads each frame to an `MTLTexture`, runs `libra_mtl_filter_chain_frame`
     from it into the drawable when a shader is configured, and does a plain
     scaled blit when none is;
   - draws at the size the UI computes through `RendererViewportFit` and passes
     to `SetRendererSize`.
2. **Selection.** Under `__APPLE__`, `InitRenderer()` returns
   `MacOSMetalRenderer`; `softwareRenderer == true` and headless runs keep
   `SoftwareRenderer`; `IsShaderSupportEnabled()` returns `true` on Apple, and
   `CheckShaderSupport()` stays the gate — when the dylib is missing the shader
   group stays hidden and the renderer runs unfiltered.
3. **The library.** *(Amended 2026-10-02 — accepted by the maintainer. The text
   accepted 2026-09-26 read: "`librashader.dylib` for macOS arm64 is built from
   source, since SourMesen publishes none. It is pinned to the same librashader
   revision the other two platforms fetch." Its premise was false:
   SourMesen/librashader's `build` workflow publishes a `librashader-arm-macos`
   artifact on branch `Mesen`.)*
   - `librashader.dylib` for macOS arm64 is the **prebuilt** SourMesen/librashader
     branch-`Mesen` CI artifact `librashader-arm-macos`, workflow run 33975397584
     (commit `01febce641d9e97ff99bfa266bcb17343609e296`, `cargo run -p
     librashader-build-script -- --profile optimized -- --no-default-features
     --features runtime-metal` on `macos-15`), pinned by sha256: dylib
     (`9c47230f…91ca`) and artifact zip (`081f6a23…3475`, GitHub's own digest).
     That commit is the branch head the Windows and Linux legs fetched when the
     pin was taken; those legs are not pinned, so the two may drift apart.
   - The pinned dylib is **mirrored** as an asset of this repo's GitHub release
     `librashader-macos-arm64-01febce6` (not latest, so `releases/latest` keeps
     resolving to the player release, ADR-0204), with the MPL-2.0 and GPL-3.0
     texts, the source archive of the pinned commit and a `SHA256SUMS`. GitHub
     expires Actions artifacts (this one 2026-12-04), so the artifact alone
     cannot be a build input.
   - `scripts/fetch_librashader_macos.sh` takes the mirror first and the original
     artifact (by its run URL, never the branch URL) only until its expiry. Bytes
     from either source that miss a pin fail the fetch; they never fall through
     to the other. Moving the pin means mirroring the new build under a new tag
     first, then changing the tag and the three hashes together.
   - A source build stays possible through `--from <dylib>`, which checks arm64
     and the Metal entry points and reports the result UNPINNED.
   - Fetched by the macOS CI legs, bundled inside the `.app` next to
     `MesenCore.dylib`, signed with the bundle.
   - Licensing: librashader's runtime and `librashader-capi` are
     "MPL-2.0 OR GPL-3.0-only" (`librashader-capi/Cargo.toml`; the C headers are
     MIT). The fork redistributes the dylib under MPL-2.0 §3.2: unmodified, with
     the license text and a pointer to the Source Code Form. The mirror carries
     both; the `.app` loads it dynamically through `librashader_ld.h`, as
     upstream's README prescribes for a non-MPL host.
4. **Acceptance.**
   - With a shader set, the presented frame differs from the unfiltered frame;
     with none set, it matches the software path's output at the same size.
   - Every `headless_record` output (screenshots, OAM dumps, `hires.txt`) is
     byte-identical with and without a shader configured.
   - The slice closes with a human check on a real display: the shader group is
     visible in Video settings, a CRT preset visibly applies, and there is no
     visible stutter at the panel's native resolution.

## Consequences

- Fastest path at display time: the GPU filters and presents the frame directly,
  no readback and no second upload, matching upstream's other two platforms.
- The most new native code of the options considered, and the only one depending
  on Avalonia's native-view hosting on macOS — a risk to confirm first in P.8:
  the `viewerHandle` must be a view that can back a `CAMetalLayer`.
- New files sit in the fork's added-by-us tier (ADR-0163), so they do not
  conflict on sync. If upstream later ships its own macOS renderer the two
  collide head-on; on that sync, take upstream's and retire ours unless it
  measurably regresses.
- macOS gains a second native library to bundle and sign. Under the amended §3,
  CI does not gain a Rust build; it downloads a binary the fork did not build,
  trusted by pin rather than reproduction (a rebuild never matches the published
  bytes). The fork now hosts a third-party binary and owes its MPL-2.0
  obligations on that release. The stale-native-library trap applies: the dylib
  inside the `.app` must be the one just fetched (`scripts/release_macos.sh`
  compares the copy byte for byte).
- The display path is no longer one code path on every platform: a macOS-only
  bug now has to be told apart from a Core bug by re-running with
  `softwareRenderer`.
