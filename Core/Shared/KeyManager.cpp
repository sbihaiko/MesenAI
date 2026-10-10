#include "pch.h"
#include "Shared/Interfaces/IKeyManager.h"
#include "Shared/KeyManager.h"
#include "Shared/EmuSettings.h"
#include "Shared/Emulator.h"
#include "Shared/Video/VideoDecoder.h"
#include "Shared/Video/VideoRenderer.h"

IKeyManager* KeyManager::_keyManager = nullptr;
MousePosition KeyManager::_mousePosition = { 0, 0 };
double KeyManager::_xMouseMovement;
double KeyManager::_yMouseMovement;
EmuSettings* KeyManager::_settings = nullptr;
SimpleLock KeyManager::_lock;
SimpleLock KeyManager::_injectedLock;
vector<uint16_t> KeyManager::_injectedKeys;

void KeyManager::RegisterKeyManager(IKeyManager* keyManager)
{
	_xMouseMovement = 0;
	_yMouseMovement = 0;
	_keyManager = keyManager;
}

void KeyManager::RefreshKeyState()
{
	if(_keyManager != nullptr) {
		return _keyManager->RefreshState();
	}
}

void KeyManager::SetSettings(EmuSettings* settings)
{
	_settings = settings;
}

bool KeyManager::IsKeyPressed(uint16_t keyCode)
{
	if(_keyManager != nullptr) {
		return _settings->IsInputEnabled() && _keyManager->IsKeyPressed(keyCode);
	}
	return false;
}

optional<int16_t> KeyManager::GetAxisPosition(uint16_t keyCode)
{
	if(_keyManager != nullptr && _settings->IsInputEnabled()) {
		return _keyManager->GetAxisPosition(keyCode);
	}
	return std::nullopt;
}

bool KeyManager::IsMouseButtonPressed(MouseButton button)
{
	if(_keyManager != nullptr) {
		return _settings->IsInputEnabled() && _keyManager->IsMouseButtonPressed(button);
	}
	return false;
}

//The GUI test hook's overlay (the GUI test hook ADR, PR #1202, item 4): codes a
//run asked to hold down, read as pressed on top of whatever the backend reports.
//Empty unless the host started with --test-hook, so a normal run never differs.
void KeyManager::SetInjectedKey(uint16_t keyCode, bool pressed)
{
	auto lock = _injectedLock.AcquireSafe();
	auto it = std::find(_injectedKeys.begin(), _injectedKeys.end(), keyCode);
	if(pressed && it == _injectedKeys.end()) {
		_injectedKeys.push_back(keyCode);
	} else if(!pressed && it != _injectedKeys.end()) {
		_injectedKeys.erase(it);
	}
}

vector<uint16_t> KeyManager::GetPressedKeys()
{
	vector<uint16_t> keys;
	if(_keyManager != nullptr) {
		//#902: a backend's set can carry the "no key" sentinel - macOS recorded it
		//for any key code its table cannot name, and SetKeyState is a host export
		//every backend accepts code 0 through. Filtered here, at the one place the
		//set leaves a backend, so the host, Lua's getPressedKeys and
		//ShortcutKeyHandler all read the same set: the shortcut handler, which
		//reads the set's non-emptiness and its size as a key being down, is the one
		//that cannot tell the sentinel from a key.
		keys = IKeyManager::WithoutNoKey(_keyManager->GetPressedKeys());
	}
	auto lock = _injectedLock.AcquireSafe();
	for(uint16_t injected : _injectedKeys) {
		if(std::find(keys.begin(), keys.end(), injected) == keys.end()) {
			keys.push_back(injected);
		}
	}
	return keys;
}

string KeyManager::GetKeyName(uint16_t keyCode)
{
	if(_keyManager != nullptr) {
		return _keyManager->GetKeyName(keyCode);
	}
	return "";
}

uint16_t KeyManager::GetKeyCode(string keyName)
{
	if(_keyManager != nullptr) {
		return _keyManager->GetKeyCode(keyName);
	}
	return 0;
}

void KeyManager::UpdateDevices()
{
	if(_keyManager != nullptr) {
		_keyManager->UpdateDevices();
	}
}

void KeyManager::SetMouseMovement(int16_t x, int16_t y)
{
	auto lock = _lock.AcquireSafe();
	_xMouseMovement += x;
	_yMouseMovement += y;
}

MouseMovement KeyManager::GetMouseMovement(Emulator* emu, uint32_t mouseSensitivity)
{
	constexpr double divider[10] = { 0.25, 0.33, 0.5, 0.66, 0.75, 1, 1.5, 2, 3, 4 };
	FrameInfo rendererSize = emu->GetVideoRenderer()->GetRendererSize();
	FrameInfo frameSize = emu->GetVideoDecoder()->GetFrameInfo();
	double scale = (double)rendererSize.Width / frameSize.Width;
	double factor = scale / divider[mouseSensitivity];

	MouseMovement mov = {};

	auto lock = _lock.AcquireSafe();
	mov.dx = (int16_t)(_xMouseMovement / factor);
	mov.dy = (int16_t)(_yMouseMovement / factor);
	_xMouseMovement -= (mov.dx * factor);
	_yMouseMovement -= (mov.dy * factor);

	return mov;
}

void KeyManager::SetMousePosition(Emulator* emu, double x, double y)
{
	if(x < 0 || y < 0) {
		_mousePosition.X = -1;
		_mousePosition.Y = -1;
		_mousePosition.RelativeX = -1;
		_mousePosition.RelativeY = -1;
	} else {
		OverscanDimensions overscan = emu->GetSettings()->GetOverscan();
		FrameInfo frame = emu->GetVideoDecoder()->GetBaseFrameInfo(true);
		_mousePosition.X = (int32_t)(x * frame.Width + overscan.Left);
		_mousePosition.Y = (int32_t)(y * frame.Height + overscan.Top);
		_mousePosition.RelativeX = x;
		_mousePosition.RelativeY = y;
	}
}

MousePosition KeyManager::GetMousePosition()
{
	return _mousePosition;
}

void KeyManager::SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	if(_keyManager != nullptr) {
		double intensity = _settings->GetInputConfig().ForceFeedbackIntensity;
		_keyManager->SetForceFeedback(magnitudeRight * intensity, magnitudeLeft * intensity);
	}
}

void KeyManager::SetForceFeedback(uint16_t magnitude)
{
	SetForceFeedback(magnitude, magnitude);
}
