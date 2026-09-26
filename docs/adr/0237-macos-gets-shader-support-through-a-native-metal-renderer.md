# ADR-0237: macOS gets shader support through a native Metal renderer running the librashader Metal filter chain

- Status: accepted (2026-09-26, user's pick verbatim: *"escreva o ADR utilizando o natinvo no Metal"*); **not implemented** — pending slice P.8 in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part A §4, Phase 7), no go-ahead to implement yet
- Date: 2026-09-26
- Related: ADR-0163 (fork–upstream coexistence tiers), ADR-0203 (CI builds Windows and macOS Apple Silicon), ADR-0204 (download channel), upstream `392421500` "Added support for shaders (Windows + Linux) (#270)", merged here by PR #440
- Supersedes / amends: nothing

## Context

Upstream commit `392421500` added RetroArch-format shader presets through
librashader, on Windows (the Direct3D `Windows/Renderer`) and Linux (the new
`Linux/LinuxOglRenderer`). The fork's macOS build received none of it:

- `InteropDLL/EmuApiWrapper.cpp` `InitRenderer()` returns a
  `SoftwareRenderer` under `__APPLE__`, so the Core hands finished frames to
  the UI as pixel buffers (`SoftwareRendererView`, a `DynamicBitmap`) and
  nothing on the GPU sees them.
- `Utilities/Video/LibrashaderUtilities.h` `IsShaderSupportEnabled()` returns
  `false` under `__APPLE__`, so `CheckShaderSupport()` is false and
  `VideoConfigViewModel.ShowShaderConfig` hides the whole shader group.
- `.github/workflows/build.yml` fetches `librashader.dll` and `librashader.so`
  from SourMesen's librashader fork; there is no macOS artifact to fetch.

What is already in place: `Utilities/Video/librashader_ld.h` declares the
Metal runtime (`libra_mtl_filter_chain_create`, `…_frame`, `…_free` and the
rest) and `dlopen`s `librashader.dylib` on Apple. The shader settings, the
per-shader parameter files (`ShaderConfig`) and the config window are
platform-neutral C#.

macOS Apple Silicon is a published release target (ADR-0203) and the
maintainer's own platform, so today the feature is invisible to the person
who develops and tests the fork.

Alternatives considered:

- **Filter inside the software path** — keep `SoftwareRenderer`, run the
  chain on an offscreen Metal texture and read the result back into the
  bitmap the UI shows. Rejected. A CRT shader has to run at the display's
  resolution, not the NES's 256×240: on a Retina or 4K panel that is 6–8
  million pixels (about 33 MB) per frame, which the `DynamicBitmap` path would
  copy back and upload again 60 times a second. That path was sized for
  frames of about 1 MB. The likely cost is stutter and a frame of added input
  latency, and retro players notice latency. It would also edit the
  inherited `SoftwareRenderer`, moving it into ADR-0163's modified-by-us
  tier.
- **Wait for upstream** — rejected. The maintainer's platform would keep the
  feature hidden for an unknown time.

Non-goals:

- Shaders never touch recording, capture, HD-pack building or any
  measurement surface. `headless_record` screenshots, OAM dumps and the
  artist kit stay unfiltered; a shader is a display effect only.
- No shader authoring, bundled preset catalogue or per-game shader policy —
  the user points at an existing RetroArch `.slangp`, as upstream does.
- No Intel macOS leg (ADR-0203 narrowed the release to Apple Silicon).

## Decision

1. **A native Metal renderer.** A new `MacOSMetalRenderer` (Objective-C++,
   under `MacOS/`) implements `IRenderingDevice` the way `Windows/Renderer`
   and `Linux/LinuxOglRenderer` do:
   - It presents into a `CAMetalLayer` on the viewer's native view, the view
     whose handle `InitializeEmu` already receives.
   - It uploads each frame to an `MTLTexture`, runs
     `libra_mtl_filter_chain_frame` from that texture into the drawable when
     a shader is configured, and does a plain scaled blit when none is.
   - It draws at the size the UI already computes through
     `RendererViewportFit` and passes to `SetRendererSize`.
2. **Selection.**
   - Under `__APPLE__`, `InitRenderer()` returns `MacOSMetalRenderer`.
   - `softwareRenderer == true` and headless runs keep `SoftwareRenderer`.
   - `IsShaderSupportEnabled()` returns `true` on Apple, and
     `CheckShaderSupport()` stays the gate: when the dylib is missing, the
     shader group stays hidden and the renderer runs unfiltered.
3. **The library.**
   - `librashader.dylib` for macOS arm64 is built from source, since SourMesen
     publishes none. It is pinned to the same librashader revision the other
     two platforms fetch.
   - It is fetched by the macOS CI legs, bundled inside the `.app` next to
     `MesenCore.dylib`, and signed with the bundle.
4. **Acceptance.**
   - With a shader set, the presented frame differs from the unfiltered
     frame. With none set, the presented frame matches the software path's
     output at the same size.
   - Every `headless_record` output (screenshots, OAM dumps, `hires.txt`) is
     byte-identical with and without a shader configured.
   - The slice closes with a human check on a real display: the shader group
     is visible in Video settings, a CRT preset visibly applies, and there is
     no visible stutter at the panel's native resolution.

## Consequences

- The fastest path at display time. The GPU filters and presents the frame
  directly, with no readback and no second upload, matching how upstream
  built the other two platforms.
- The most new native code of the options considered, and the only one that
  depends on Avalonia's native-view hosting on macOS. That dependency is a
  risk to confirm first in P.8: the `viewerHandle` must be a view that can
  back a `CAMetalLayer`.
- New files sit in the fork's added-by-us tier (ADR-0163), so they do not
  conflict on sync. If upstream later ships its own macOS renderer, the two
  collide head-on. On that sync, take upstream's renderer and retire ours
  unless it measurably regresses.
- CI gains a Rust build of librashader, and macOS gains a second native
  library to bundle and sign. The stale-native-library trap applies: the
  dylib inside the `.app` must be the one just built.
- The display path is no longer one code path on every platform. A bug that
  appears only on macOS now has to be told apart from a Core bug by
  re-running with `softwareRenderer`.
