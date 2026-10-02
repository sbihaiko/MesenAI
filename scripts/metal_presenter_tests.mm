//ADR-0237 / PRD slice P.8: framework-free tests for the macOS Metal presenter
//and the renderer-selection policy. macOS only. No emulator, no ROM, no
//window server window: the presenter is attached to an offscreen NSView and
//the drawable it presented is read back (MetalPresenter::SetReadbackEnabled).
//
//What this proves, and what it does not:
//  - PRD stop condition (1): with a shader set the presented frame differs from
//    the unfiltered one (and by the shader's documented amount); with none set
//    it equals a CPU nearest-neighbour scale of the input, which is what the
//    software path shows at that size.
//  - The "first risk": an NSView (what Avalonia's NativeControlHost hands out
//    on macOS) can back a CAMetalLayer, and a handle that is not an NSView is
//    refused. That the *live Avalonia* view is an NSView is NOT proven here;
//    see the P.8 notes in the PRD.
//  - Not proven: anything visible on a real display (stop condition 3).
//
//Run from the repo root with librashader.dylib in the working directory or
//next to this binary (scripts/fetch_librashader_macos.sh puts it in
//UI/Dependencies/). `make metal-presenter-tests` does that.
#import <AppKit/AppKit.h>
#import <QuartzCore/CAMetalLayer.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "Core/Shared/Video/RendererSelection.h"
#include "MacOS/MetalPresenter.h"

static int gCases = 0;
static int gFailures = 0;

#define CHECK(cond, msg) \
	do { \
		gCases++; \
		if(!(cond)) { \
			gFailures++; \
			printf("  FAIL  %s  (%s:%d)\n", msg, __FILE__, __LINE__); \
		} else { \
			printf("  PASS  %s\n", msg); \
		} \
	} while(0)

static const uint32_t kSrcW = 256;
static const uint32_t kSrcH = 240;
static const uint32_t kScale = 4;
static const uint32_t kOutW = kSrcW * kScale;
static const uint32_t kOutH = kSrcH * kScale;

//Every source pixel is distinct enough that a wrong sample shows up.
static std::vector<uint32_t> MakeFrame()
{
	std::vector<uint32_t> px(kSrcW * kSrcH);
	for(uint32_t y = 0; y < kSrcH; y++) {
		for(uint32_t x = 0; x < kSrcW; x++) {
			uint32_t r = 40 + (x % 200);
			uint32_t g = 40 + (y % 200);
			uint32_t b = 60 + ((x * 3 + y * 5) % 180);
			px[y * kSrcW + x] = 0xFF000000u | (r << 16) | (g << 8) | b;
		}
	}
	return px;
}

static std::vector<uint32_t> NearestReference(const std::vector<uint32_t>& src)
{
	std::vector<uint32_t> out(kOutW * kOutH);
	for(uint32_t y = 0; y < kOutH; y++) {
		for(uint32_t x = 0; x < kOutW; x++) {
			out[y * kOutW + x] = src[(y / kScale) * kSrcW + (x / kScale)];
		}
	}
	return out;
}

static bool Present(MetalPresenter& p, const std::vector<uint32_t>& frame, std::vector<uint32_t>& out, const MetalOverlay& hud = MetalOverlay())
{
	MetalOverlay none;
	if(!p.Present(frame.data(), kSrcW, kSrcH, 1, false, hud, none)) {
		return false;
	}
	uint32_t w = 0, h = 0;
	return p.GetLastPresented(out, w, h) && w == kOutW && h == kOutH;
}

static int ChannelDiff(uint32_t a, uint32_t b)
{
	int worst = 0;
	for(int shift = 0; shift <= 16; shift += 8) {
		int d = (int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF);
		if(d < 0) {
			d = -d;
		}
		if(d > worst) {
			worst = d;
		}
	}
	return worst;
}

