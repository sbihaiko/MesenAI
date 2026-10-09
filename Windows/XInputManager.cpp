#include "Common.h"
#include "XInputManager.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/EmuSettings.h"

//The chain is per slot, and its slots are XInput's: a tick on the third pad must
//not touch the first, so the two counts are the same number on purpose.
static_assert(HapticTickChain::SlotCount == (uint32_t)XUSER_MAX_COUNT,
	"HapticTickChain::SlotCount must be XInput's XUSER_MAX_COUNT");

XInputManager::XInputManager(Emulator* emu)
{
	_emu = emu;
	InitializeCriticalSection(&_tickLock);
	//One queue for every tick timer. A queue that could not be created is not an
	//error here: PlayTick answers false, and the input tester is unaffected.
	_tickTimerQueue = CreateTimerQueue();
	for(int i = 0; i < XUSER_MAX_COUNT; i++) {
		_gamePadConnected[i] = true;
	}
}

XInputManager::~XInputManager()
{
	//INVALID_HANDLE_VALUE deletes the queue and every timer still in it, and waits
	//for a callback that is running right now, so nothing reaches _tickChain after
	//it is gone. The lock is deliberately not held across that wait.
	if(_tickTimerQueue != nullptr) {
		DeleteTimerQueueEx(_tickTimerQueue, INVALID_HANDLE_VALUE);
		_tickTimerQueue = nullptr;
	}
	DeleteCriticalSection(&_tickLock);
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
	//#1121: this is what a tick has to put back, both the game's rumble and the
	//input tester's - recorded before it is applied, and only from here: a tick's
	//own magnitude goes through ApplyVibration, or a tick would restore itself.
	EnterCriticalSection(&_tickLock);
	_tickChain.RecordApplied(gamepadPort, magnitudeRight, magnitudeLeft);
	LeaveCriticalSection(&_tickLock);
	ApplyVibration(gamepadPort, magnitudeRight, magnitudeLeft);
}

void XInputManager::ApplyVibration(uint8_t gamepadPort, uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	XINPUT_VIBRATION settings = {};
	settings.wRightMotorSpeed = magnitudeRight;
	settings.wLeftMotorSpeed = magnitudeLeft;
	XInputSetState(gamepadPort, &settings);
}

//The second parameter is the type's, not this function's: a tick timer is one-shot
//(due time, no period), so it only ever fires on its due date.
void CALLBACK XInputManager::TickElapsed(PVOID context, BOOLEAN)
{
	TickTimerContext* tick = (TickTimerContext*)context;

	//The tick's own handle, not the slot's newest one: a tick that a later tick
	//superseded must leave the pad to it, and the newer tick's timer is the one
	//that stops it (HapticTickChain::EndTick).
	EnterCriticalSection(&tick->Manager->_tickLock);
	optional<HapticTickChain::Magnitudes> restore = tick->Manager->_tickChain.EndTick(tick->Port, tick->Handle);
	LeaveCriticalSection(&tick->Manager->_tickLock);

	if(restore) {
		tick->Manager->ApplyVibration(tick->Port, restore->Right, restore->Left);
	}
}

bool XInputManager::PlayTick(uint8_t gamepadPort)
{
	if(!IsConnected(gamepadPort)) {
		return false;
	}

	//The tap goes out first: a tick whose timer cannot be armed is still a tick,
	//and the branch below puts the pad back rather than leaving it buzzing.
	ApplyVibration(gamepadPort, HapticTickChain::TickMagnitude, HapticTickChain::TickMagnitude);

	EnterCriticalSection(&_tickLock);
	uint32_t handle = _tickChain.BeginTick(gamepadPort);
	uint8_t contextIndex = (uint8_t)(_tickContextInUse[gamepadPort] ^ 1);
	_tickContextInUse[gamepadPort] = contextIndex;
	TickTimerContext& tick = _tickContexts[gamepadPort][contextIndex];
	tick.Manager = this;
	tick.Port = gamepadPort;
	tick.Handle = handle;

	//A timer of its own for every tick: re-arming a one-shot that has already
	//expired does not fire it again, and then nothing ends the tick - #1121 names
	//that as the way a pad is left buzzing. The handle the previous tick armed is
	//deleted below, which is also what keeps a slot from accumulating timers.
	HANDLE armedTimer = nullptr;
	bool armed = _tickTimerQueue != nullptr
		&& CreateTimerQueueTimer(&armedTimer, _tickTimerQueue, XInputManager::TickElapsed,
			&tick, HapticTickChain::TickDurationMs, 0, WT_EXECUTEINTIMERTHREAD) != FALSE;
	HANDLE previous = _tickTimers[gamepadPort];
	_tickTimers[gamepadPort] = armed ? armedTimer : nullptr;
	LeaveCriticalSection(&_tickLock);

	if(previous != nullptr) {
		//A pending timer is deleted without calling back; one that is already in its
		//callback is only marked, and its tick is superseded in the chain, so it
		//stops nothing.
		DeleteTimerQueueTimer(_tickTimerQueue, previous, nullptr);
	}

	if(!armed) {
		EnterCriticalSection(&_tickLock);
		optional<HapticTickChain::Magnitudes> restore = _tickChain.EndTick(gamepadPort, handle);
		LeaveCriticalSection(&_tickLock);
		if(restore) {
			ApplyVibration(gamepadPort, restore->Right, restore->Left);
		}
	}
	return armed;
}
