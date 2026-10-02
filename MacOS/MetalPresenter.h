#pragma once
//ADR-0237 / PRD slice P.8: the part of the macOS renderer that talks to Metal.
//
//MetalPresenter owns the device, the command queue, the CAMetalLayer and the
//optional librashader Metal filter chain. It knows nothing about Emulator,
//RenderedFrame or the video thread, so scripts/metal_presenter_tests.mm can
//drive it against an offscreen NSView and read the presented drawable back.
//MacOSMetalRenderer (the IRenderingDevice) is a thin adapter over this class.
//
//The header is plain C++ on purpose: Objective-C types live in the pimpl.
#include <cstdint>
#include <string>
#include <vector>

struct MetalShaderParam
{
	std::string Name;
	float Value;
};

//One premultiplied-free BGRA overlay (the emulator HUD or the script HUD),
//alpha-blended over the picture after the shader ran, as the Windows and Linux
//renderers do.
struct MetalOverlay
{
	const uint32_t* Pixels = nullptr;
	uint32_t Width = 0;
	uint32_t Height = 0;
	bool Dirty = true;
};

class MetalPresenter
{
public:
	MetalPresenter();
	~MetalPresenter();

	MetalPresenter(const MetalPresenter&) = delete;
	MetalPresenter& operator=(const MetalPresenter&) = delete;

	//True when this machine has a Metal device at all.
	static bool HasMetalDevice();

	//Attach to a native view (an NSView*): the view is made layer-backed with a
	//CAMetalLayer. This is the P.8 "first risk" - it only works when the handle
	//is an NSView. Returns false (and leaves nothing attached) otherwise.
	bool InitWithView(void* nsView);

	//Size of the presented picture in device pixels (what the UI computes
	//through RendererViewportFit and hands to SetRendererSize).
	void SetOutputSize(uint32_t width, uint32_t height);

	//Presentation pacing: true waits for the display's refresh (vsync).
	void SetVsync(bool enabled);

	//Loads a RetroArch .slangp through the librashader Metal filter chain.
	//Returns false - and keeps presenting unfiltered - when the dylib is
	//missing or the preset does not compile; the reason is in LastError().
	bool SetShader(const std::string& presetPath, const std::vector<MetalShaderParam>& params);
	void UpdateShaderParams(const std::vector<MetalShaderParam>& params);
	void ClearShader();
	bool ShaderActive() const;
	const std::string& LastError() const;

	//Issue #584: true once after Present() dropped the filter chain because a
	//command buffer that ran it completed with an error (a GPU hang, for one);
	//the reason is in LastError(). The caller logs it - the presenter itself
	//has no log - and the picture keeps being presented unfiltered.
	bool TakeShaderDropped();

	//Uploads the frame (BGRA, width*height) and presents it. With a shader
	//active the filter chain runs from the frame into the drawable; without
	//one the frame is scaled into the drawable (nearest, or bilinear when
	//requested). The overlays are blended on top.
	bool Present(const uint32_t* frame, uint32_t width, uint32_t height, uint32_t frameNumber, bool bilinear,
		const MetalOverlay& emuHud, const MetalOverlay& scriptHud);

	//Test hook: when on, Present() waits for the GPU and keeps a CPU copy of
	//the drawable it just drew. Costs a stall per frame - never on in the app.
	void SetReadbackEnabled(bool enabled);
	bool GetLastPresented(std::vector<uint32_t>& pixels, uint32_t& width, uint32_t& height) const;

	//Introspection for the "first risk" check.
	bool LayerIsMetalLayer() const;

	//Test hook: how many HUD textures were written since construction.
	uint64_t OverlayUploadCount() const;

private:
	struct Impl;
	Impl* _impl;
};
