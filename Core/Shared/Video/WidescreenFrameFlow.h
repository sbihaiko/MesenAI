#pragma once
//ADR-0253 slice W.6: the arithmetic a frame that carries a widescreen Reveal's
//extra columns needs from the display tools that are not the emulator window -
//the NES NTSC filters that re-filter it, the video recorder that writes it and
//the capture tools that measure it.
//
//W.1 put the extra columns in the frame behind a contract and left every tool
//that assumes 256 px on the decoder's standard-center crop
//(BaseVideoFilter::AcceptsExtendedFrame). W.6 makes those tools width-driven
//instead. Host-free on purpose: every number here is asserted in
//scripts/core_unit_tests.cpp without an Emulator, and the shipping code calls
//exactly these functions, so a test and the filter cannot disagree about a
//stride, a buffer size or a phase.
#include <cstdint>
#include <cstring>
#include <vector>
#include "Utilities/NTSC/nes_ntsc.h"

namespace WidescreenFrameFlow
{
	//------------------------------------------------------------------------
	// The standard picture inside an extended frame (the capture tools)
	//------------------------------------------------------------------------

	//Which columns of a capture are the standard picture: the `standardWidth`
	//columns centered in a `frameWidth`-wide frame, i.e. `(frameWidth -
	//standardWidth) / 2` in from either edge. The extra columns are
	//presentation only (ADR-0253 §2), so a capture of an extended frame is
	//compared against a frame drawn with the switch off on this region and
	//nothing else.
	//
	//The caller names the *standard* width it expects - the console's own,
	//times whatever scale filter ran - rather than the number of extra columns,
	//because that is the half of the arithmetic it knows and the function does
	//not. It also makes "there is no center here" a real answer instead of an
	//assumption: a capture no wider than the standard picture (a standard
	//frame), one narrower than it, and one whose extra columns do not split
	//evenly all have no center.
	//
	//This answers "is there a center of this width in this capture", not "is
	//this capture an extended frame": a frame widened by an NTSC filter also
	//has an arithmetic center, and it is not the picture. A tool that must only
	//measure extended frames still checks the frame's shape itself.
	struct Centre
	{
		uint32_t Offset = 0;
		uint32_t Width = 0;

		bool IsValid() const { return Width > 0; }
	};

	inline Centre StandardCentre(uint32_t frameWidth, uint32_t standardWidth)
	{
		if(standardWidth == 0 || frameWidth <= standardWidth) {
			return { 0, 0 };
		}
		uint32_t extra = frameWidth - standardWidth;
		if(extra % 2 != 0) {
			return { 0, 0 };
		}
		return { extra / 2, standardWidth };
	}

	//Copies that center out of a `width` x `height` capture, row by row. False
	//(and `out` cleared) for a capture whose center does not exist, so a
	//standard capture is refused rather than read as if it were extended.
	inline bool ExtractCentre(const uint32_t* pixels, uint32_t width, uint32_t height, uint32_t standardWidth, std::vector<uint32_t>& out)
	{
		out.clear();
		Centre centre = StandardCentre(width, standardWidth);
		if(!pixels || height == 0 || !centre.IsValid()) {
			return false;
		}
		out.resize((size_t)centre.Width * height);
		for(uint32_t y = 0; y < height; y++) {
			memcpy(out.data() + (size_t)y * centre.Width, pixels + (size_t)y * width + centre.Offset, (size_t)centre.Width * sizeof(uint32_t));
		}
		return true;
	}

	//------------------------------------------------------------------------
	// The NES NTSC filters
	//------------------------------------------------------------------------
	namespace Ntsc
	{
		//nes_ntsc's blitter turns 3 input pixels into 7 output pixels, so the
		//width it writes - and therefore the plane its caller must own - is a
		//function of the frame it is handed. A buffer sized for 256 px is short
		//by the extra columns' own output pixels the moment the frame is a
		//Reveal frame, i.e. the blit writes past its end.
		inline uint32_t BlitOutputWidth(uint32_t inWidth)
		{
			return inWidth == 0 ? 0 : (uint32_t)NES_NTSC_OUT_WIDTH(inWidth);
		}

		inline uint64_t BlitPlanePixels(uint32_t inWidth, uint32_t height)
		{
			return (uint64_t)BlitOutputWidth(inWidth) * height;
		}

		//What a filter reports as the frame's width once the overscan has come
		//off the blitted plane - i.e. what becomes RenderedFrame::Width, and so
		//what the video recorder is opened at. Total on purpose: a blitted
		//width now follows the frame, so unlike the old 256 px constant it can
		//be no wider than the overscan wants to cut, and a plain subtraction
		//would wrap into a four-billion-pixel frame.
		inline uint32_t BlitVisibleWidth(uint32_t inWidth, uint32_t overscanLeft, uint32_t overscanRight)
		{
			uint64_t blitted = BlitOutputWidth(inWidth);
			uint64_t overscan = (uint64_t)overscanLeft + overscanRight;
			return blitted > overscan ? (uint32_t)(blitted - overscan) : 0;
		}

		//The horizontal scale the HUD is drawn at over a blitted frame. The two
		//axes are not scaled alike - the blit's 602/256 against 480/240 is the
		//standard case - which is why the recorder lays its own HUD out on the
		//decoder's base frame rather than on a quotient. Reporting the 256 px
		//ratio for a Reveal frame would crowd the HUD into a third of the
		//picture, so it is the frame's own ratio.
		inline double BlitHudScaleX(uint32_t inWidth)
		{
			return inWidth == 0 ? 1.0 : (double)BlitOutputWidth(inWidth) / inWidth;
		}

		//Bisqwit's decoder carries the color subcarrier as 8 NTSC samples per
		//frame pixel, so a row's signal is 8 samples for every pixel of the
		//frame it was handed.
		constexpr int32_t SignalsPerPixel = 8;

		inline uint32_t SignalSamples(uint32_t width)
		{
			return width * (uint32_t)SignalsPerPixel;
		}

		//The NES PPU's scanline is 341 cycles wide whatever part of it the
		//picture shows, so the phase a row ends on depends on the whole scanline
		//and not on the pixels drawn. The invariant:
		//width * SignalsPerPixel + PhaseAdvanceAfterRow(width)
		//    == ScanlineCycles * SignalsPerPixel, for every width.
		constexpr int32_t ScanlineCycles = 341;

		inline int32_t PhaseAdvanceAfterRow(uint32_t width)
		{
			return (ScanlineCycles - (int32_t)width) * SignalsPerPixel;
		}
	}

	//------------------------------------------------------------------------
	// The video recorder
	//------------------------------------------------------------------------

	//The canvas VideoRenderer::ProcessAviRecording lays the input/system HUD
	//out on before drawing that HUD onto the recording at one uniform scale.
	//Both sides come from the frame's own numbers - the scale is what the
	//frame's height implies against the decoder's base frame - so a Reveal
	//recording gets a Reveal-sized canvas. Laying it out on the standard
	//canvas instead is what crowds the HUD into part of the picture or clips
	//it, which is why this is a function and not a constant.
	struct HudCanvas
	{
		uint32_t Width = 0;
		uint32_t Height = 0;
	};

	inline HudCanvas RecorderHudCanvas(uint32_t frameWidth, uint32_t frameHeight, uint32_t baseHeight)
	{
		if(frameHeight == 0 || baseHeight == 0) {
			return { 0, 0 };
		}
		double scale = (double)frameHeight / baseHeight;
		return { (uint32_t)(frameWidth / scale), (uint32_t)(frameHeight / scale) };
	}
}
