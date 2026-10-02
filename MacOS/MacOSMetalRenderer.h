#pragma once
#include "Core/Shared/Interfaces/IRenderingDevice.h"
#include "Core/Shared/SettingTypes.h"
#include "Utilities/SimpleLock.h"
#include "MacOS/MetalPresenter.h"

#include <memory>
#include <vector>

class Emulator;

//ADR-0237 / PRD slice P.8: the macOS IRenderingDevice. A thin adapter between
//the video thread (UpdateFrame / Render) and MetalPresenter, which owns every
//Metal object. It follows LinuxOglRenderer: UpdateFrame copies the finished
//frame under a lock, Render presents it, and the shader preset is re-read from
//EmuSettings whenever its config version moves.
class MacOSMetalRenderer : public IRenderingDevice
{
private:
	Emulator* _emu = nullptr;
	MetalPresenter _presenter;

	SimpleLock _frameLock;
	std::vector<uint32_t> _frame;
	uint32_t _frameWidth = 0;
	uint32_t _frameHeight = 0;
	uint32_t _frameNumber = 0;
	std::vector<uint32_t> _presented; //render thread only

	ShaderConfig _shaderCfg = {};
	bool _vsync = false;
	uint32_t _outW = 0;
	uint32_t _outH = 0;

	explicit MacOSMetalRenderer(Emulator* emu);
	bool Init(void* viewerHandle);
	void UpdateShader();

public:
	//Returns nullptr - and registers nothing - when the Metal renderer cannot
	//be brought up on this handle; the caller then builds a SoftwareRenderer.
	static IRenderingDevice* Create(Emulator* emu, void* viewerHandle);
	~MacOSMetalRenderer() override;

	void ClearFrame() override;
	void UpdateFrame(RenderedFrame& frame) override;
	void Render(RenderSurfaceInfo& emuHud, RenderSurfaceInfo& scriptHud) override;
	void Reset() override;
	void SetFullscreenMode(FullscreenSettings settings) override;
};
