#pragma once
#include "pch.h"
#include "Utilities/SimpleLock.h"
#include "Utilities/AutoResetEvent.h"
#include "Shared/SettingTypes.h"
#include "Shared/RenderedFrame.h"
#include "Shared/Video/FrameCapture.h"

class BaseVideoFilter;
class ScaleFilter;
class RotateFilter;
class IRenderingDevice;
class Emulator;

class VideoDecoder
{
private:
	Emulator* _emu;

	ConsoleType _consoleType = ConsoleType::Snes;

	unique_ptr<thread> _decodeThread;

	SimpleLock _stopStartLock;
	AutoResetEvent _waitForFrame;

	atomic<bool> _frameChanged;
	atomic<bool> _stopFlag;
	uint32_t _frameCount = 0;
	bool _forceFilterUpdate = false;

	double _lastAspectRatio = 0.0;

	FrameInfo _baseFrameSize = {};
	FrameInfo _lastFrameSize = {};
	RenderedFrame _frame = {};

	VideoFilterType _videoFilterType = VideoFilterType::None;
	unique_ptr<BaseVideoFilter> _videoFilter;
	unique_ptr<ScaleFilter> _scaleFilter;
	//ADR-0246 §5: the console's default filter, used instead of an NTSC filter
	//while Hold to Compare is on. Built on the first compared frame and kept
	//until the filters are rebuilt, so holding again costs nothing.
	unique_ptr<BaseVideoFilter> _compareFilter;
	//Whether _videoFilter is the pack-art filter (IConsole::IsDrawingPackArt
	//when it was built). Compare never replaces it: Art stays.
	bool _videoFilterIsPackArt = false;
	unique_ptr<RotateFilter> _rotateFilter;

	//ADR-0253: the standard center of a widescreen Reveal frame, for a filter
	//(or a border layer) that cannot take the extra columns
	vector<uint16_t> _standardCentre;

	void UpdateVideoFilter();
	BaseVideoFilter* GetFrameFilter(bool compare);
	void KeepStandardCentre();

	void DecodeThread();

public:
	VideoDecoder(Emulator* console);
	~VideoDecoder();

	void Init();

	void DecodeFrame(bool synchronous = false);
	void TakeScreenshot(string romName = "");
	void TakeScreenshot(std::stringstream& stream);

	//F9.15: the same screenshot, kept in memory. `out` is the caller's; the
	//returned capture is empty when no filter/frame exists yet.
	ScreenshotCapture CaptureScreenshot(vector<uint32_t>& out);

	void ForceFilterUpdate() { _forceFilterUpdate = true; }

	//ADR-0246 §5: while paused no new frame reaches the decoder, so a Look
	//change (or Hold to Compare) would not show until the game resumes. Decodes
	//the last frame again through the current filters - the same path a state
	//loaded while paused takes (SaveStateManager::LoadState). No-op while
	//running, while recording, or before the first frame.
	void RedrawPausedFrame();

	uint32_t GetFrameCount();
	FrameInfo GetBaseFrameInfo(bool removeOverscan);
	FrameInfo GetFrameInfo();
	double GetLastFrameScale() { return _frame.Scale; }

	//ADR-0253: whether the frame being shown carries a widescreen Reveal's
	//extra columns (read by EmuSettings::GetAspectRatio)
	bool IsFrameExtended() { return _frame.ExtendedColumns > 0; }

	void UpdateFrame(RenderedFrame frame, bool sync, bool forRewind);

	void WaitForAsyncFrameDecode();

	bool IsRunning();
	void StartThread();
	void StopThread();
};