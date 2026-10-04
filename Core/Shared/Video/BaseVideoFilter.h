#pragma once
#include "pch.h"
#include "Utilities/SimpleLock.h"
#include "Shared/SettingTypes.h"
#include "Shared/RenderedFrame.h"
#include "Shared/Video/BlendFilter.h"
#include "Shared/Video/FrameCapture.h"

class Emulator;

class BaseVideoFilter
{
private:
	uint32_t* _outputBuffer = nullptr;
	double _yiqToRgbMatrix[6] = {};
	uint32_t _bufferSize = 0;
	SimpleLock _frameLock;
	OverscanDimensions _overscan = {};
	bool _isOddFrame = false;
	uint32_t _videoPhase = 0;

	void UpdateBufferSize();

protected:
	Emulator* _emu = nullptr;
	FrameInfo _baseFrameInfo = {};
	FrameInfo _frameInfo = {};
	RenderedFrame _frame = {};
	BlendFilter _blendFilter;
	void* _frameData = nullptr;
	uint16_t* _ppuOutputBuffer = nullptr;

	void InitConversionMatrix(double hueShift, double saturationShift);
	void ApplyColorOptions(uint8_t& r, uint8_t& g, uint8_t& b, double brightness, double contrast);
	void RgbToYiq(double r, double g, double b, double& y, double& i, double& q);
	void YiqToRgb(double y, double i, double q, double& r, double& g, double& b);

	virtual void ApplyFilter(uint16_t* ppuOutputBuffer) = 0;
	virtual void OnBeforeApplyFilter();
	bool IsOddFrame();
	uint32_t GetVideoPhase();
	uint32_t GetBufferSize();
	virtual FrameInfo GetFrameInfo();

public:
	BaseVideoFilter(Emulator* emu);
	virtual ~BaseVideoFilter();

	uint32_t* GetOutputBuffer();
	FrameInfo SendFrame(uint16_t* ppuOutputBuffer, uint32_t frameNumber, uint32_t videoPhase, void* frameData, bool enableOverscan = true, RenderedFrame frame = {});
	void TakeScreenshot(string romName, VideoFilterType filterType);
	void TakeScreenshot(VideoFilterType filterType, string filename, std::stringstream* stream = nullptr);

	//F9.15: the same pipeline TakeScreenshot writes to a PNG, stopping one
	//step earlier - the pixels land in a caller-owned vector instead of on
	//disk. Returns an empty ScreenshotCapture (and clears `out`) when no
	//frame has been decoded yet or the dimensions do not validate.
	ScreenshotCapture CaptureScreenshot(VideoFilterType filterType, vector<uint32_t>& out);

	//The unfiltered half of it: the filtered output buffer copied out under
	//_frameLock, with the dimensions validated *before* `out` is resized.
	ScreenshotCapture CopyOutputBuffer(vector<uint32_t>& out);

	virtual HudScaleFactors GetScaleFactor() { return { 1.0, 1.0 }; }

	//ADR-0253: whether this filter decodes a frame of any width, so a
	//widescreen Reveal frame (RenderedFrame::ExtendedColumns > 0) can be
	//handed to it whole. A filter that assumes the console's standard width
	//(the NES NTSC filters) gets the standard centre instead - VideoDecoder
	//crops it. W.6 widens those filters.
	virtual bool AcceptsExtendedFrame() { return false; }

	//ADR-0253 §3 (W.3): the side-column contract of the frame this filter has
	//just produced, in the filter's *own* output coordinates - how many columns
	//on each side of the output are the Reveal's, and the per-row map of which of
	//them the console filled (RenderedFrame::ExtendedColumns/ExtendedSideFill).
	//
	//The default is the identity: a filter that hands the frame over in its own
	//geometry returns the console's contract unchanged, and one that rescaled the
	//picture returns nothing at all - the map's rows are the console's 240, and a
	//rescaled picture breaks that correspondence. A filter that draws an extended
	//frame at a scale of its own (the HD pack filter) restates the contract in its
	//own coordinates instead.
	struct FrameExtension
	{
		uint32_t Columns = 0;
		const uint8_t* SideFill = nullptr;
	};
	virtual FrameExtension GetOutputFrameExtension();

	virtual OverscanDimensions GetOverscan();
	void SetOverscan(OverscanDimensions dimensions);
	FrameInfo GetFrameInfo(uint16_t* ppuOutputBuffer, bool enableOverscan);

	void SetBaseFrameInfo(FrameInfo frameInfo);
};
