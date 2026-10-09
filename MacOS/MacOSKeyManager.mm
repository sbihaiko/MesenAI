#import <Foundation/Foundation.h>
#import <Cocoa/Cocoa.h>
#import <GameController/GameController.h>

#include <algorithm>
#include "MacOSKeyManager.h"
#include "MacOSGameController.h"
//The MacOS SDK defines a global function 'Debugger', colliding with Mesen's Debugger class
//Redefine it temporarily so the headers don't cause compilation errors due to this
#define Debugger MesenDebugger
#include "Shared/MessageManager.h"
#include "Shared/Emulator.h"
#include "Shared/EmuSettings.h"
#include "Shared/KeyDefinitions.h"
#include "Shared/KeyMonitorRouting.h"
#include "Shared/SettingTypes.h"
#undef Debugger

//#1080: the modifier families a host event carries, as the routing rule compares
//them against the families a binding names. Command is deliberately absent: the
//shared key table has no name for it, so no binding can hold one, and the monitor
//passes command chords through before the rule is asked (UI/Logic/OverlayKeyPress
//makes the same point for the window's side).
//
//Function and the numeric-pad flag are absent for the same reason: nothing in the
//config can name them, and a press carrying one is read as carrying nothing -
//which is also what Avalonia's KeyModifiers hands the window's twin rule.
static int PressModifierFamilies(NSEventModifierFlags flags)
{
	int families = KeyMonitorRouting::NoModifier;
	if((flags & NSEventModifierFlagShift) != 0) {
		families |= KeyMonitorRouting::ShiftModifier;
	}
	if((flags & NSEventModifierFlagControl) != 0) {
		families |= KeyMonitorRouting::ControlModifier;
	}
	if((flags & NSEventModifierFlagOption) != 0) {
		families |= KeyMonitorRouting::AltModifier;
	}
	return families;
}

