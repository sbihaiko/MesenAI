#include "pch.h"
#include "Shared/Video/VideoRenderer.h"
#include "Shared/Video/AspectRatioMath.h"
#include "Shared/Video/VideoDecoder.h"
#include "Shared/Interfaces/IRenderingDevice.h"
#include "Shared/Emulator.h"
#include "Shared/EmuSettings.h"
#include "Shared/Video/DebugHud.h"
#include "Shared/Video/SystemHud.h"
#include "Shared/Video/DebugStats.h"
#include "Shared/InputHud.h"
#include "Shared/MessageManager.h"
#include "Shared/NotificationManager.h"
#include "Shared/Interfaces/INotificationListener.h"
#include "Shared/EnhancementPacks/MepPackManager.h"
#include "Shared/EnhancementPacks/MepWidescreen.h"
#include "Shared/Video/FrameCapture.h"
#include "Shared/Video/WidescreenFallback.h"
#include "Shared/Video/WidescreenFrameFlow.h"
#include "Utilities/Video/IVideoRecorder.h"
#include "Utilities/Video/AviRecorder.h"
#include "Utilities/Video/GifRecorder.h"
#include "Utilities/PNGHelper.h"
#include "Utilities/JsonReader.h"
#include "Shared/Video/BorderLayout.h"
#include "Utilities/FolderUtilities.h"

namespace
{
	//ADR-0149: flips VideoRenderer::_borderDirty when the active enhancement
	//pack may have changed. Held as a shared_ptr by the renderer (the
	//NotificationManager keeps only a weak_ptr) and touches nothing else, so
	//it never runs renderer code on the notifying thread.
	class BorderInvalidator final : public INotificationListener
	{
	public:
		explicit BorderInvalidator(std::atomic<bool>& dirty) : _dirty(dirty) {}

		void ProcessNotification(ConsoleNotificationType type, void* parameter) override
		{
			switch(type) {
				case ConsoleNotificationType::GameLoaded:
				case ConsoleNotificationType::BeforeGameUnload:
				case ConsoleNotificationType::EmulationStopped:
					_dirty.store(true, std::memory_order_release);
					break;
				default:
					break;
			}
		}

	private:
		std::atomic<bool>& _dirty;
	};
}

VideoRenderer::VideoRenderer(Emulator* emu)
{
	_emu = emu;
	_stopFlag = false;

	_rendererHud.reset(new DebugHud());
	_systemHud.reset(new SystemHud(_emu));
	_inputHud.reset(new InputHud(emu, _rendererHud.get()));

	//Emulator constructs its NotificationManager before this object (member
	//order), so the listener can be registered right away.
	_borderListener.reset(new BorderInvalidator(_borderDirty));
	if(NotificationManager* notifications = _emu->GetNotificationManager()) {
		notifications->RegisterNotificationListener(_borderListener);
	}
}

VideoRenderer::~VideoRenderer()
{
	_stopFlag = true;
	StopThread();
}

FrameInfo VideoRenderer::GetRendererSize()
{
	FrameInfo frame = {};
	frame.Width = _rendererWidth;
	frame.Height = _rendererHeight;
	return frame;
}

void VideoRenderer::SetRendererSize(uint32_t width, uint32_t height)
{
	_rendererWidth = width;
	_rendererHeight = height;
}

void VideoRenderer::StartThread()
{
	if(!_renderThread) {
		auto lock = _stopStartLock.AcquireSafe();
		if(!_renderThread) {
			_stopFlag = false;
			_waitForRender.Reset();

			_renderThread.reset(new std::thread(&VideoRenderer::RenderThread, this));
		}
	}
}

void VideoRenderer::StopThread()
{
	_stopFlag = true;
	if(_renderThread) {
		auto lock = _stopStartLock.AcquireSafe();
		if(_renderThread) {
			_renderThread->join();
			_renderThread.reset();
		}
	}
}

