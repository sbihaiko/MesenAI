#pragma once
#include "pch.h"

enum class MouseButton
{
	LeftButton = 0,
	RightButton = 1,
	MiddleButton = 2,
	Button4 = 3,
	Button5 = 4
};

struct MousePosition
{
	int16_t X;
	int16_t Y;
	double RelativeX;
	double RelativeY;
};

struct MouseMovement
{
	int16_t dx;
	int16_t dy;
};

enum class GamepadBackend : uint8_t
{
	None = 0,
	XInput = 1,
	DirectInput = 2,
	Evdev = 3,
	GameController = 4
};

//Host input tester (PRD slice I.0): one connected pad as the host OS sees it,
//independent of the emulated PadN mapping - name, which input backend it came
//through, its slot, VID/PID (0 when the backend does not expose them) and
//whether it can rumble.
struct GamepadInfo
{
	std::string Name;
	//0 when the backend does not expose them, which is not an edge case: macOS
	//(GameController) and Windows XInput both report 0:0000 for every pad, so a
	//reader that keys on VID/PID has no identity there. See ADR-0255's second
	//correction.
	uint32_t VendorId = 0;
	uint32_t ProductId = 0;
	//The pad's slot WITHIN ITS BACKEND's own numbering - an XInput slot, a
	//DirectInput ordinal, an index into the GameController or evdev list - which
	//is the `device` a mapping's key code carries (base + device * 0x100 +
	//button). It is deliberately NOT the ordinal `GetGamepadInfo` was handed:
	//Windows enumerates the four XInput slots and only then the joysticks, so
	//that ordinal is global. Issue #813 was Slot carrying the global one for
	//DirectInput; ShortcutKeyRules::DirectInputDeviceOf is the rule.
	uint32_t Slot = 0;
	bool HasRumble = false;
	GamepadBackend Backend = GamepadBackend::None;
};

//Host input tester (PRD slice I.0): the pad's raw state - a bitmask of its
//pressed buttons (bit i = button i, same numbering as the PadN key space) and
//its analog axes (raw int16, centered at 0: left X/Y then right X/Y).
struct GamepadState
{
	uint32_t Buttons = 0;
	int16_t Axes[4] = {};
};

class IKeyManager
{
public:
	//"No key": the value an empty KeyCombination slot has, and the one code a
	//pressed set must never carry. It is not a key - it is what "nothing here"
	//is spelled with, so a reader that takes it for one reads a key that is not
	//there.
	//
	//#902: macOS answers it for every virtual key code its table has no Mesen
	//key for and records the answer, so the sentinel entered a pressed set. The
	//set reached the host, Lua's getPressedKeys and ShortcutKeyHandler, and each
	//one had to know: the host filters it in PressedKeys.Decode, Lua drops it by
	//accident (code 0's key name is the empty string), and the shortcut handler
	//does not drop it at all - its non-emptiness is "a key is down" and its size
	//is a press or a release. SetKeyState, a host export, is the other way in: it
	//accepts 0 on every backend. WithoutNoKey is the one filter, so no reader has
	//to know the sentinel exists.
	static constexpr uint16_t NoKey = 0;

	static constexpr int BaseMouseButtonIndex = 0x200;
	static constexpr int BaseGamepadIndex = 0x1000;
	//Windows is the only backend with a second pad family: XInput above, and
	//DirectInput joysticks here. It lives in the shared key-code namespace rather
	//than in WindowsKeyManager because the pad rule in ShortcutKeyRules has to
	//tell the two apart - the families number their buttons independently, so the
	//same button byte is "Start" in one and an axis direction in the other.
	static constexpr int BaseDirectInputIndex = 0x2000;

	//The pressed set a backend reports, with the sentinel removed. The backend's
	//order is kept: ShortcutKeyHandler compares two reads of this list position
	//by position.
	static vector<uint16_t> WithoutNoKey(const vector<uint16_t>& keyCodes)
	{
		vector<uint16_t> keysOut;
		keysOut.reserve(keyCodes.size());
		for(uint16_t keyCode : keyCodes) {
			if(keyCode != NoKey) {
				keysOut.push_back(keyCode);
			}
		}
		return keysOut;
	}

	virtual ~IKeyManager() {}

	virtual void RefreshState() = 0;
	virtual void UpdateDevices() = 0;
	virtual bool IsMouseButtonPressed(MouseButton button) = 0;
	virtual bool IsKeyPressed(uint16_t keyCode) = 0;
	virtual optional<int16_t> GetAxisPosition(uint16_t keyCode) { return std::nullopt; }
	virtual vector<uint16_t> GetPressedKeys() = 0;
	virtual string GetKeyName(uint16_t keyCode) = 0;
	virtual uint16_t GetKeyCode(string keyName) = 0;

	virtual bool SetKeyState(uint16_t scanCode, bool state) = 0;
	virtual void ResetKeyState() = 0;
	virtual void SetDisabled(bool disabled) = 0;

	virtual void SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft) {}

	//Host input tester (PRD slice I.0). Defaults are no-ops so a platform that
	//has not implemented the tester still builds; the Input tester tab only
	//lists pads the active backend actually reports.
	virtual uint32_t GetConnectedGamepadCount() { return 0; }
	virtual bool GetGamepadInfo(uint32_t index, GamepadInfo& info) { return false; }
	virtual bool GetGamepadState(uint32_t index, GamepadState& state) { return false; }
	virtual void TestForceFeedback(uint32_t index, uint16_t magnitudeRight, uint16_t magnitudeLeft) {}
};