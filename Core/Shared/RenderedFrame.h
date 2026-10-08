#pragma once
#include "pch.h"
#include "Shared/ControlDeviceState.h"

enum class FrameFlags
{
	None = 0,
	Interlaced = 1
};

struct RenderedFrame
{
	void* FrameBuffer = nullptr;
	void* Data = nullptr; //Used by HD packs
	uint32_t Width = 256;
	uint32_t Height = 240;
	double Scale = 1.0;
	uint32_t FrameNumber = 0;
	uint32_t VideoPhase = 0;
	FrameFlags Flags = FrameFlags::None;
	//ADR-0253 (frame-width contract): how many of Width's columns on EACH side
	//are extra, drawn beside the console's standard picture by a widescreen
	//Reveal. 0 = a standard frame. The standard picture is the center
	//Width - 2 * ExtendedColumns columns, bit-identical to what the console
	//sends with the switch off.
	uint32_t ExtendedColumns = 0;
	//ADR-0253 §3 (slice W.3): one byte per row of an extended frame, saying
	//which side columns the console itself filled (bit 0 = left, bit 1 = right -
	//WidescreenFallback.h). The rows a side is not filled on are the ones the
	//pack's `<widescreen>` art, its border layer, or black fill in. Null on a
	//standard frame; owned by the console that produced the frame, valid for as
	//long as FrameBuffer is.
	const uint8_t* ExtendedSideFill = nullptr;
	vector<ControllerData> InputData;

	//ADR-0253 §2/§3: the two fields above describe each other, so a frame that
	//stops being extended clears both. A side-fill map left behind by a frame
	//that dropped back to the standard width is a map of columns the picture no
	//longer has, and the border composite indexes it per row - so the two go
	//together, everywhere.
	void ClearExtension()
	{
		ExtendedColumns = 0;
		ExtendedSideFill = nullptr;
	}

	RenderedFrame()
	{
	}

	RenderedFrame(void* buffer, uint32_t width, uint32_t height, double scale = 1.0, uint32_t frameNumber = 0)
		: FrameBuffer(buffer),
		  Data(nullptr),
		  Width(width),
		  Height(height),
		  Scale(scale),
		  FrameNumber(frameNumber),
		  InputData({})
	{
	}

	RenderedFrame(void* buffer, uint32_t width, uint32_t height, double scale, uint32_t frameNumber, vector<ControllerData> inputData, uint32_t videoPhase = 0)
		: FrameBuffer(buffer),
		  Data(nullptr),
		  Width(width),
		  Height(height),
		  Scale(scale),
		  FrameNumber(frameNumber),
		  VideoPhase(videoPhase),
		  InputData(inputData)
	{
	}
};