void VideoRenderer::RenderThread()
{
	if(_renderer) {
		_renderer->OnRendererThreadStarted();
	}

	Timer lastFrameTimer;
	bool needClearHud = false;
	while(!_stopFlag.load()) {
		//Wait until a frame is ready, or until 32ms have passed (to allow HUD to update at ~30fps when paused)
		bool forceRender = !_waitForRender.Wait(32);
		if(_renderer) {
			FrameInfo size = _emu->GetVideoDecoder()->GetBaseFrameInfo(true);
			_scriptHudSurface.UpdateSize(size.Width * _scriptHudScale, size.Height * _scriptHudScale);

			size = GetEmuHudSize(size);
			if(_emuHudSurface.UpdateSize(size.Width, size.Height)) {
				_rendererHud->ClearScreen();
			}

			RenderedFrame frame;
			{
				auto lock = _frameLock.AcquireSafe();
				frame = _lastFrame;
			}

			if(needClearHud) {
				_emuHudSurface.Clear();
				_rendererHud->ClearScreen();
			}

			_inputHud->DrawControllers(size, frame.InputData);

			{
				auto lock = _hudLock.AcquireSafe();
				_systemHud->Draw(_rendererHud.get(), size.Width, size.Height);
			}

			bool showDebugInfo = _emu->GetSettings()->GetPreferences().ShowDebugInfo;
			if(showDebugInfo) {
				double lastFrameTime = lastFrameTimer.GetElapsedMS();
				lastFrameTimer.Reset();
				_emu->GetDebugStats()->UpdateStats(_emu, true, lastFrameTime);
				_emu->GetDebugStats()->DisplayStats(_emu, _rendererHud.get());
				needClearHud = true;
			}

			_emuHudSurface.IsDirty = _rendererHud->Draw(_emuHudSurface.Buffer, size, {}, 0, {}, !needClearHud);
			_scriptHudSurface.IsDirty = DrawScriptHud(frame);

			needClearHud = showDebugInfo;

			if(forceRender || _needRedraw || _emuHudSurface.IsDirty || _scriptHudSurface.IsDirty) {
				_needRedraw = false;
				_renderer->Render(_emuHudSurface, _scriptHudSurface);
			}
		}
	}

	if(_renderer) {
		_renderer->OnRendererThreadStopped();
	}
}

FrameInfo VideoRenderer::GetEmuHudSize(FrameInfo baseFrameSize)
{
	FrameInfo size = {};
	if(_emu->GetSettings()->GetPreferences().HudSize == HudDisplaySize::Scaled) {
		//Adjust the system HUD's width to match the aspect ratio to allow text to be unstretched
		//(The Lua HUD is not adjusted to allow scripts that need to match positions on the game screen to work correctly.)
		double aspectRatio = _emu->GetSettings()->GetAspectRatio(_emu->GetRegion(), baseFrameSize);
		AspectRatioMath::Size stretched = AspectRatioMath::ComputeStretchedSize(baseFrameSize.Height, aspectRatio);
		size.Width = stretched.Width;
		size.Height = stretched.Height;
	} else {
		size.Width = _rendererWidth / 2;
		size.Height = _rendererHeight / 2;
	}
	return size;
}

bool VideoRenderer::DrawScriptHud(RenderedFrame& frame)
{
	bool needRedraw = false;
	if(_lastScriptHudFrameNumber != frame.FrameNumber) {
		//Clear+draw HUD for scripts
		//-Only when frame number changes (to prevent the HUD from disappearing when paused, etc.)
		//-Only when commands are queued, otherwise skip drawing/clearing to avoid wasting CPU time
		if(_needScriptHudClear) {
			_scriptHudSurface.Clear();
			_needScriptHudClear = false;
			needRedraw = true;
		}

		if(_emu->GetScriptHud()->HasCommands()) {
			auto [size, overscan] = GetScriptHudSize();
			_emu->GetScriptHud()->Draw(_scriptHudSurface.Buffer, size, overscan, frame.FrameNumber, {});
			_needScriptHudClear = true;
			_lastScriptHudFrameNumber = frame.FrameNumber;
			needRedraw = true;
		}
	}
	return needRedraw;
}

