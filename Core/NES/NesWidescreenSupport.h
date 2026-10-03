#pragma once
#include "pch.h"

//ADR-0253 slice W.5: the per-game support measurement behind the Enhancements
//sheet's Widescreen switch. Section 4: the first time a game runs, the core
//measures over the first gameplay seconds whether the side columns ever held
//real content. A game whose sides never did is remembered as unsupported per
//ROM, and the switch is shown disabled from the next load with its one-line
//reason.
//
//The whole rule lives here, host-free, so scripts/core_unit_tests.cpp drives it
//with plain booleans. The emulator feeds it one sample per gameplay frame - the
//frame's NesWidescreenReveal::SideColumnsHaveContent, section 3's "cannot fill"
//rule, gated on the background actually being drawn.
namespace NesWidescreenSupport
{
	//300 gameplay frames is 5 s at the NTSC 60 Hz frame (6 s PAL): long enough
	//for a game's first level to start scrolling, short enough not to keep the
	//switch in its "measuring" state for a whole session.
	constexpr uint32_t MeasurementFrames = 300;

	//Mirrored by UI/Logic/WidescreenSupportRule.cs (WidescreenSupport).
	enum class Verdict : uint8_t
	{
		//Still measuring (or nothing observed yet): the switch stays enabled.
		Undecided = 0,
		//Some measured frame had real content beside the picture.
		Supported = 1,
		//The whole window went by with nothing beside the picture.
		Unsupported = 2
	};

	//Section 1/4: the switch being on is not enough. A game the window settled
	//as unsupported "supports no widescreen mode", so it is not widened at all -
	//otherwise the player is left with Reveal/black columns and a switch that is
	//disabled, with no way to turn them off. The switch's saved value is never
	//written off, so the next game that can use it gets it back.
	inline bool Reveals(bool switchOn, Verdict verdict)
	{
		return switchOn && verdict != Verdict::Unsupported;
	}

	class Probe
	{
	private:
		uint32_t _framesObserved = 0;
		bool _foundContent = false;

	public:
		//One gameplay frame's sample. A positive is kept forever - content found
		//after the window still counts (section 4's re-check), so a later level
		//that scrolls clears an earlier "no" within the same session, and the
		//per-ROM record is dropped on the next load.
		void ObserveFrame(bool canFill)
		{
			if(canFill) {
				_foundContent = true;
				return;
			}
			if(_framesObserved < MeasurementFrames) {
				_framesObserved++;
			}
		}

		Verdict GetVerdict() const
		{
			if(_foundContent) {
				return Verdict::Supported;
			}
			return _framesObserved >= MeasurementFrames ? Verdict::Unsupported : Verdict::Undecided;
		}

		uint32_t FramesObserved() const { return _framesObserved; }

		//A new run measures from scratch; the per-ROM memory the caller keeps
		//is what survives across loads.
		void Reset()
		{
			_framesObserved = 0;
			_foundContent = false;
		}
	};
}
