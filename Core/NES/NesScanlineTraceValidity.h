#pragma once

//Issue #419 (ADR-0215, amended 2026-09-24). Whether BaseNesPpu's two
//per-scanline traces (_scanlineVideoRamAddr, _scanlineChrBankOffsets)
//describe a whole frame the PPU drew - the frame on screen - or only whatever
//was left in them before a save state load or a reset.
//
//Neither trace is part of a save state (ADR-0215's "Correction,
//2026-09-19"), so after a load they still hold the frame drawn before it. The
//copy actions resolved keys through that leftover: F14.2 measured Dr. Mario's
//cell (0,0) copying as 1276 instead of 252, and Super Mario Bros. and Zelda
//copying nothing - all silent. Serializing ~31 KB of trace into every state
//(and every rewind snapshot) to describe a frame that is about to be redrawn
//anyway would buy nothing, so the core instead remembers that the trace is
//stale and says so; the copy refuses until a frame has been drawn.
//
//Fed from two PPU points (NesPpu):
//  - OnRowZeroCaptured: the pre-render line's cycle 257, the first write of a
//    frame's trace (row 0; every later row is written by the scanline before
//    it). A load that lands after this point but before the frame ends leaves
//    rows above it from before the load, so that frame does not count.
//  - OnVisibleFrameEnd: scanline 240, where the frame is sent to the screen.
//Host-free (ADR-0127) so scripts/core_unit_tests.cpp can cover it without an
//emulator.
class NesScanlineTraceValidity
{
private:
	//Row 0 was captured since the last Invalidate, so the frame in progress is
	//being traced from its top.
	bool _tracingWholeFrame = false;
	//A whole frame traced from row 0 has reached the end of the visible frame.
	bool _describesDrawnFrame = false;

public:
	//A state load (NesPpu::Serialize on restore) or a reset.
	void Invalidate()
	{
		_tracingWholeFrame = false;
		_describesDrawnFrame = false;
	}

	void OnRowZeroCaptured()
	{
		_tracingWholeFrame = true;
	}

	void OnVisibleFrameEnd()
	{
		if(_tracingWholeFrame) {
			_describesDrawnFrame = true;
		}
	}

	bool DescribesDrawnFrame() const
	{
		return _describesDrawnFrame;
	}
};
