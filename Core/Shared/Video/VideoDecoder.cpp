#include "pch.h"
#include "Shared/Video/VideoDecoder.h"
#include "Shared/Video/VideoRenderer.h"
#include "Shared/Video/BaseVideoFilter.h"
#include "Shared/NotificationManager.h"
#include "Shared/Emulator.h"
#include "Shared/RewindManager.h"
#include "Shared/EmuSettings.h"
#include "Shared/SettingTypes.h"
#include "Shared/Video/ScaleFilter.h"
#include "Shared/Video/RotateFilter.h"
#include "Shared/Video/ScanlineFilter.h"
#include "Shared/Video/DebugHud.h"
#include "Shared/RenderedFrame.h"
#include "Shared/Interfaces/IConsole.h"

VideoDecoder::VideoDecoder(Emulator* emu)
{
	_emu = emu;
	_frameChanged = false;
	_stopFlag = false;
	_baseFrameSize = { 256, 239 };
	_lastFrameSize = _baseFrameSize;
}

VideoDecoder::~VideoDecoder()
{
	StopThread();
}

void VideoDecoder::Init()
{
	UpdateVideoFilter();
	_videoFilter->SetBaseFrameInfo(_baseFrameSize);
}

FrameInfo VideoDecoder::GetBaseFrameInfo(bool removeOverscan)
{
	if(removeOverscan) {
		OverscanDimensions overscan = _emu->GetSettings()->GetOverscan();
		uint32_t hOverscan = overscan.Left + overscan.Right;
		uint32_t vOverscan = overscan.Top + overscan.Bottom;

		bool swapOverscan = (_emu->GetSettings()->GetVideoConfig().ScreenRotation % 180) != 0;
		if(swapOverscan) {
			std::swap(hOverscan, vOverscan);
		}

		return {
			(uint32_t)(_baseFrameSize.Width * _frame.Scale) - hOverscan,
			(uint32_t)(_baseFrameSize.Height * _frame.Scale) - vOverscan
		};
	} else {
		return {
			(uint32_t)(_baseFrameSize.Width * _frame.Scale),
			(uint32_t)(_baseFrameSize.Height * _frame.Scale)
		};
	}
}

FrameInfo VideoDecoder::GetFrameInfo()
{
	return _lastFrameSize;
}

void VideoDecoder::UpdateVideoFilter()
{
	VideoFilterType newFilter = _emu->GetSettings()->GetVideoConfig().VideoFilter;
	ConsoleType consoleType = _emu->GetConsoleType();

	if(_videoFilterType != newFilter || _videoFilter == nullptr || _consoleType != consoleType || _forceFilterUpdate) {
		_videoFilterType = newFilter;
		_consoleType = consoleType;

		_videoFilter.reset(_emu->GetVideoFilter());
		_scaleFilter = ScaleFilter::GetScaleFilter(_emu, _videoFilterType);
		_compareFilter.reset();
		shared_ptr<IConsole> console = _emu->GetConsole();
		_videoFilterIsPackArt = console && console->IsDrawingPackArt();
		_forceFilterUpdate = false;
	}

	uint32_t screenRotation = _emu->GetSettings()->GetVideoConfig().ScreenRotation;
	_emu->GetScreenRotationOverride(screenRotation);

	if(screenRotation != 0) {
		if(!_rotateFilter || _rotateFilter->GetAngle() != screenRotation) {
			_rotateFilter.reset(new RotateFilter(screenRotation));
		}
	} else {
		_rotateFilter.reset();
	}
}

BaseVideoFilter* VideoDecoder::GetFrameFilter(bool compare)
{
	//ADR-0246 §5: Hold to Compare drops the NTSC console filter (Screen) but
	//never the pack-art filter (Art). Every other console filter already is the
	//default one.
	bool isNtsc = _videoFilterType == VideoFilterType::NtscBlargg || _videoFilterType == VideoFilterType::NtscBisqwit;
	if(!compare || !isNtsc || _videoFilterIsPackArt) {
		return _videoFilter.get();
	}
	if(!_compareFilter) {
		_compareFilter.reset(_emu->GetVideoFilter(true));
	}
	return _compareFilter.get();
}

