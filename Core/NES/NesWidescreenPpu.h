#pragma once
#include "pch.h"
#include "NES/NesWidescreenReveal.h"
#include "NES/NesWidescreenSupport.h"
#include "Shared/RenderedFrame.h"

//ADR-0253 (slices W.1, W.3, W.5): the NES PPU's side of the widescreen Reveal,
//shared by the two PPUs that draw a picture - DefaultNesPpu, and HdNesPpu once
//an HD pack is loaded.
//
//W.1/W.3 built the Reveal and its fallback chain on the default path; W.4 gave
//the HD path its own copy of the frame widening, and W.5 gave only the default
//path the per-game support measurement. It is one feature, so it answers the
//same on either path and lives here once:
//
//- the Reveal is latched at the frame boundary from the switch, the Vs.
//  DualSystem exception, a mapper to read the sides through, and the
//  measurement (RevealRequested);
//- every row the game really drew is measured, whether or not the switch is on
//  (ObserveRow) - the measurement is how the switch learns whether this game can
//  use it at all (section 4);
//- the frame the decoder is handed carries the extended picture *and* the
//  per-row side-fill map the fallback chain reads (PublishFrame), which is what
//  makes the chain run at all (VideoRenderer::UpdateFrame skips both links when
//  the map is absent).
//
//Host-free on purpose: scripts/core_unit_tests.cpp drives it with no PPU, no
//Emulator and no pack.
namespace NesWidescreenPpu
{
	//ADR-0253 §1/§4: the one rule both PPUs latch with. WideScrn on, a mapper to
	//read the sides through, not a Vs. DualSystem - which merges two standard
	//frames side by side, so it stays standard - and a game the measurement has
	//not settled as unsupported. A game it did settle is not widened at all,
	//whatever the switch says: the switch is disabled for it, so Reveal/black
	//columns nobody can turn off must not appear - unless the loaded pack ships
	//widescreen art (§3), which gives that game a mode of its own.
	inline bool RevealRequested(bool widescreenAspect, bool vsDualSystem, bool hasMapper, NesWidescreenSupport::Verdict verdict, bool packArtAvailable)
	{
		return NesWidescreenSupport::Reveals(widescreenAspect && !vsDualSystem && hasMapper, verdict, packArtAvailable);
	}

	class State
	{
	private:
		NesWidescreenReveal::FrameBuffers _reveal;
		NesWidescreenSupport::Probe _probe;
		//ADR-0253 §4: `_frameHadRendering` keeps a forced-blank or power-on frame
		//from advancing the window, so a boot screen is not "gameplay".
		bool _frameHadRendering = false;
		bool _frameHadSideContent = false;

	public:
		//The frame boundary, on the pre-render line (row 0, cycle 257): the frame
		//that just ended is the probe's sample, and the Reveal is latched once per
		//frame, so a switch flipped mid-frame never yields half a frame.
		void BeginFrame(bool widescreenAspect, bool vsDualSystem, bool hasMapper, bool packArtAvailable)
		{
			if(_frameHadRendering) {
				_probe.ObserveFrame(_frameHadSideContent);
			}
			_frameHadRendering = false;
			_frameHadSideContent = false;
			_reveal.BeginFrame(RevealRequested(widescreenAspect, vsDualSystem, hasMapper, _probe.GetVerdict(), packArtAvailable));
		}

		//One row's captured basis, measured against §3's "cannot fill" rule.
		//Called whether or not the switch is on (§4): only a row the game really
		//drew can carry side content, and a frame with no such row is not
		//gameplay.
		void ObserveRow(const NesWidescreenReveal::RowBasis& basis, MirroringType mirroring)
		{
			if(!basis.BgEnabled) {
				return;
			}
			_frameHadRendering = true;
			if(NesWidescreenReveal::SideColumnsHaveContent(mirroring, NesWidescreenReveal::RowOriginX(basis))) {
				_frameHadSideContent = true;
			}
		}

		//ADR-0253 §4 (W.5): what this PPU measured for the running game.
		NesWidescreenSupport::Verdict GetVerdict() const { return _probe.GetVerdict(); }

		//The extended buffer and its per-row fill map the rows are drawn into.
		NesWidescreenReveal::FrameBuffers& Reveal() { return _reveal; }

		//ADR-0253 §2/§3: hands `frame` the extended picture and the per-row
		//side-fill map the fallback chain reads, or leaves it the console's own
		//standard picture when this frame is not widened. The map is the Reveal's
		//own, so a frame that is not extended never carries one
		//(RenderedFrame::ClearExtension's rule).
		void PublishFrame(RenderedFrame& frame, const uint16_t* standardFrame)
		{
			const uint16_t* extended = _reveal.Finish(standardFrame);
			if(!extended) {
				return;
			}
			frame.FrameBuffer = (void*)extended;
			frame.Width = NesWidescreenReveal::ExtendedWidth;
			frame.ExtendedColumns = NesWidescreenReveal::ExtraColumns;
			frame.ExtendedSideFill = _reveal.LastFill();
		}
	};
}
