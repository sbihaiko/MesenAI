#pragma once

#include "pch.h"
#include "Common.h"
#include <Xinput.h>
#include <atomic>
#include <mutex>

class Emulator;

class XInputManager
{
private:
	Emulator* _emu = nullptr;
	XINPUT_STATE _gamePadStates[XUSER_MAX_COUNT] = {};
	uint8_t _gamePadConnected[XUSER_MAX_COUNT] = {};
	bool _enableForceFeedback[XUSER_MAX_COUNT] = {};
	//#1106: when a menu tick's motors must be switched off again, 0 = no tick
	//running. XInput has no timed effect, so each slot's one-shot timer ends the
	//pulse - independent of RefreshState, which does not run while input is disabled.
	std::atomic<ULONGLONG> _tickStopAt[XUSER_MAX_COUNT] = {};
	//The rumble the game or the tester last asked for, per slot; a tick ends by
	//restoring it, not by silencing the pad. Written by the emulation/UI threads and
	//read by the timer thread, so _rumbleLock guards it and the tick state.
	struct { uint16_t Right; uint16_t Left; } _desiredRumble[XUSER_MAX_COUNT] = {};
	std::mutex _rumbleLock;
	struct TickTimer { XInputManager* Owner; uint8_t Slot; HANDLE Handle; };
	TickTimer _tickTimers[XUSER_MAX_COUNT] = {};

	static VOID CALLBACK OnTickExpired(PVOID context, BOOLEAN timerOrWaitFired);
	void EndTick(uint8_t gamepadPort);

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

	//#1106: a short pulse on one slot; false when the slot has no pad.
	bool PlayTick(uint8_t gamepadPort);
};