void VideoDecoder::RedrawPausedFrame()
{
	//#616: Stop() keeps the pause flag, so "paused" alone does not mean a game
	//is loaded. With no console there is nothing to redraw, and the console's
	//video filter would dereference it (NesDefaultVideoFilter reads its PPU).
	if(!_emu->IsRunning() || !_emu->IsPaused() || _emu->GetVideoRenderer()->IsRecording() || _frame.FrameBuffer == nullptr) {
		return;
	}
	auto lock = _emu->AcquireLock();
	if(!_emu->IsRunning() || _frame.FrameBuffer == nullptr) {
		return;
	}
	UpdateFrame(_frame, true, false);
}

void VideoDecoder::KeepStandardCentre()
{
	//Only the NES emits extended frames today, in its 16-bit PPU format
	uint32_t standardWidth = _frame.Width - 2 * _frame.ExtendedColumns;
	_standardCentre.resize((size_t)standardWidth * _frame.Height);
	const uint16_t* src = (const uint16_t*)_frame.FrameBuffer;
	for(uint32_t y = 0; y < _frame.Height; y++) {
		memcpy(_standardCentre.data() + (size_t)y * standardWidth, src + (size_t)y * _frame.Width + _frame.ExtendedColumns, standardWidth * sizeof(uint16_t));
	}
	_frame.FrameBuffer = _standardCentre.data();
	_frame.Width = standardWidth;
	//Both extended fields, not just the width: the side-fill map describes
	//columns this frame no longer has (RenderedFrame::ClearExtension).
	_frame.ClearExtension();
}

void VideoDecoder::DecodeFrame(bool forRewind)
{
	UpdateVideoFilter();
	bool compare = _emu->GetSettings()->IsLookCompare();
	BaseVideoFilter* videoFilter = GetFrameFilter(compare);

	//ADR-0253 W.1: a filter that assumes the standard width gets the standard
	//picture, so nothing downstream ever reads a row at the wrong stride; the
	//aspect ratio then falls back with it (IsFrameExtended). W.3 taught the
	//border composite to take an extended frame itself (the side columns the
	//game left unfilled become the border's), so a composited border is no
	//longer a reason to drop the extra columns.
	if(_frame.ExtendedColumns > 0 && _frame.FrameBuffer && !videoFilter->AcceptsExtendedFrame()) {
		KeepStandardCentre();
	}

	bool isAudioPlayer = _emu->GetAudioPlayerHud() != nullptr;
	if(isAudioPlayer) {
		//When an audio file is loaded, force base resolution to 256x240 for all consoles
		_baseFrameSize.Width = 256;
		_baseFrameSize.Height = 240;
	} else {
		_baseFrameSize.Width = _frame.Width;
		_baseFrameSize.Height = _frame.Height;
	}

	videoFilter->SetBaseFrameInfo(_baseFrameSize);
	FrameInfo frameSize = videoFilter->SendFrame((uint16_t*)_frame.FrameBuffer, _frame.FrameNumber, _frame.VideoPhase, _frame.Data, true, _frame);

	uint32_t* outputBuffer = videoFilter->GetOutputBuffer();

	OverscanDimensions overscan = videoFilter->GetOverscan();

	if(_rotateFilter && !isAudioPlayer) {
		outputBuffer = _rotateFilter->ApplyFilter(outputBuffer, frameSize.Width, frameSize.Height);
		if((_rotateFilter->GetAngle() % 180) != 0) {
			//90 or 270 rotation, swap height & width
			std::swap(_baseFrameSize.Width, _baseFrameSize.Height);
			frameSize = _rotateFilter->GetFrameInfo(frameSize);
		}
	}

	_emu->GetDebugHud()->Draw(outputBuffer, frameSize, overscan, _frame.FrameNumber, videoFilter->GetScaleFactor());

	//ADR-0246 §5: Hold to Compare drops Pixels (the scale filter, LcdGrid too).
	if(_scaleFilter && !isAudioPlayer && !compare) {
		outputBuffer = _scaleFilter->ApplyFilter(outputBuffer, frameSize.Width, frameSize.Height);
		frameSize = _scaleFilter->GetFrameInfo(frameSize);
	}

	if(!isAudioPlayer) {
		uint8_t scale = std::max<uint8_t>(1, (uint8_t)((double)frameSize.Height / (_frame.Height - overscan.Top - overscan.Bottom)));
		ScanlineFilter::ApplyFilter(outputBuffer, frameSize.Width, frameSize.Height, _emu->GetSettings()->GetVideoConfig().ScanlineIntensity, scale);
	}

	RenderedFrame convertedFrame((void*)outputBuffer, frameSize.Width, frameSize.Height, _frame.Scale, _frame.FrameNumber, _frame.InputData);

	//ADR-0253 §3 (W.3): the frame-width contract and the per-row side fill map
	//travel to the renderer with the picture, so the fallback chain (pack art,
	//then the border layer) can fill the side columns the console could not.
	//Only when no filter rescaled the picture: the map's rows are the console's
	//rows, and a scale/rotate filter would invalidate that correspondence.
	if(frameSize.Width == _frame.Width && frameSize.Height == _frame.Height) {
		convertedFrame.ExtendedColumns = _frame.ExtendedColumns;
		convertedFrame.ExtendedSideFill = _frame.ExtendedSideFill;
	}

	double aspectRatio = _emu->GetSettings()->GetAspectRatio(_emu->GetRegion(), _baseFrameSize);
	if(frameSize.Height != _lastFrameSize.Height || frameSize.Width != _lastFrameSize.Width || aspectRatio != _lastAspectRatio) {
		_emu->GetNotificationManager()->SendNotification(ConsoleNotificationType::ResolutionChanged);
	}
	_lastAspectRatio = aspectRatio;
	_lastFrameSize = frameSize;

	//Rewind manager will take care of sending the correct frame to the video renderer
	_emu->GetRewindManager()->SendFrame(convertedFrame, forRewind);

	_frameChanged = false;
}