std::pair<FrameInfo, OverscanDimensions> VideoRenderer::GetScriptHudSize()
{
	FrameInfo scriptHudSize = { _scriptHudSurface.Width, _scriptHudSurface.Height };
	OverscanDimensions overscan = _emu->GetSettings()->GetOverscan();
	overscan.Top *= _scriptHudScale;
	overscan.Bottom *= _scriptHudScale;
	overscan.Left *= _scriptHudScale;
	overscan.Right *= _scriptHudScale;
	return { scriptHudSize, overscan };
}

void VideoRenderer::ResetBorderAsset()
{
	_borderAvailable = false;
	_borderPixels.clear();
	_borderBackdrop.clear();
	_borderLayout = BorderLayout();
	_widescreenPackFolder.clear();
	_widescreenLeft.clear();
	_widescreenRight.clear();
	_widescreenLeftSize = {};
	_widescreenRightSize = {};
}

//Decode thread only. Runs when _borderDirty is set (game load/unload/stop),
//so the MepPackManager section lookup and the file I/O happen once per pack
//change instead of once per frame (ADR-0149 §2 "decoded once at pack load").
//The pack's widescreen side art (ADR-0253 §3) is decoded on the same pass.
void VideoRenderer::UpdatePackArtAssets()
{
	if(!_borderDirty.exchange(false, std::memory_order_acq_rel)) {
		return;
	}

	MepPackManager* mgr = _emu->GetEnhancementPackManager();
	string borderFolder = mgr ? mgr->GetSectionPath(MepSectionType::Border) : "";
	if(borderFolder.empty() && mgr) {
		borderFolder = mgr->GetSectionAutoPath(MepSectionType::Border);
	}
	string widescreenFolder = mgr ? mgr->GetSectionPath(MepSectionType::Widescreen) : "";
	if(widescreenFolder.empty() && mgr) {
		widescreenFolder = mgr->GetSectionAutoPath(MepSectionType::Widescreen);
	}
	bool sameBorder = borderFolder == _borderPackFolder && (_borderAvailable || borderFolder.empty());
	bool sameWidescreen = widescreenFolder == _widescreenPackFolder && (!_widescreenLeft.empty() || !_widescreenRight.empty() || widescreenFolder.empty());
	if(sameBorder && sameWidescreen) {
		//Same pack as before - the cached surfaces are still the right ones
		return;
	}

	ResetBorderAsset();
	_borderPackFolder = borderFolder;
	_widescreenPackFolder = widescreenFolder;
	//The pack's `<widescreen>` art (ADR-0253 §3, MEP-v1 §5.5): the section
	//manifest names the images, and each is decoded once. The screen id that
	//picks a per-screen override belongs to the HD pack path (W.4); until the
	//default pair covers every frame.
	if(!widescreenFolder.empty()) {
		LoadWidescreenArt(widescreenFolder);
	}
	if(borderFolder.empty()) {
		return;
	}

	string pngPath = FolderUtilities::CombinePath(borderFolder, "border.png");
	ifstream pngFile(pngPath, ios::in | ios::binary);
	if(!pngFile) {
		return;
	}
	vector<uint8_t> fileData((std::istreambuf_iterator<char>(pngFile)), std::istreambuf_iterator<char>());
	uint32_t w = 0, h = 0;
	if(!PNGHelper::ReadPNG(std::move(fileData), _borderPixels, w, h) || w == 0 || h == 0) {
		_borderPixels.clear();
		return;
	}
	//A border is a screen-sized bezel; refuse anything that would make the
	//composite buffer (and every downstream copy) absurdly large. Same pixel
	//budget as the capture path (FrameCapture.h).
	if(w > 8192 || h > 8192 || (uint64_t)w * h > FrameCaptureMath::MaxCapturePixels) {
		MessageManager::Log("[MEP] border: border.png is " + std::to_string(w) + "x" + std::to_string(h) + " - too large, border skipped (" + borderFolder + ")");
		_borderPixels.clear();
		return;
	}
	if(_borderPixels.size() < (size_t)w * h) {
		_borderPixels.clear();
		return;
	}

	//Layout math lives in BorderLayout (host-free, unit-tested); this method
	//only reads border.json into it (ADR-0149 §1)
	BorderLayout layout;
	layout.CanvasWidth = w;
	layout.CanvasHeight = h;

	string jsonPath = FolderUtilities::CombinePath(borderFolder, "border.json");
	ifstream jsonFile(jsonPath, ios::in | ios::binary);
	if(jsonFile) {
		string jsonText((std::istreambuf_iterator<char>(jsonFile)), std::istreambuf_iterator<char>());
		JsonReader reader;
		JsonValue root;
		if(reader.Parse(jsonText, root) && root.IsObject()) {
			const JsonValue* vp = root.Get("viewport");
			if(vp && vp->IsObject()) {
				const JsonValue* vx = vp->Get("x");
				const JsonValue* vy = vp->Get("y");
				const JsonValue* vw = vp->Get("width");
				const JsonValue* vh = vp->Get("height");
				if(vx && vx->IsNumber()) layout.ViewportX = (int32_t)vx->GetNumber();
				if(vy && vy->IsNumber()) layout.ViewportY = (int32_t)vy->GetNumber();
				if(vw && vw->IsNumber()) layout.ViewportWidth = (uint32_t)vw->GetNumber();
				if(vh && vh->IsNumber()) layout.ViewportHeight = (uint32_t)vh->GetNumber();
			}
			const JsonValue* u = root.Get("underlay");
			if(u && u->IsBool()) {
				layout.Underlay = u->GetBool();
			}
			const JsonValue* sm = root.Get("scale_mode");
			if(sm && sm->IsString()) {
				BorderLayout::ParseScaleMode(sm->GetString(), layout.ScaleMode);
			}
		}
	}

	//Fallback if viewport was absent or invalid: 4:3 centred inside canvas
	layout.ApplyDefaultViewportIfMissing();
	_borderLayout = layout;
	BorderPrepareBackdrop(_borderBackdrop, _borderPixels.data(), _borderLayout);
	_borderAvailable = true;
}