MacOSKeyManager::MacOSKeyManager(Emulator* emu)
{
	_emu = emu;

	ResetKeyState();

	//The table is the backend's half of the contract AliasedKeyState owns: it is
	//partial (an entry left at 0 names no key) and many-to-one (four pairs of
	//keycodes share one Mesen code), so a host event is handed to that class
	//instead of being written into _keyState at the Mesen code it maps to.
	for(uint32_t rawCode = 0; rawCode < AliasedKeyState::RawCodeCount; rawCode++) {
		_hostKeyState.SetMapping(rawCode, _keyCodeMap[rawCode]);
	}

	_keyDefinitions = KeyDefinition::GetSharedKeyDefinitions();

	vector<string> buttonNames = {
		"A", "B", "X", "Y", "L1", "R1", "Start", "Select",
		"Up", "Down", "Left", "Right", "L2", "R2", "L3", "R3",
		"X+", "X-", "Y+", "Y-", "X2+", "X2-", "Y2+", "Y2-",
		"X", "Y", "X2", "Y2"
	};

	for(int i = 0; i < 20; i++) {
		for(int j = 0; j < (int)buttonNames.size(); j++) {
			_keyDefinitions.push_back({ "Pad" + std::to_string(i + 1) + " " + buttonNames[j], (uint32_t)(MacOSKeyManager::BaseGamepadIndex + i * 0x100 + j) });
		}
	}

	for(KeyDefinition &keyDef : _keyDefinitions) {
		_keyNames[keyDef.keyCode] = keyDef.name;
		_keyCodes[keyDef.name] = keyDef.keyCode;
	}

	_disableAllKeys = false;

	// On some versions of macOS, there is an assert in the GameController code that seems to verify
	// that the GCController is getting accessed on the main thread. Users are reporting this issue
	// only on older versions of intel MacOS, so it's hard to be 100% certain, but this reliably fixes
	// the issue in my testing.
	dispatch_async(dispatch_get_main_queue(), ^{
		// On start up, load the list of existing controllers and add them.
		// The callbacks only add controllers that are connected after launch.
		for(GCController* controller in [GCController controllers]) {
			AddController(controller);
		}
		if(@available(macOS 11.3, *)) {
			GCController.shouldMonitorBackgroundEvents = YES;
		}
	});

	//#1080: the six keyboard keys the config can name as a modifier, in the shared
	//table's own codes (Shared/KeyDefinitions.h: 116-117 Shift, 118-119 Ctrl,
	//120-121 Alt - the same codes HandleModifiers writes into _keyState).
	const KeyMonitorRouting::ModifierKeyCodes modifierKeys = { 116, 117, 118, 119, 120, 121 };

	NSEventMask eventMask = NSEventMaskKeyDown | NSEventMaskKeyUp | NSEventMaskFlagsChanged;

	_eventMonitor = [NSEvent addLocalMonitorForEventsMatchingMask:eventMask handler:^ NSEvent* (NSEvent* event) {
		//#1080: what this handler returns is who gets the key. `nil` discards the
		//event and publishes it to the emulator's key state; the event itself is
		//dispatched by AppKit as usual, which is the only way a key reaches the
		//Avalonia window - Avalonia.Native installs no monitor of its own, so a key
		//consumed here never reaches OnPreviewKeyDown. The overlay's own press is
		//the UI's to answer (its arm is MainWindow.HandleEscInTheUi, and Esc in Play
		//is the overlay's by decision), and it was being swallowed here, which is
		//the reported repro: the press never arrived at the window that answers it.
		//
		//The decision is host-free and unit-tested (Core/Shared/KeyMonitorRouting.h,
		//scripts/core_unit_tests.cpp Bloco U). The binding comes from the settings
		//the config already pushed into the core - key sets 0 and 1, the two
		//keyboard bindings; the pad slot is a third set and carries pad codes, which
		//no host keyboard event maps to (GetShortcutKey bounds-checks the index, so
		//neither call can read past the sets a config may fill).
		//
		//Everything that is not the overlay's press goes to the core exactly as it
		//did before, and a press the window does not answer is handed back to the
		//core there (MainWindow), so the arms the core owns keep working. The
		//overlay's press is the UI's only while MainWindow - the window holding
		//that arm - is the one with the keyboard: `InBackground` is false whenever
		//*any* window of the app is active, and the press returned there would go
		//to a Settings dialog or a tool window that has no arm for it.
		//
		//A key-up is sent the way its own key-down went, remembered per raw host
		//code (#1080): the rule decides one event from the modifiers that event
		//carries, and a modifier released or pressed between a press and its
		//release would otherwise send the two halves of one key to different
		//owners - the core never seeing the release, or the window never seeing it,
		//either way a key left held.
		NSEventType type = [event type];
		bool isFlagsChanged = type == NSEventTypeFlagsChanged;
		bool isKeyDown = type == NSEventTypeKeyDown;
		uint32_t rawCode = isFlagsChanged ? 0 : (uint32_t) [event keyCode];
		uint16_t mappedKeyCode = rawCode < AliasedKeyState::RawCodeCount ? _keyCodeMap[rawCode] : 0;

		EmuSettings* settings = _emu->GetSettings();
		KeyMonitorRouting::Route route;
		if(isFlagsChanged) {
			//A modifier of its own carries no key of the table's (rawCode is 0
			//above), so it is never the overlay's press and there is no pair to
			//keep: the rule answers it, as it always did.
			route = KeyMonitorRouting::For(
				settings->CheckFlag(EmulationFlags::InBackground),
				false,
				false,
				settings->CheckFlag(EmulationFlags::MainWindowIsKey));
		} else if(isKeyDown) {
			route = _downRoutes.Down(rawCode, KeyMonitorRouting::For(
				settings->CheckFlag(EmulationFlags::InBackground),
				([event modifierFlags] & NSEventModifierFlagCommand) != 0,
				KeyMonitorRouting::IsTheOverlaysPress(mappedKeyCode, PressModifierFamilies([event modifierFlags]),
					settings->GetShortcutKey(EmulatorShortcut::ToggleOverlay, 0),
					settings->GetShortcutKey(EmulatorShortcut::ToggleOverlay, 1),
					modifierKeys),
				settings->CheckFlag(EmulationFlags::MainWindowIsKey)));
		} else {
			route = _downRoutes.Up(rawCode);
		}

		if(route == KeyMonitorRouting::Route::LeaveItToTheUi) {
			return event;
		}

		if(isFlagsChanged) {
			HandleModifiers((uint32_t) [event modifierFlags]);
		} else {
			//AliasedKeyState answers this line both ways it used to be wrong. It
			//refuses a virtual key code its table cannot name - the codes with no
			//Mesen key and every code >= 128, outside the range the table covers at
			//all - so the "no key" sentinel (#902) is never recorded here; and it
			//counts the host codes behind a Mesen code, so a slot four pairs of
			//them share drops only on the last release (#904).
			//
			//The sentinel has a second way in that this class cannot close: the
			//host export InputApi.SetKeyState accepts code 0 on every backend. That
			//one is answered at the interface, in IKeyManager::WithoutNoKey.
			//
			//Which physical keys reach this line with such a code is not something
			//this repo can settle: the Fn key arrives as FlagsChanged (the branch
			//above) and media keys arrive as SystemDefined, which the event mask
			//does not subscribe to.
			_hostKeyState.SetKeyState(rawCode, type == NSEventTypeKeyDown);
		}

		return nil;
	}];

	_connectObserver = [[NSNotificationCenter defaultCenter] addObserverForName:GCControllerDidConnectNotification object:nil queue:nil usingBlock:^ void (NSNotification* notification) {
		GCController* controller = (GCController*) [notification object];
		AddController(controller);
	}];

	_disconnectObserver = [[NSNotificationCenter defaultCenter] addObserverForName:GCControllerDidDisconnectNotification object:nil queue:nil usingBlock:^ void (NSNotification* notification) {
		GCController* controller = (GCController*) [notification object];

		int indexToRemove = -1;
		for(int i = 0; i < _controllers.size(); i++) {
			if(_controllers[i]->IsGameController(controller)) {
				indexToRemove = i;
				break;
			}
		}

		if(indexToRemove >= 0) {
			_controllers.erase(_controllers.begin() + indexToRemove);
			MessageManager::Log("[Input Device] Disconnected");
		}
	}];
}

