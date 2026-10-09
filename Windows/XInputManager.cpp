#include "Common.h"
#include "XInputManager.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/EmuSettings.h"

XInputManager::XInputManager(Emulator* emu)
{
	_emu = emu;
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		_gamePadConnected[i] = true;
	}
}

XInputManager::~XInputManager()
{
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		if(_tickTimers[i].Handle) {
			//Blocks until a running callback returns, so none outlives this object.
			DeleteTimerQueueTimer(nullptr, _tickTimers[i].Handle, INVALID_HANDLE_VALUE);
		}
	}
}

VOID CALLBACK XInputManager::OnTickExpired(PVOID context, BOOLEAN timerOrWaitFired)
{
	TickTimer* timer = (TickTimer*)context;
	timer->Owner->EndTick(timer->Slot);
}

void XInputManager::EndTick(uint8_t gamepadPort)
{
	std::lock_guard<std::mutex> lock(_rumbleLock);
	if(_tickStopAt[gamepadPort] == 0) {
		return;
	}
	_tickStopAt[gamepadPort] = 0;
	XINPUT_VIBRATION restore = {};
	restore.wRightMotorSpeed = _desiredRumble[gamepadPort].Right;
	restore.wLeftMotorSpeed = _desiredRumble[gamepadPort].Left;
	XInputSetState(gamepadPort, &restore);
}

void XInputManager::RefreshState()
{
	XINPUT_STATE state;
	for(DWORD i = 0; i < XUSER_MAX_COUNT; i++) {
		if(_gamePadConnected[i]) {
			if(XInputGetState(i, &state) != ERROR_SUCCESS) {
				//XInputGetState is incredibly slow when no controller is plugged in
				ZeroMemory(&_gamePadStates[i], sizeof(XINPUT_STATE));
				_gamePadConnected[i] = false;
			} else {
				_gamePadStates[i] = state;
			}
		}
	}
}

bool XInputManager::NeedToUpdate()
{
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		if(!_gamePadConnected[i]) {
			XINPUT_STATE state;
			if(XInputGetState(i, &state) == ERROR_SUCCESS) {
				return true;
			}
		}
	}
	return false;
}

void XInputManager::UpdateDeviceList()
{
	//Periodically detect if a controller has been plugged in to allow controllers to be plugged in after the emu is started
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		_gamePadConnected[i] = true;
	}
}

bool XInputManager::IsPressed(uint8_t gamepadPort, uint8_t button)
{
	if(_gamePadConnected[gamepadPort]) {
		XINPUT_GAMEPAD& gamepad = _gamePadStates[gamepadPort].Gamepad;
		bool pressed = false;
		if(button <= 16) {
			WORD xinputButton = 1 << (button - 1);
			pressed = (_gamePadStates[gamepadPort].Gamepad.wButtons & xinputButton) != 0;
		} else {
			//ADR-0255 slice 4 deliberately leaves this backend's magnitudes alone.
			//An XInput stick direction is named as a *button* here - "Pad1 RT Up",
			//"Pad1 LT Right" (WindowsKeyManager's buttonNames) - so
			//PadAxisAction.IsAxisDirectionName never recognises one and no
			//shortcut's spare binding can name a direction in this family: there is
			//nothing for a player-set axis threshold to govern. DirectInput, whose
			//directions *are* named "Joy1 Y+" / "Joy1 Y-", is where the threshold
			//lands on Windows (DirectInputManager::AxisThresholdRange), and the
			//trigger pair at 17/18 is 8-bit with no full-travel scale a percentage
			//could be measured against. Windows is not built or run on the macOS
			//machine slice 4 was written on; CI compiles it.
			double ratio = _emu->GetSettings()->GetControllerDeadzoneRatio() * 2;

			switch(button) {
				case 17: pressed = gamepad.bLeftTrigger > (XINPUT_GAMEPAD_TRIGGER_THRESHOLD * ratio); break;
				case 18: pressed = gamepad.bRightTrigger > (XINPUT_GAMEPAD_TRIGGER_THRESHOLD * ratio); break;
				case 19: pressed = gamepad.sThumbRY > (XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE * ratio); break;
				case 20: pressed = gamepad.sThumbRY < -(XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE * ratio); break;
				case 21: pressed = gamepad.sThumbRX < -(XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE * ratio); break;
				case 22: pressed = gamepad.sThumbRX > (XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE * ratio); break;
				case 23: pressed = gamepad.sThumbLY > (XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE * ratio); break;
				case 24: pressed = gamepad.sThumbLY < -(XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE * ratio); break;
				case 25: pressed = gamepad.sThumbLX < -(XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE * ratio); break;
				case 26: pressed = gamepad.sThumbLX > (XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE * ratio); break;
			}
		}

		_enableForceFeedback[gamepadPort] |= pressed;

		return pressed;
	}
	return false;
}

