#include "MacOS/MacOSMetalRenderer.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/MessageManager.h"
#include "Core/Shared/RenderedFrame.h"
#include "Core/Shared/Video/VideoRenderer.h"
#include "Core/Shared/Video/ShaderPresetApply.h"

MacOSMetalRenderer::MacOSMetalRenderer(Emulator* emu) : _emu(emu)
{
}

IRenderingDevice* MacOSMetalRenderer::Create(Emulator* emu, void* viewerHandle)
{
	std::unique_ptr<MacOSMetalRenderer> renderer(new MacOSMetalRenderer(emu));
	if(!renderer->Init(viewerHandle)) {
		return nullptr;
	}
	emu->GetVideoRenderer()->RegisterRenderingDevice(renderer.get());
	return renderer.release();
}

bool MacOSMetalRenderer::Init(void* viewerHandle)
{
	if(!_presenter.InitWithView(viewerHandle)) {
		MessageManager::Log("[Video] Metal renderer unavailable: " + _presenter.LastError());
		return false;
	}
	//The line a person checks in the log to tell the Metal path from the
	//software fallback (ADR-0237).
	MessageManager::Log("[Video] Metal renderer active");
	return true;
}

MacOSMetalRenderer::~MacOSMetalRenderer()
{
	_emu->GetVideoRenderer()->UnregisterRenderingDevice(this);
}

void MacOSMetalRenderer::SetFullscreenMode(FullscreenSettings settings)
{
	//Fullscreen is the window's business on macOS; the layer just follows the view.
}

void MacOSMetalRenderer::Reset()
{
	//Nothing to rebuild: the layer follows the view, and the shader is re-read
	//from EmuSettings whenever its config version moves (UpdateShader).
}

void MacOSMetalRenderer::ClearFrame()
{
	auto lock = _frameLock.AcquireSafe();
	std::fill(_frame.begin(), _frame.end(), 0);
}

void MacOSMetalRenderer::UpdateFrame(RenderedFrame& frame)
{
	auto lock = _frameLock.AcquireSafe();
	_frameNumber = frame.FrameNumber;
	if(_frameWidth != frame.Width || _frameHeight != frame.Height) {
		_frameWidth = frame.Width;
		_frameHeight = frame.Height;
		_frame.assign((size_t)_frameWidth * _frameHeight, 0);
	}
	memcpy(_frame.data(), frame.FrameBuffer, (size_t)frame.Width * frame.Height * sizeof(uint32_t));
}

void MacOSMetalRenderer::UpdateShader()
{
	if(!_emu->GetSettings()->NeedsShaderUpdate(_shaderCfg.ConfigVersion)) {
		return;
	}

	ShaderConfig cfg = _emu->GetSettings()->GetShaderConfig();
	std::vector<MetalShaderParam> params;
	for(const ShaderParam& p : cfg.Params) {
		params.push_back({ p.Name, (float)p.Value });
	}

	ApplyShaderPreset(_presenter, _shaderCfg.ShaderFile, cfg.ShaderFile, params);
	_shaderCfg = cfg;
}

void MacOSMetalRenderer::Render(RenderSurfaceInfo& emuHud, RenderSurfaceInfo& scriptHud)
{
	VideoConfig cfg = _emu->GetSettings()->GetVideoConfig();
	FrameInfo size = _emu->GetVideoRenderer()->GetRendererSize();
	if(size.Width == 0 || size.Height == 0) {
		return;
	}

	if(size.Width != _outW || size.Height != _outH) {
		_outW = size.Width;
		_outH = size.Height;
		_presenter.SetOutputSize(_outW, _outH);
	}
	if(cfg.VerticalSync != _vsync) {
		_vsync = cfg.VerticalSync;
		_presenter.SetVsync(_vsync);
	}
	UpdateShader();
	_presenter.SetShaderBypass(_emu->GetSettings()->IsLookCompare());

	MetalOverlay emu = { emuHud.Buffer, emuHud.Width, emuHud.Height, emuHud.IsDirty };
	MetalOverlay script = { scriptHud.Buffer, scriptHud.Width, scriptHud.Height, scriptHud.IsDirty };

	//Copy out under the lock, present without it: Present() can wait on the
	//GPU (in-flight slots) and, with vsync, on the display for a drawable, and
	//the emulation thread must never wait behind that in UpdateFrame().
	uint32_t width, height, frameNumber;
	{
		auto lock = _frameLock.AcquireSafe();
		if(_frame.empty()) {
			return;
		}
		_presented = _frame;
		width = _frameWidth;
		height = _frameHeight;
		frameNumber = _frameNumber;
	}
	_presenter.Present(_presented.data(), width, height, frameNumber, cfg.UseBilinearInterpolation, emu, script);

	//Issue #584: a preset that hangs the GPU is dropped by the presenter. It is
	//not reloaded until the configured shader changes (_shaderCfg keeps the
	//file), so it cannot hang the GPU again on the next frame. Issue #593: a
	//frame the chain failed on is reported once per failure episode.
	ReportShaderFrameProblems(_presenter, _shaderCfg.ShaderFile);
}