MacOSKeyManager::~MacOSKeyManager()
{
	[NSEvent removeMonitor:(id) _eventMonitor];
	[[NSNotificationCenter defaultCenter] removeObserver:(id) _connectObserver];
	[[NSNotificationCenter defaultCenter] removeObserver:(id) _disconnectObserver];
}

void MacOSKeyManager::AddController(void* cont)
{
	GCController* controller = static_cast<GCController*>(cont);
	//W-P15 (#912): a pad is a pad whatever profile it exposes. A micro/basic
	//controller has no extended gamepad and used to be dropped here, so it
	//never got a slot, sent no key, never raised the unknown-pad pill and could
	//not be set up. Its own profile's elements drive the same bits (see
	//MacOSGameController), which is what the pill and the setup sheet read.
	if(!MacOSGameController::Supports(controller)) {
		MessageManager::Log(std::string("[Input] Device ignored (No gamepad profile) - Name: ") + [[controller vendorName] UTF8String]);
	} else {
		_controllers.push_back(std::shared_ptr<MacOSGameController>(new MacOSGameController(_emu, controller)));
		MessageManager::Log(std::string("[Input Connected] Name: ") + [[controller vendorName] UTF8String]);
	}
}

void MacOSKeyManager::HandleModifiers(uint32_t flags)
{
	_keyState[116] = (flags & NX_DEVICELSHIFTKEYMASK) != 0; //Left shift
	_keyState[117] = (flags & NX_DEVICERSHIFTKEYMASK) != 0; //Right shift
	_keyState[118] = (flags & NX_DEVICELCTLKEYMASK) != 0; //Left ctrl
	_keyState[119] = (flags & NX_DEVICERCTLKEYMASK) != 0; //Right ctrl
	_keyState[120] = (flags & NX_DEVICELALTKEYMASK) != 0; //Left alt/option
	_keyState[121] = (flags & NX_DEVICERALTKEYMASK) != 0; //Right alt/option
	_keyState[70] = (flags & NX_DEVICELCMDKEYMASK) != 0; //Left cmd
	_keyState[71] = (flags & NX_DEVICERCMDKEYMASK) != 0; //Right cmd
}

void MacOSKeyManager::RefreshState()
{
	//TODO: NOT IMPLEMENTED YET
	//Only needed to detect poll controller input
}

bool MacOSKeyManager::IsKeyPressed(uint16_t key)
{
	if(_disableAllKeys || key == 0) {
		return false;
	}

	if(key >= MacOSKeyManager::BaseGamepadIndex) {
		uint8_t gamepadPort = (key - MacOSKeyManager::BaseGamepadIndex) / 0x100;
		uint8_t gamepadButton = (key - MacOSKeyManager::BaseGamepadIndex) % 0x100;
		if(_controllers.size() > gamepadPort) {
			return _controllers[gamepadPort]->IsButtonPressed(gamepadButton);
		}
	} else if(key < 0x205) {
		return _keyState[key] != 0 || _hostKeyState.IsPressed(key);
	}
	return false;
}

optional<int16_t> MacOSKeyManager::GetAxisPosition(uint16_t key)
{
	if(key >= MacOSKeyManager::BaseGamepadIndex) {
		uint8_t port = (key - MacOSKeyManager::BaseGamepadIndex) / 0x100;
		uint8_t button = (key - MacOSKeyManager::BaseGamepadIndex) % 0x100;
		if(_controllers.size() > port) {
			return _controllers[port]->GetAxisPosition(button);
		}
	}
	return std::nullopt;
}

