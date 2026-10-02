# ADR-0237: macOS gets shader support through a native Metal renderer running the librashader Metal filter chain

- Status: accepted (2026-09-26, user's pick verbatim: *"escreva o ADR utilizando o natinvo no Metal"*); slice P.8 in `docs/roadmap/PRD-mesence-enhancement-ecosystem.md` (Part A §4, Phase 7) **implemented 2026-10-02** under a go-ahead relayed by the coordinating session that day, except acceptance item 3 (the human check on a real display), which is not evaluated. §1, §2 and acceptance items 1–2 are reflected in code: `MacOS/MacOSMetalRenderer`, `MacOS/MetalPresenter`, `make metal-presenter-tests`, `scripts/check_headless_shader_invariance.sh`. **§3 amendment (2026-10-02) is proposed, not accepted:** it records what the slice actually ships — a prebuilt, sha256-pinned SourMesen CI dylib, mirrored as an asset of this repo's release `librashader-macos-arm64-01febce6` — in place of the original "built from source". `scripts/fetch_librashader_macos.sh` already prefers that mirror and falls back to the original artifact until it expires on 2026-12-04; the mirror release does not exist until the maintainer creates it. The amendment needs the maintainer's pick (accept it as written, or ask for a source build instead); until then the original §3 text, kept below, is the accepted one and the code diverges from it.
- Date: 2026-09-26
- Related: ADR-0163 (fork–upstream coexistence tiers), ADR-0203 (CI builds Windows and macOS Apple Silicon), ADR-0204 (download channel), upstream `392421500` "Added support for shaders (Windows + Linux) (#270)", merged here by PR #440
- Supersedes / amends: nothing; amended in place 2026-10-02 (§3, proposed — see Status)
- Amended by: ADR-0246 (accepted 2026-10-02) — non-goals: a short bundled list of named looks is allowed (the amended non-goal bullet). The original clause is kept in git history; ADR-0246 §4 carries the decision and its rationale.

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
  (Wrong, found 2026-10-02: the same workflow publishes `librashader-arm-macos`;
  see the §3 amendment.)

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
- No shader authoring or per-game shader policy. The user points at an
  existing RetroArch `.slangp`, as upstream does.
  - **Amended 2026-10-02 (user's decision, *"Sim, 2–3 estilos"*):** the
    "no bundled preset catalogue" clause is lifted for a **short named
    list** of two or three looks (for example *CRT TV* and *Handheld LCD*),
    offered by Settings › Look › Screen (PRD Part B §13, W-P10).
  - Each bundled preset is a `.slangp` plus its passes. It must carry a
    license compatible with the app's (GPL-3.0). The slice that picks
    them records each preset's source, license and sha256.
  - The display-only rule above is unchanged: a bundled look never
    reaches screenshots, recordings or the kit.
  - This is not a catalogue browser and not a download path; adding a
    look is a code change.
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
3. **The library.** *(Amended 2026-10-02 — proposed, awaiting the
   maintainer's pick. The text accepted on 2026-09-26 read: "`librashader.dylib`
   for macOS arm64 is built from source, since SourMesen publishes none. It
   is pinned to the same librashader revision the other two platforms
   fetch." Its premise was false: SourMesen/librashader's `build` workflow
   publishes a `librashader-arm-macos` artifact on branch `Mesen`.)*
   - `librashader.dylib` for macOS arm64 is the **prebuilt** binary from
     SourMesen/librashader branch `Mesen`, CI artifact
     `librashader-arm-macos` of workflow run 33975397584 (commit
     `01febce641d9e97ff99bfa266bcb17343609e296`, `cargo run -p
     librashader-build-script -- --profile optimized -- --no-default-features
     --features runtime-metal` on `macos-15`). It is pinned by sha256: the
     dylib (`9c47230f…91ca`) and the artifact zip (`081f6a23…3475`, GitHub's
     own digest). That commit is the branch head the Windows and Linux legs
     fetched when the pin was taken; those legs are not pinned, so the two
     may drift apart when the branch moves.
   - The pinned dylib is **mirrored** as an asset of this repo's GitHub
     release `librashader-macos-arm64-01febce6` (not marked latest, so
     `releases/latest` keeps resolving to the player release, ADR-0204),
     together with the MPL-2.0 and GPL-3.0 texts, the source archive of the
     pinned commit and a `SHA256SUMS`. GitHub expires Actions artifacts
     (this one on 2026-12-04), so the artifact alone cannot be a build
     input.
   - `scripts/fetch_librashader_macos.sh` takes the mirror first and the
     original artifact (by its run URL, never the branch URL) only until its
     expiry date. Bytes from either source that miss a pin fail the fetch;
     they never fall through to the other source. Moving the pin means
     mirroring the new build under a new tag first, then changing the tag
     and the three hashes together.
   - A source build stays possible through `--from <dylib>`, which checks
     arm64 and the Metal entry points and reports the result UNPINNED.
   - It is fetched by the macOS CI legs, bundled inside the `.app` next to
     `MesenCore.dylib`, and signed with the bundle.
   - Licensing: librashader's runtime and `librashader-capi` are
     "MPL-2.0 OR GPL-3.0-only" (`librashader-capi/Cargo.toml`; the C headers
     are MIT). The fork redistributes the dylib under MPL-2.0 §3.2: unmodified,
     with the license text and a pointer to the Source Code Form. The mirror
     release carries both; the `.app` loads it dynamically through
     `librashader_ld.h`, as upstream's README prescribes for a host that is
     not itself MPL.
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
- macOS gains a second native library to bundle and sign. Under the
  amended §3, CI does not gain a Rust build; it downloads a binary the fork
  did not build, trusted by pin rather than by reproduction (a rebuild never
  matches the published bytes). The fork now hosts a third-party binary and
  owes its MPL-2.0 obligations on that release. The stale-native-library
  trap applies: the dylib inside the `.app` must be the one just fetched
  (`scripts/release_macos.sh` compares the copy byte for byte).
- The display path is no longer one code path on every platform. A bug that
  appears only on macOS now has to be told apart from a Core bug by
  re-running with `softwareRenderer`.
