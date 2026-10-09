#pragma once
#include "pch.h"

//#1121 (spec #1102): the policy half of the short menu tick on a backend whose
//rumble is a level and has no transient call - XInput. A tick writes a magnitude
//and a timer puts the pad's own magnitude back, which leaves two decisions the
//Windows timer code cannot make for itself:
//
//  * Whose end counts. A player holding a direction down repeats a focus move, so
//    two ticks can be in flight on one slot at once, each with a timer of its own
//    (a one-shot timer re-armed with ChangeTimerQueueTimer after it expired does
//    not fire again, which leaves the pad buzzing, so every tick arms a fresh
//    one). The first tick's end must not stop the pad under the second tick's
//    feet: only the newest tick on a slot ends it. What keeps a pad from buzzing
//    forever is that the newest tick always has a live timer, so its end always
//    comes.
//  * What "put it back" means. A tick restores the magnitudes the game, or the
//    input tester, last applied to that slot - never zero - so a menu tick over a
//    rumbling game does not silence it.
//
// Host-free and header-only (ADR-0127), so scripts/core_unit_tests.cpp drives it
// without a pad and without Windows. The Windows backend supplies the timer and
// the XInputSetState call, and decides nothing.
class HapticTickChain
{
public:
	//XInput's own slot count (XUSER_MAX_COUNT); the chain is per slot, because a
	//tick on one pad must not touch another.
	static constexpr uint32_t SlotCount = 4;

	//How long a tick holds the pad, and how hard. XInputSetState has no duration,
	//so the timer below is the duration; 40 ms is one light tap at the intensity
	//macOS's transient tick uses (0.8 of a short pulse), well under the 65535 a
	//game's own rumble can reach.
	static constexpr uint32_t TickDurationMs = 40;
	static constexpr uint16_t TickMagnitude = 0x4000;

	//The magnitudes a tick ends with: the slot's own, from before it.
	struct Magnitudes
	{
		uint16_t Right = 0;
		uint16_t Left = 0;
	};

	//Records the magnitudes `slot` holds when no tick is running - what the game,
	//or the input tester, last applied. A tick's own write does not come through
	//here, or a tick would restore itself.
	void RecordApplied(uint32_t slot, uint16_t magnitudeRight, uint16_t magnitudeLeft)
	{
		if(slot >= SlotCount) {
			return;
		}
		_slots[slot].Right = magnitudeRight;
		_slots[slot].Left = magnitudeLeft;
	}

	//Starts a tick on `slot` and hands back the handle that ends it - a value of
	//its own, never a live one twice, which the backend carries to its timer.
	//0 for a slot this chain does not have.
	uint32_t BeginTick(uint32_t slot)
	{
		if(slot >= SlotCount) {
			return 0;
		}
		_slots[slot].Ticking = true;
		return _slots[slot].Handle = ++_lastHandle;
	}

	//Ends the tick `handle` began on `slot`: the magnitudes to put back, or nothing
	//when a later tick on the same slot took over (that tick's own timer ends it)
	//or when nothing is ticking there.
	std::optional<Magnitudes> EndTick(uint32_t slot, uint32_t handle)
	{
		if(slot >= SlotCount || !_slots[slot].Ticking) {
			return std::nullopt;
		}
		_slots[slot].Ticking = false;
		return Magnitudes{ _slots[slot].Right, _slots[slot].Left };
	}

	bool IsTicking(uint32_t slot) const
	{
		return slot < SlotCount && _slots[slot].Ticking;
	}

private:
	struct Slot
	{
		bool Ticking = false;
		uint32_t Handle = 0;
		uint16_t Right = 0;
		uint16_t Left = 0;
	};

	Slot _slots[SlotCount] = {};
	uint32_t _lastHandle = 0;
};