optional<int16_t> XInputManager::GetAxisPosition(uint8_t port, int axis)
{
	if(_gamePadConnected[port]) {
		XINPUT_GAMEPAD& gamepad = _gamePadStates[port].Gamepad;
		switch(axis - 27) {
			case 0: return gamepad.sThumbLY;
			case 1: return gamepad.sThumbLX;
			case 2: return gamepad.sThumbRY;
			case 3: return gamepad.sThumbRX;
			case 4: return gamepad.bLeftTrigger;
			case 5: return gamepad.bRightTrigger;
		}
	}

	return std::nullopt;
}

void XInputManager::SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		if(_enableForceFeedback[i]) {
			SetForceFeedback(i, magnitudeRight, magnitudeLeft);
		}
	}
}

bool XInputManager::IsConnected(uint8_t gamepadPort)
{
	return gamepadPort < XUSER_MAX_COUNT && _gamePadConnected[gamepadPort];
}

void XInputManager::SetForceFeedback(uint8_t gamepadPort, uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	if(gamepadPort >= XUSER_MAX_COUNT || !_gamePadConnected[gamepadPort]) {
		return;
	}
	std::lock_guard<std::mutex> lock(_rumbleLock);
	_desiredRumble[gamepadPort].Right = magnitudeRight;
	_desiredRumble[gamepadPort].Left = magnitudeLeft;
	if(_tickStopAt[gamepadPort] != 0) {
		//A tick is playing; EndTick restores this request when it ends.
		return;
	}
	XINPUT_VIBRATION settings = {};
	settings.wRightMotorSpeed = magnitudeRight;
	settings.wLeftMotorSpeed = magnitudeLeft;
	XInputSetState(gamepadPort, &settings);
}

bool XInputManager::PlayTick(uint8_t gamepadPort)
{
	if(!IsConnected(gamepadPort)) {
		return false;
	}
	std::lock_guard<std::mutex> lock(_rumbleLock);
	XINPUT_VIBRATION tick = {};
	tick.wRightMotorSpeed = 0x6000;
	tick.wLeftMotorSpeed = 0x6000;
	if(XInputSetState(gamepadPort, &tick) != ERROR_SUCCESS) {
		return false;
	}

	//One-shot 40 ms timer per slot, created on first use and re-armed after that.
	TickTimer& timer = _tickTimers[gamepadPort];
	bool armed;
	if(timer.Handle) {
		armed = ChangeTimerQueueTimer(nullptr, timer.Handle, 40, 0) != 0;
	} else {
		timer.Owner = this;
		timer.Slot = gamepadPort;
		armed = CreateTimerQueueTimer(&timer.Handle, nullptr, OnTickExpired, &timer, 40, 0, WT_EXECUTEONLYONCE) != 0;
	}
	if(!armed) {
		//No timer, no way to end the pulse: put the requested rumble straight back.
		XINPUT_VIBRATION restore = {};
		restore.wRightMotorSpeed = _desiredRumble[gamepadPort].Right;
		restore.wLeftMotorSpeed = _desiredRumble[gamepadPort].Left;
		XInputSetState(gamepadPort, &restore);
		return false;
	}
	_tickStopAt[gamepadPort] = GetTickCount64() + 40;
	return true;
}
