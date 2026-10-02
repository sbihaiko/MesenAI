#pragma once

//ADR-0237 / PRD slice P.8: which renderer InitRenderer() builds on macOS.
//Header-only and free of Metal/AppKit types so the policy is unit-testable on
//any host (scripts/metal_presenter_tests.mm drives it).
//
//Rules, in order:
//  1. The user (or a headless host that asks for it) requested the software
//     renderer: SoftwareRenderer, always. Headless runs never reach this
//     function - they pass no viewer handle, so no rendering device is built.
//  2. Otherwise the native Metal renderer, but only when it could be brought
//     up on the viewer's native view; when it could not (no GPU, a handle that
//     cannot back a CAMetalLayer) the frame still reaches the screen through
//     SoftwareRenderer rather than leaving the window black.
enum class MacRendererKind
{
	Software,
	Metal
};

inline MacRendererKind ChooseMacRenderer(bool softwareRendererRequested, bool metalRendererAvailable)
{
	if(softwareRendererRequested || !metalRendererAvailable) {
		return MacRendererKind::Software;
	}
	return MacRendererKind::Metal;
}