void VideoDecoder::DecodeThread()
{
	//This thread will decode the PPU's output (color ID to RGB, intensify r/g/b and produce a HD version of the frame if needed)
	while(!_stopFlag.load()) {
		//DecodeFrame returns the final ARGB frame we want to display in the emulator window
		while(!_frameChanged) {
			_waitForFrame.Wait();
			if(_stopFlag.load()) {
				return;
			}
		}

		DecodeFrame();
	}
}

uint32_t VideoDecoder::GetFrameCount()
{
	return _frameCount;
}

void VideoDecoder::WaitForAsyncFrameDecode()
{
	while(_frameChanged) {
		//Spin until decode is done
		std::this_thread::sleep_for(std::chrono::duration<int, std::milli>(15));
	}
}

void VideoDecoder::UpdateFrame(RenderedFrame frame, bool sync, bool forRewind)
{
	if(_emu->IsRunAheadFrame()) {
		return;
	}

	if(_frameChanged) {
		//Last frame isn't done decoding yet - sometimes Signal() introduces a 25-30ms delay
		while(_frameChanged) {
			//Spin until decode is done
		}
		//At this point, we are sure that the decode thread is no longer busy
	}

	_emu->OnBeforeSendFrame();

	_frame = frame;
	if(sync) {
		DecodeFrame(forRewind);
	} else {
		_frameChanged = true;
		_waitForFrame.Signal();
	}
	_frameCount++;
}

void VideoDecoder::StartThread()
{
	auto lock = _stopStartLock.AcquireSafe();
	if(!_decodeThread) {
		_videoFilter.reset();
		UpdateVideoFilter();
		_videoFilter->SetBaseFrameInfo(_baseFrameSize);
		_stopFlag = false;
		_frameChanged = false;
		_frameCount = 0;
		_waitForFrame.Reset();

		_emu->GetVideoRenderer()->ClearFrame();

		_decodeThread.reset(new thread(&VideoDecoder::DecodeThread, this));
	}
}

void VideoDecoder::StopThread()
{
	auto lock = _stopStartLock.AcquireSafe();
	_stopFlag = true;
	if(_decodeThread) {
		_waitForFrame.Signal();
		_decodeThread->join();

		_decodeThread.reset();

		//#616: the last frame points into the buffer of the console being
		//stopped or replaced; drop it so RedrawPausedFrame can never decode it.
		_frame = RenderedFrame();

		//Clear whole screen
		_emu->GetVideoRenderer()->ClearFrame();
	}
}

bool VideoDecoder::IsRunning()
{
	return _decodeThread != nullptr;
}

void VideoDecoder::TakeScreenshot(string romName)
{
	if(_videoFilter) {
		_videoFilter->TakeScreenshot(romName.empty() ? _emu->GetRomInfo().RomFile.GetFileName() : romName, _videoFilterType);
	}
}

ScreenshotCapture VideoDecoder::CaptureScreenshot(vector<uint32_t>& out)
{
	if(!_videoFilter) {
		out.clear();
		return {};
	}
	return _videoFilter->CaptureScreenshot(_videoFilterType, out);
}

void VideoDecoder::TakeScreenshot(std::stringstream& stream)
{
	if(_videoFilter) {
		_videoFilter->TakeScreenshot(_videoFilterType, "", &stream);
	}
}
