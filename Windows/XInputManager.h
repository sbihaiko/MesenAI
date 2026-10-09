#pragma once

#include "pch.h"
#include "Common.h"
#include "Core/Shared/HapticTickChain.h"
#include <Xinput.h>

class Emulator;

class XInputManager
{
private:
	Emulator* _emu = nullptr;
	XINPUT_STATE _gamePadStates[XUSER_MAX_COUNT] = {};
	uint8_t _gamePadConnected[XUSER_MAX_COUNT] = {};
	bool _enableForceFeedback[XUSER_MAX_COUNT] = {};

	//#1121 (spec #1102): the short menu tick. XInput rumble is a level and has no
	//transient call, so a tick writes a magnitude and a timer puts the slot's own
	//back. The queue and the handle per slot are the plumbing; which tick's end
	//counts, and what it restores, is HapticTickChain's - host-free, so
	//scripts/core_unit_tests.cpp drives it without a pad and without Windows.
	HapticTickChain _tickChain;
	CRITICAL_SECTION _tickLock;
	HANDLE _tickTimerQueue = nullptr;
	HANDLE _tickTimers[XUSER_MAX_COUNT] = {};

	//What a tick's timer is handed: which manager, slot and tick. A callback has
	//no other way back to the manager, and the pair per slot is what lets a tick
	//that is ending find its OWN handle - a single shared record would have been
	//overwritten by the next tick on that slot. Two are enough because a slot never
	//has more than one tick armed plus the one ending.
	struct TickTimerContext
	{
		XInputManager* Manager;
		uint8_t Port;
		uint32_t Handle;
	};
	TickTimerContext _tickContexts[XUSER_MAX_COUNT][2] = {};
	uint8_t _tickContextInUse[XUSER_MAX_COUNT] = {};

	static void CALLBACK TickElapsed(PVOID context, BOOLEAN timerOrWaitFired);

	//The one place XInputSetState is called. A tick goes through it too, which is
	//what keeps the tick's own magnitude out of the chain's record of what the
	//game asked for.
	void ApplyVibration(uint8_t gamepadPort, uint16_t magnitudeRight, uint16_t magnitudeLeft);

public:
	XInputManager(Emulator* emu);
	~XInputManager();

	bool NeedToUpdate();
	void UpdateDeviceList();
	void RefreshState();
	bool IsPressed(uint8_t gamepadPort, uint8_t button);
	optional<int16_t> GetAxisPosition(uint8_t gamepadPort, int axis);

	void SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft);

	//Host input tester (PRD slice I.0): whether the port currently has a pad,
	//and force feedback applied to a single pad instead of all of them.
	bool IsConnected(uint8_t gamepadPort);
	void SetForceFeedback(uint8_t gamepadPort, uint16_t magnitudeRight, uint16_t magnitudeLeft);

	//#1121: one short tick on ONE XInput slot, for menu focus feedback. False when
	//the slot has no pad, or when the timer that would end the tick could not be
	//armed (in which case the pad is put straight back where it was, rather than
	//left buzzing). Who is aimable is IKeyManager's rule; this is the device half.
	bool PlayTick(uint8_t gamepadPort);
};