//ADR-0253 §3: decodes the `<widescreen>` section's side art. Sizes are NOT
//checked here: the console decides how many extra columns it has, and
//WidescreenFallback::FillSideFromArt refuses an image whose size is not exactly
//that run - a wrong-sized image falls to the next source in the chain instead
//of being stretched or tiled across the side.
void VideoRenderer::LoadWidescreenArt(const string& folder)
{
	MepWidescreen art;
	string text;
	{
		ifstream file(FolderUtilities::CombinePath(folder, "widescreen.json"), ios::in | ios::binary);
		if(!file) {
			return;
		}
		text.assign((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
	}
	string error;
	if(!MepWidescreen::Parse(text, art, error)) {
		MessageManager::Log("[MEP] widescreen: " + error + " (" + folder + ")");
		return;
	}

	auto loadSide = [&](bool left, vector<uint32_t>& into, FrameInfo& size) {
		string relative = art.GetSidePath(MepWidescreen::DefaultScreenId, left);
		if(relative.empty()) {
			return;
		}
		string path = FolderUtilities::CombinePath(folder, relative);
		ifstream file(path, ios::in | ios::binary);
		if(!file) {
			MessageManager::Log("[MEP] widescreen: " + relative + " is missing (" + folder + ")");
			return;
		}
		vector<uint8_t> fileData((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
		uint32_t w = 0, h = 0;
		vector<uint32_t> pixels;
		if(!PNGHelper::ReadPNG(std::move(fileData), pixels, w, h) || w == 0 || h == 0 || pixels.size() < (size_t)w * h) {
			MessageManager::Log("[MEP] widescreen: " + relative + " could not be decoded (" + folder + ")");
			return;
		}
		if(w > 8192 || h > 8192 || (uint64_t)w * h > FrameCaptureMath::MaxCapturePixels) {
			MessageManager::Log("[MEP] widescreen: " + relative + " is " + std::to_string(w) + "x" + std::to_string(h) + " - too large, skipped");
			return;
		}
		into = std::move(pixels);
		size = { w, h };
	};
	loadSide(true, _widescreenLeft, _widescreenLeftSize);
	loadSide(false, _widescreenRight, _widescreenRightSize);
	if(!_widescreenLeft.empty() || !_widescreenRight.empty()) {
		MessageManager::Log("[MEP] widescreen: side art loaded from '" + folder + "' (" +
			std::to_string(_widescreenLeftSize.Width) + "x" + std::to_string(_widescreenLeftSize.Height) + " left, " +
			std::to_string(_widescreenRightSize.Width) + "x" + std::to_string(_widescreenRightSize.Height) + " right)");
	}
}

//ADR-0253 §3, first link of the chain: the pack's `<widescreen>` art. Only the
//side columns the console's Reveal left unfilled are touched, and every row it
//draws is marked filled so the border composite below leaves it alone.
void VideoRenderer::ApplyWidescreenFallback(RenderedFrame& frame)
{
	if(frame.ExtendedColumns == 0 || !frame.FrameBuffer || !frame.ExtendedSideFill) {
		return;
	}
	UpdatePackArtAssets();
	if(_widescreenLeft.empty() && _widescreenRight.empty()) {
		return;
	}
	if(_sideFillScratch.size() < frame.Height) {
		_sideFillScratch.resize(frame.Height);
	}
	memcpy(_sideFillScratch.data(), frame.ExtendedSideFill, frame.Height);

	uint32_t* pixels = (uint32_t*)frame.FrameBuffer;
	if(!_widescreenLeft.empty()) {
		WidescreenFallback::FillSideFromArt(pixels, frame.Width, frame.Height, frame.ExtendedColumns, true,
			_widescreenLeft.data(), _widescreenLeftSize.Width, _widescreenLeftSize.Height, _sideFillScratch.data());
	}
	if(!_widescreenRight.empty()) {
		WidescreenFallback::FillSideFromArt(pixels, frame.Width, frame.Height, frame.ExtendedColumns, false,
			_widescreenRight.data(), _widescreenRightSize.Width, _widescreenRightSize.Height, _sideFillScratch.data());
	}
	frame.ExtendedSideFill = _sideFillScratch.data();
}

bool VideoRenderer::IsBorderComposited()
{
	return _emu->GetSettings()->GetEnhancementPackConfig().EnableBorder && _borderAvailable;
}

RenderedFrame* VideoRenderer::CompositeBorder(RenderedFrame& inFrame)
{
	if(!_emu->GetSettings()->GetEnhancementPackConfig().EnableBorder) {
		return &inFrame;
	}

	UpdatePackArtAssets();
	if(!_borderAvailable || _borderLayout.CanvasWidth == 0 || _borderLayout.CanvasHeight == 0 || _borderPixels.empty() || !inFrame.FrameBuffer) {
		return &inFrame;
	}

	size_t totalPixels = (size_t)_borderLayout.CanvasWidth * _borderLayout.CanvasHeight;
	if(_compositeBuffer.size() != totalPixels) {
		_compositeBuffer.resize(totalPixels);
	}

	uint32_t* dst = _compositeBuffer.data();
	if(inFrame.ExtendedColumns > 0 && inFrame.ExtendedSideFill) {
		//ADR-0253 W.3: the sides the game/art did not fill keep the border art,
		//so the border is the chain's second link rather than a reason to drop
		//the extended frame back to its centre.
		BorderCompositeExtendedFrame(dst, _borderBackdrop.data(), _borderPixels.data(), _borderLayout, (const uint32_t*)inFrame.FrameBuffer,
			inFrame.Width, inFrame.Height, inFrame.ExtendedColumns, inFrame.ExtendedSideFill);
	} else {
		BorderCompositePrepared(dst, _borderBackdrop.data(), _borderPixels.data(), _borderLayout, (const uint32_t*)inFrame.FrameBuffer, inFrame.Width, inFrame.Height, _borderSxLut);
	}

	_compositedFrame = inFrame;
	_compositedFrame.FrameBuffer = (void*)dst;
	_compositedFrame.Width = _borderLayout.CanvasWidth;
	_compositedFrame.Height = _borderLayout.CanvasHeight;
	//The canvas is a finished picture, not an extended frame: its sides are
	//already composited in, so a reader that keyed on the extra columns would
	//compute a standard centre out of the border's own width. The decoder's
	//own frame keeps them - that is what the aspect ratio reads
	//(EmuSettings::GetAspectRatio's ExtendedFrame) - so only this copy drops them.
	_compositedFrame.ClearExtension();
	return &_compositedFrame;
}

void VideoRenderer::UpdateFrame(RenderedFrame& frame)
{
	{
		auto lock = _hudLock.AcquireSafe();
		_systemHud->UpdateHud();
	}

	ProcessAviRecording(frame);

	//ADR-0253 §3: the chain is the pack's widescreen art (the first link after
	//Reveal) and then the border composite, which fills whatever the art left
	//empty. The order, and why inverting it would lose the art, is
	//WidescreenFallback::ApplyChain's contract - the unit tests pin it there.
	RenderedFrame* effectiveFrame = WidescreenFallback::ApplyChain(
		frame.ExtendedColumns > 0 && frame.FrameBuffer != nullptr && frame.ExtendedSideFill != nullptr,
		[&]() { ApplyWidescreenFallback(frame); },
		[&]() { return CompositeBorder(frame); });

	{
		auto lock = _frameLock.AcquireSafe();
		_lastFrame = *effectiveFrame;
	}

	if(_renderer) {
		_renderer->UpdateFrame(*effectiveFrame);
		_needRedraw = true;
		_waitForRender.Signal();
	}
}

void VideoRenderer::ClearFrame()
{
	if(_renderer) {
		_renderer->ClearFrame();
	}
}

void VideoRenderer::RegisterRenderingDevice(IRenderingDevice* renderer)
{
	_renderer = renderer;
	StartThread();
}

void VideoRenderer::UnregisterRenderingDevice(IRenderingDevice* renderer)
{
	if(_renderer == renderer) {
		StopThread();
		_renderer = nullptr;
	}
}

void VideoRenderer::CaptureSystemHud(uint32_t width, uint32_t height, vector<uint32_t>& out)
{
	//Fully transparent, not the previous call's leftovers - unlike the render
	//thread's _rendererHud/_emuHudSurface pair, there is no earlier frame this
	//buffer is diffed against, so every pixel a toast does not touch must read
	//as blank rather than as whatever a stale allocation happened to hold.
	out.assign((size_t)width * height, 0);

	//Same two-step pattern as ProcessAviRecording's HUD overlay: a local,
	//one-shot DebugHud collects the system HUD's draw commands, then
	//rasterises them directly onto the caller's buffer. No render thread, no
	//IRenderingDevice, so this runs whether or not one exists - _hudLock is
	//still required, because _systemHud's message queue is shared with
	//whichever thread does have a render loop running (UpdateFrame's
	//UpdateHud() call, RenderThread's Draw() call).
	DebugHud hud;
	{
		auto lock = _hudLock.AcquireSafe();
		_systemHud->Draw(&hud, width, height);
	}
	FrameInfo frameSize = { width, height };
	hud.Draw(out.data(), frameSize, {}, 0, {});
}

void VideoRenderer::ProcessAviRecording(RenderedFrame& frame)
{
	shared_ptr<IVideoRecorder> recorder = _recorder.lock();
	if(recorder) {
		if(!recorder->IsRecording()) {
			recorder->StartRecording(frame.Width, frame.Height, 4, _emu->GetSettings()->GetAudioConfig().SampleRate, _emu->GetFps());
		}

		if(_recorderOptions.RecordInputHud || _recorderOptions.RecordSystemHud) {
			//Calculate the scale needed for the HUD elements
			FrameInfo originalSize = _emu->GetVideoDecoder()->GetBaseFrameInfo(true);
			double scale = (double)frame.Height / originalSize.Height;
			//ADR-0253 W.6: the canvas the HUD is laid out on is the frame's own,
			//so a Reveal recording gets a Reveal-sized one - 448x240 over an
			//896x480 frame, against the 301x240 a standard 602x480 recording
			//gets. Laying the Reveal HUD out on the standard canvas is what
			//would crowd it or clip it.
			WidescreenFrameFlow::HudCanvas hudCanvas = WidescreenFrameFlow::RecorderHudCanvas(frame.Width, frame.Height, originalSize.Height);
			FrameInfo scaledFrameSize = { hudCanvas.Width, hudCanvas.Height };

			//Update the surface to match the frame's size
			_aviRecorderSurface.UpdateSize(frame.Width, frame.Height);

			//Copy the game screen
			memcpy(_aviRecorderSurface.Buffer, frame.FrameBuffer, frame.Width * frame.Height * sizeof(uint32_t));

			//Draw the system/input HUDs
			DebugHud hud;
			InputHud inputHud(_emu, &hud);
			if(_recorderOptions.RecordSystemHud) {
				_systemHud->Draw(&hud, scaledFrameSize.Width, scaledFrameSize.Height);
			}
			if(_recorderOptions.RecordInputHud) {
				inputHud.DrawControllers(scaledFrameSize, frame.InputData);
			}

			FrameInfo frameSize = { frame.Width, frame.Height };
			hud.Draw((uint32_t*)_aviRecorderSurface.Buffer, frameSize, {}, frame.FrameNumber, { scale, scale });

			//Record the final result
			if(!recorder->AddFrame(_aviRecorderSurface.Buffer, frame.Width, frame.Height, _emu->GetFps())) {
				StopRecording();
			}
		} else {
			//Only record the game screen
			if(!recorder->AddFrame(frame.FrameBuffer, frame.Width, frame.Height, _emu->GetFps())) {
				StopRecording();
			}
		}
	}
}

void VideoRenderer::StartRecording(string filename, RecordAviOptions options)
{
	_recorderOptions = options;

	shared_ptr<IVideoRecorder> recorder;
	if(options.Codec == VideoCodec::GIF) {
		recorder.reset(new GifRecorder());
	} else {
		recorder.reset(new AviRecorder(options.Codec, options.CompressionLevel));
	}

	if(recorder->Init(filename)) {
		_recorder.reset(recorder);

		if(!options.RecordSystemHud) {
			//Only display message if not recording the system HUD (otherwise the message is always visible on the recording, which isn't ideal)
			MessageManager::DisplayMessage("VideoRecorder", "VideoRecorderStarted", filename);
		}
	} else {
		MessageManager::DisplayMessage("VideoRecorder", "CouldNotWriteToFile", filename);
	}
}

void VideoRenderer::AddRecordingSound(int16_t* soundBuffer, uint32_t sampleCount, uint32_t sampleRate)
{
	shared_ptr<IVideoRecorder> recorder = _recorder.lock();
	if(recorder) {
		if(!recorder->AddSound(soundBuffer, sampleCount, sampleRate)) {
			StopRecording();
		}
	}
}

void VideoRenderer::StopRecording()
{
	shared_ptr<IVideoRecorder> recorder = _recorder.lock();
	if(recorder) {
		MessageManager::DisplayMessage("VideoRecorder", "VideoRecorderStopped", recorder->GetOutputFile());
	}
	_aviRecorderSurface.UpdateSize(0, 0);
	_recorder.reset();
}

bool VideoRenderer::IsRecording()
{
	return _recorder != nullptr;
}