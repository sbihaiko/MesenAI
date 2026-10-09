#pragma once

#include "pch.h"
#include "Common.h"
#include <Xinput.h>

class Emulator;

class XInputManager
{
private:
	Emulator* _emu = nullptr;
	XINPUT_STATE _gamePadStates[XUSER_MAX_COUNT] = {};
	uint8_t _gamePadConnected[XUSER_MAX_COUNT] = {};
	bool _enableForceFeedback[XUSER_MAX_COUNT] = {};
	//#1106: when a menu tick's motors must be switched off again, 0 = no tick
	//running. XInput has no timed effect, so RefreshState ends the pulse.
	ULONGLONG _tickStopAt[XUSER_MAX_COUNT] = {};
	//The rumble the game or the tester last asked for, per slot; a tick ends by
	//restoring it, not by silencing the pad.
	struct { uint16_t Right; uint16_t Left; } _desiredRumble[XUSER_MAX_COUNT] = {};

public:
	XInputManager(Emulator* emu);

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