static void TestSelectionPolicy()
{
	printf("renderer selection\n");
	CHECK(ChooseMacRenderer(false, true) == MacRendererKind::Metal, "default on Apple is the Metal renderer");
	CHECK(ChooseMacRenderer(true, true) == MacRendererKind::Software, "softwareRenderer=true keeps SoftwareRenderer");
	CHECK(ChooseMacRenderer(false, false) == MacRendererKind::Software, "no usable Metal renderer falls back to SoftwareRenderer");
	CHECK(ChooseMacRenderer(true, false) == MacRendererKind::Software, "software + no Metal is software");
}

static void TestFirstRisk()
{
	printf("first risk: a native view can back a CAMetalLayer\n");
	CHECK(MetalPresenter::HasMetalDevice(), "a Metal device exists on this machine");

	NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, kOutW, kOutH)];
	MetalPresenter p;
	CHECK(p.InitWithView((__bridge void*)view), "InitWithView accepts a plain NSView");
	CHECK(p.LayerIsMetalLayer(), "the presenter's layer is a CAMetalLayer");
	CHECK(view.subviews.count == 1 && [view.subviews[0].layer isKindOfClass:[CAMetalLayer class]], "the NSView now hosts a subview backed by a CAMetalLayer");
	CHECK(view.subviews.count == 1 && view.subviews[0].wantsLayer, "that subview is layer-backed");

	NSObject* notAView = [[NSObject alloc] init];
	MetalPresenter bad;
	CHECK(!bad.InitWithView((__bridge void*)notAView), "a handle that is not an NSView is refused");
	CHECK(!bad.InitWithView(nullptr), "a null handle is refused");
}

static void TestUnfilteredMatchesSoftwareScale(MetalPresenter& p, const std::vector<uint32_t>& frame, const std::vector<uint32_t>& ref)
{
	printf("no shader: presented frame equals the software path's pixels at the same size\n");
	std::vector<uint32_t> out;
	CHECK(Present(p, frame, out), "Present() succeeds and the drawable reads back");
	CHECK(!p.ShaderActive(), "no shader is active");
	CHECK(out.size() == ref.size() && memcmp(out.data(), ref.data(), ref.size() * 4) == 0, "drawable is byte-identical to a nearest-neighbour scale of the input");
}

static void TestShader(MetalPresenter& p, const std::vector<uint32_t>& frame, const std::vector<uint32_t>& ref)
{
	printf("shader set: presented frame differs from the unfiltered one\n");
	std::string preset = "tests/fixtures/shaders/scanlines.slangp";
	bool loaded = p.SetShader(preset, {});
	if(!loaded) {
		printf("  note: SetShader failed: %s\n", p.LastError().c_str());
	}
	CHECK(loaded && p.ShaderActive(), "the Metal filter chain loads the fixture preset");

	std::vector<uint32_t> out;
	CHECK(Present(p, frame, out), "Present() succeeds with the filter chain");
	CHECK(out.size() == ref.size() && memcmp(out.data(), ref.data(), ref.size() * 4) != 0, "the filtered frame differs from the unfiltered one");

	//Even output rows are multiplied by (1 - 0.5), odd rows are untouched.
	uint32_t darkOk = 0, darkTotal = 0, litOk = 0, litTotal = 0;
	for(uint32_t y = 8; y < kOutH - 8; y += 7) {
		for(uint32_t x = 8; x < kOutW - 8; x += 11) {
			uint32_t r = ref[y * kOutW + x];
			uint32_t o = out[y * kOutW + x];
			if(y % 2 == 0) {
				uint32_t half = 0xFF000000u | (((r >> 16 & 0xFF) / 2) << 16) | (((r >> 8 & 0xFF) / 2) << 8) | ((r & 0xFF) / 2);
				darkTotal++;
				darkOk += ChannelDiff(o, half) <= 2;
			} else {
				litTotal++;
				litOk += ChannelDiff(o, r) <= 2;
			}
		}
	}
	CHECK(darkTotal > 0 && darkOk == darkTotal, "even rows carry the shader's 50% darkening (within 2/255)");
	CHECK(litTotal > 0 && litOk == litTotal, "odd rows still carry the picture");

	printf("shader parameters reach the filter chain\n");
	p.UpdateShaderParams({ { "darkness", 0.0f } });
	std::vector<uint32_t> zero;
	CHECK(Present(p, frame, zero), "Present() after a parameter change");
	uint32_t same = 0, total = 0;
	for(size_t i = 0; i < zero.size(); i += 97) {
		total++;
		same += ChannelDiff(zero[i], ref[i]) <= 1;
	}
	CHECK(total > 0 && same == total, "darkness=0 makes the shader a no-op (params are applied)");
	p.UpdateShaderParams({ { "darkness", 0.5f } });

	printf("clearing the shader returns to the unfiltered picture\n");
	p.ClearShader();
	std::vector<uint32_t> back;
	CHECK(Present(p, frame, back), "Present() after ClearShader");
	CHECK(!p.ShaderActive() && memcmp(back.data(), ref.data(), ref.size() * 4) == 0, "byte-identical to the unfiltered scale again");
}

