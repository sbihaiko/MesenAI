#pragma once
#include <unordered_map>
#include <vector>
#include "Shared/Interfaces/IKeyManager.h"
#include "Shared/KeyDefinitions.h"
#include "Shared/AliasedKeyState.h"
#include "Shared/KeyMonitorRouting.h"

class MacOSGameController;
class Emulator;

class MacOSKeyManager : public IKeyManager
{
private:
	Emulator* _emu;
	std::vector<shared_ptr<MacOSGameController>> _controllers;

	vector<KeyDefinition> _keyDefinitions;
	//Keys written by code, not by a host event: the modifier flags, the mouse
	//buttons, and SetKeyState's remap/test writes.
	bool _keyState[0x205];
	//Keys a host keyboard event published, through _keyCodeMap. The table is
	//partial and many-to-one, so the events are translated there rather than
	//written into _keyState directly (#902, #904).
	AliasedKeyState _hostKeyState;
	//#1080: which raw host codes' key-downs were handed to the UI, so their
	//key-up goes the same way whatever modifiers it is released with
	//(KeyMonitorRouting::DownRoutes).
	KeyMonitorRouting::DownRoutes _downRoutes;
	std::unordered_map<uint16_t, string> _keyNames;
	std::unordered_map<string, uint16_t> _keyCodes;

	bool _disableAllKeys;

	void* _eventMonitor;
	void* _connectObserver;
	void* _disconnectObserver;

	//Mapping of MacOS keycodes to Avalonia keycodes
	uint16_t _keyCodeMap[128] = {
		 44,  62,  47,  49,  51,  50,  69,  67,  46,  65,
		154,  45,  60,  66,  48,  61,  68,  63,  35,  36,
		 37,  38,  40,  39, 141,  43,  41, 143,  42,  34,
		151,  58,  64, 149,  52,  59,   6,  55,  53, 152,
		 54, 140, 150, 142, 145,  57,  56, 144,   3,  18,
		146,   2,   6,  13,  71,  70, 116,   8, 120, 118,
		117, 121, 119,   0, 106,  88,   0,  84,   0,  85,
		  0,   5, 131, 130, 129,  89, 511,   0,  87, 107,
		108, 141,  74,  75,  76,  77,  78,  79,  80,  81,
		109,  82,  83, 150, 154, 148,  94,  95,  96,  92,
		 97,  98,  12, 100,   9, 102, 105, 103,   0,  99,
		 72, 101,   0, 104,  31,  22,  19,  32,  93,  21,
		 91,  20,  90,  23,  25,  26,  24,   0
	};

	void HandleModifiers(uint32_t flags);

	void AddController(void* controller);

public:
	MacOSKeyManager(Emulator* emu);
	virtual ~MacOSKeyManager();

	void RefreshState() override;
	bool IsKeyPressed(uint16_t key) override;
	optional<int16_t> GetAxisPosition(uint16_t key) override;
	bool IsMouseButtonPressed(MouseButton button) override;
	std::vector<uint16_t> GetPressedKeys() override;
	string GetKeyName(uint16_t key) override;
	uint16_t GetKeyCode(string keyName) override;

	void UpdateDevices() override;
	bool SetKeyState(uint16_t scanCode, bool state) override;
	void ResetKeyState() override;

	void SetDisabled(bool disabled) override;

	void SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft) override;

	//Host input tester (PRD slice I.0)
	uint32_t GetConnectedGamepadCount() override;
	bool GetGamepadInfo(uint32_t index, GamepadInfo& info) override;
	bool GetGamepadState(uint32_t index, GamepadState& state) override;
	void TestForceFeedback(uint32_t index, uint16_t magnitudeRight, uint16_t magnitudeLeft) override;
	bool SetGamepadLight(uint32_t index, uint8_t r, uint8_t g, uint8_t b) override;
	bool PlayGamepadTick(uint32_t index) override;
};