bool MacOSKeyManager::IsMouseButtonPressed(MouseButton button)
{
	return _keyState[MacOSKeyManager::BaseMouseButtonIndex + (int)button];
}

vector<uint16_t> MacOSKeyManager::GetPressedKeys()
{
	vector<uint16_t> pressedKeys;
	for(size_t i = 0; i < _controllers.size(); i++) {
		for(int j = 0; j < 24; j++) {
			if(_controllers[i]->IsButtonPressed(j)) {
				pressedKeys.push_back(MacOSKeyManager::BaseGamepadIndex + i * 0x100 + j);
			}
		}
	}

	//From 1: slot 0 is "no key" - no host reader names it (UI/Logic/
	//PressedKeys.Decode and StateGrid both skip a code of 0), so reporting it
	//only ever made this list longer than what the host could see (#902).
	for(int i = 1; i < 0x205; i++) {
		if(_keyState[i] || _hostKeyState.IsPressed((uint16_t)i)) {
			pressedKeys.push_back((uint16_t)i);
		}
	}
	return pressedKeys;
}

string MacOSKeyManager::GetKeyName(uint16_t key)
{
	auto keyDef = _keyNames.find(key);
	if(keyDef != _keyNames.end()) {
		return keyDef->second;
	}
	return "";
}

uint16_t MacOSKeyManager::GetKeyCode(string keyName)
{
	auto keyDef = _keyCodes.find(keyName);
	if(keyDef != _keyCodes.end()) {
		return keyDef->second;
	}
	return 0;
}

void MacOSKeyManager::UpdateDevices()
{
	//TODO: NOT IMPLEMENTED YET
	//Only needed to detect newly plugged in devices
}

bool MacOSKeyManager::SetKeyState(uint16_t scanCode, bool state)
{
	if(scanCode < 0x205 && _keyState[scanCode] != state) {
		_keyState[scanCode] = state;
		return true;
	}
	return false;
}

void MacOSKeyManager::ResetKeyState()
{
	memset(_keyState, 0, sizeof(_keyState));
	_hostKeyState.Reset();
	//Nothing is held, and nothing is the UI's either: a release arriving after
	//this with no press of its own is the core's, as every key was before #1080.
	_downRoutes.Reset();
}

void MacOSKeyManager::SetDisabled(bool disabled)
{
	_disableAllKeys = disabled;
}

void MacOSKeyManager::SetForceFeedback(uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	for(auto& controller : _controllers) {
		controller->SetForceFeedback(magnitudeRight, magnitudeLeft);
	}
}

uint32_t MacOSKeyManager::GetConnectedGamepadCount()
{
	return (uint32_t)_controllers.size();
}

bool MacOSKeyManager::GetGamepadInfo(uint32_t index, GamepadInfo& info)
{
	if(index >= _controllers.size()) {
		return false;
	}
	info.Name = _controllers[index]->GetName();
	//The GameController framework does not expose VID/PID - leave them as 0
	info.VendorId = 0;
	info.ProductId = 0;
	info.Slot = index;
	info.HasRumble = _controllers[index]->HasRumble();
	info.Backend = GamepadBackend::GameController;
	return true;
}

bool MacOSKeyManager::GetGamepadState(uint32_t index, GamepadState& state)
{
	if(index >= _controllers.size()) {
		return false;
	}
	for(int j = 0; j < 24; j++) {
		if(_controllers[index]->IsButtonPressed(j)) {
			state.Buttons |= (1u << j);
		}
	}
	for(int a = 0; a < 4; a++) {
		std::optional<int16_t> axis = _controllers[index]->GetAxisPosition(a);
		state.Axes[a] = axis ? *axis : 0;
	}
	return true;
}

void MacOSKeyManager::TestForceFeedback(uint32_t index, uint16_t magnitudeRight, uint16_t magnitudeLeft)
{
	if(index < _controllers.size()) {
		_controllers[index]->SetForceFeedback(magnitudeRight, magnitudeLeft);
	}
}

bool MacOSKeyManager::PlayGamepadTick(uint32_t index)
{
	return index < _controllers.size() && _controllers[index]->PlayTick();
}

bool MacOSKeyManager::SetGamepadLight(uint32_t index, uint8_t r, uint8_t g, uint8_t b)
{
	return index < _controllers.size() && _controllers[index]->SetLight(r, g, b);
}