static void TestBrokenShaderFallsBack(MetalPresenter& p, const std::vector<uint32_t>& frame, const std::vector<uint32_t>& ref)
{
	printf("a shader that cannot load never blanks the picture\n");
	CHECK(!p.SetShader("tests/fixtures/shaders/does-not-exist.slangp", {}), "a missing preset is rejected");
	CHECK(!p.ShaderActive(), "no shader is active after the failure");
	CHECK(!p.LastError().empty(), "the failure carries a reason");
	std::vector<uint32_t> out;
	CHECK(Present(p, frame, out) && memcmp(out.data(), ref.data(), ref.size() * 4) == 0, "the frame is still presented, unfiltered");
}

static void TestOverlay(MetalPresenter& p, const std::vector<uint32_t>& frame, const std::vector<uint32_t>& ref)
{
	printf("HUD overlay is blended over the picture\n");
	std::vector<uint32_t> hud(kSrcW * kSrcH, 0x00000000u);
	for(uint32_t y = 10; y < 14; y++) {
		for(uint32_t x = 10; x < 14; x++) {
			hud[y * kSrcW + x] = 0xFFFF0000u; //opaque red
		}
	}
	MetalOverlay overlay;
	overlay.Pixels = hud.data();
	overlay.Width = kSrcW;
	overlay.Height = kSrcH;
	std::vector<uint32_t> out;
	CHECK(Present(p, frame, out, overlay), "Present() with an overlay");
	CHECK(out[(10 * kScale + 1) * kOutW + (10 * kScale + 1)] == 0xFFFF0000u, "an opaque overlay pixel replaces the picture");
	CHECK(out[(100 * kScale) * kOutW + (100 * kScale)] == ref[(100 * kScale) * kOutW + (100 * kScale)], "a transparent overlay pixel leaves the picture alone");
}

//The handle the UI passes is NativeControlHost's default child. In Avalonia
//12.1.1 (native/Avalonia.Native/src/OSX/controlhost.mm, CreateDefaultChild)
//that is `[NSView new]` with `setWantsLayer:true`, created with a zero frame and
//only later sized by ShowInBounds (`[_child setFrame:...]`). Reproduce exactly
//that sequence: the Metal host must follow the frame Avalonia gives the view.
static void TestAvaloniaShapedView()
{
	printf("first risk: Avalonia's default NativeControlHost child\n");
	NSView* child = [NSView new];
	[child setWantsLayer:true];
	MetalPresenter p;
	CHECK(p.InitWithView((__bridge void*)child), "InitWithView accepts [NSView new] with wantsLayer, zero frame");
	[child setFrame:NSMakeRect(0, 0, 640, 480)];
	NSView* host = child.subviews.count == 1 ? child.subviews[0] : nil;
	CHECK(host && NSEqualRects(host.frame, child.bounds), "the Metal host follows the frame ShowInBounds sets later");
	CHECK(host && [host hitTest:NSMakePoint(10, 10)] == nil, "the Metal host never takes mouse events from the viewer");
	[child setFrame:NSMakeRect(0, 0, 1280, 720)];
	CHECK(host && NSEqualRects(host.frame, child.bounds), "and keeps following it on a window resize");
}

static void TestShippedLayerConfig()
{
	printf("the layer configuration under test is the shipped one\n");
	NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 64, 64)];
	MetalPresenter p;
	p.InitWithView((__bridge void*)view);
	CAMetalLayer* layer = view.subviews.count == 1 ? (CAMetalLayer*)view.subviews[0].layer : nil;
	CHECK(layer && layer.framebufferOnly == NO, "framebufferOnly is off without the readback hook too");
	p.SetReadbackEnabled(true);
	CHECK(layer && layer.framebufferOnly == NO, "and the readback hook does not change it");
}

static void TestDrawableSizeIsEnforced(MetalPresenter& p, NSView* view, const std::vector<uint32_t>& frame)
{
	printf("the drawable is always the size the UI asked for\n");
	CAMetalLayer* layer = (CAMetalLayer*)view.subviews[0].layer;
	layer.drawableSize = CGSizeMake(100, 50); //what a bounds/scale change can do behind our back
	std::vector<uint32_t> out;
	CHECK(Present(p, frame, out), "a frame after the layer's drawableSize moved still presents at SetOutputSize's size");
}

static void TestOverlayUploadsOnlyWhenDirty(MetalPresenter& p, const std::vector<uint32_t>& frame)
{
	printf("an unchanged HUD is not uploaded again every frame\n");
	std::vector<uint32_t> hud(kSrcW * kSrcH, 0x00000000u);
	hud[20 * kSrcW + 20] = 0xFF00FF00u; //one opaque green pixel
	MetalOverlay overlay;
	overlay.Pixels = hud.data();
	overlay.Width = kSrcW;
	overlay.Height = kSrcH;
	overlay.Dirty = true;

	uint64_t before = p.OverlayUploadCount();
	std::vector<uint32_t> out;
	bool allShown = true;
	for(int i = 0; i < 8; i++) {
		allShown &= Present(p, frame, out, overlay) && out[(20 * kScale + 1) * kOutW + (20 * kScale + 1)] == 0xFF00FF00u;
		overlay.Dirty = false;
	}
	uint64_t uploads = p.OverlayUploadCount() - before;
	printf("  note: 8 frames, 1 dirty, %llu overlay uploads\n", (unsigned long long)uploads);
	CHECK(allShown, "the HUD shows on every one of 8 frames (each in-flight slot carries it)");
	CHECK(uploads <= 3, "it is uploaded at most once per in-flight slot, not once per frame");

	hud[20 * kSrcW + 20] = 0xFF0000FFu; //now blue, and flagged dirty
	overlay.Dirty = true;
	CHECK(Present(p, frame, out, overlay) && out[(20 * kScale + 1) * kOutW + (20 * kScale + 1)] == 0xFF0000FFu, "a dirty HUD is uploaded on the frame it changes");
}

int main()
{
	@autoreleasepool {
		[NSApplication sharedApplication];

		TestSelectionPolicy();
		TestFirstRisk();
		TestAvaloniaShapedView();
		TestShippedLayerConfig();

		NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, kOutW, kOutH)];
		MetalPresenter p;
		if(!p.InitWithView((__bridge void*)view)) {
			printf("  FAIL  cannot continue: InitWithView failed (%s)\n", p.LastError().c_str());
			return 1;
		}
		p.SetOutputSize(kOutW, kOutH);
		p.SetReadbackEnabled(true);

		std::vector<uint32_t> frame = MakeFrame();
		std::vector<uint32_t> ref = NearestReference(frame);

		TestUnfilteredMatchesSoftwareScale(p, frame, ref);
		TestShader(p, frame, ref);
		TestBrokenShaderFallsBack(p, frame, ref);
		TestOverlay(p, frame, ref);
		TestOverlayUploadsOnlyWhenDirty(p, frame);
		TestDrawableSizeIsEnforced(p, view, frame);
	}

	printf("\n%d/%d cases passed\n", gCases - gFailures, gCases);
	return gFailures == 0 ? 0 : 1;
}
