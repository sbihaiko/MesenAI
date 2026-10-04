#pragma once
#include "Shared/Interfaces/IKeyManager.h"

//ADR-0255 slice 1 correction: the Play Controller sheet (UI/Views/
//PlayerControllerSheetView.axaml, UI/Logic/ControllerSheet.cs) draws W-P15's
//console pad and lights each key from the pad's own GamepadState.Buttons - and
//that bitmask's button order is *per backend*. A hand-written C# table can only
//be right for one of them, and the one the sheet shipped with (macOS/GameController)
//meant that on Windows - a shipped target - the sheet's A key (bit 0) lit from
//the XInput D-pad Up and its Start key from a stick click, with no test able to
//see it.
//
//This header carries that order for the core: the ten console keys, host-free so
//scripts/core_unit_tests.cpp can pin its own BitOf to them without compiling the
//Windows arm (the pattern Core/Shared/ShortcutKeyRules.h uses). Its rows are
//regular enough that UI.HeadlessTests/ControllerSheetPadTests parses them back and
//pins them to the per-backend order the sheet really reads
//(UI/Logic/ControllerSheet.cs), and that order is read off the three backends' own
//key tables by Every_backends_key_table_matches_ControllerLivePad - so a value
//transcribed wrongly here is caught against the backends through that chain:
//  - MacOS/MacOSGameController.mm + MacOS/MacOSKeyManager.mm
//  - Windows/XInputManager.cpp + Windows/WindowsKeyManager.cpp
//  - Linux/LinuxGameController.cpp + Linux/LinuxKeyManager.cpp
namespace GamepadButtonOrder
{
	//The console pad the sheet draws, named after SetupButton (UI/Logic/
	//PlayControllerSetup.cs) so a row parses straight into a sheet key.
	enum class PadButton { Up, Down, Left, Right, Select, Start, B, A, L, R };

	//One row per (backend, button, bit): the GamepadState.Buttons bit the backend
	//reports the button at.
	struct PadButtonBit
	{
		GamepadBackend Backend;
		PadButton Button;
		int Bit;
	};

	inline const vector<PadButtonBit>& Table()
	{
		static const vector<PadButtonBit> table = {
			//macOS / GameController (MacOSGameController.mm): _buttonState[j] is
			//the controller's own button, and MacOSKeyManager sets bit j from it -
			//A 0, B 1, X 2, Y 3, LB 4, RB 5, Menu 6 ("Start"), Options 7
			//("Select"), D-pad 8..11.
			{ GamepadBackend::GameController, PadButton::A, 0 },
			{ GamepadBackend::GameController, PadButton::B, 1 },
			{ GamepadBackend::GameController, PadButton::L, 4 },
			{ GamepadBackend::GameController, PadButton::R, 5 },
			{ GamepadBackend::GameController, PadButton::Start, 6 },
			{ GamepadBackend::GameController, PadButton::Select, 7 },
			{ GamepadBackend::GameController, PadButton::Up, 8 },
			{ GamepadBackend::GameController, PadButton::Down, 9 },
			{ GamepadBackend::GameController, PadButton::Left, 10 },
			{ GamepadBackend::GameController, PadButton::Right, 11 },

			//Windows XInput (XInputManager.cpp / WindowsKeyManager.cpp): XInput's
			//thirteen digital buttons are 1-based, so button j lands at bit j-1, in
			//the wButtons order - D-pad 0..3, Start 4, Back 5, stick clicks 6..7,
			//LB 8, RB 9, A 12, B 13.
			{ GamepadBackend::XInput, PadButton::Up, 0 },
			{ GamepadBackend::XInput, PadButton::Down, 1 },
			{ GamepadBackend::XInput, PadButton::Left, 2 },
			{ GamepadBackend::XInput, PadButton::Right, 3 },
			{ GamepadBackend::XInput, PadButton::Start, 4 },
			{ GamepadBackend::XInput, PadButton::Select, 5 },
			{ GamepadBackend::XInput, PadButton::L, 8 },
			{ GamepadBackend::XInput, PadButton::R, 9 },
			{ GamepadBackend::XInput, PadButton::A, 12 },
			{ GamepadBackend::XInput, PadButton::B, 13 },

			//Linux / evdev (LinuxGameController.cpp): bits 0..13 are the kernel's
			//BTN_A..BTN_THUMBR - A 0, B 1, TL 6 ("L"), TR 7 ("R"), SELECT 10,
			//START 11. There is no console D-pad button: evdev reports the hat as
			//axes at bits 26..29, outside the 24 GamepadState carries, so those
			//keys have no bit and stay dark.
			{ GamepadBackend::Evdev, PadButton::A, 0 },
			{ GamepadBackend::Evdev, PadButton::B, 1 },
			{ GamepadBackend::Evdev, PadButton::L, 6 },
			{ GamepadBackend::Evdev, PadButton::R, 7 },
			{ GamepadBackend::Evdev, PadButton::Select, 10 },
			{ GamepadBackend::Evdev, PadButton::Start, 11 }
		};
		return table;
	}

	//The bit this backend reports `button` at, or -1 when its GamepadState carries
	//no such button: the evdev D-pad above, and DirectInput, whose buttons are raw
	//joystick buttons with no console-pad semantics to name one by.
	inline int BitOf(GamepadBackend backend, PadButton button)
	{
		for(const PadButtonBit& row : Table()) {
			if(row.Backend == backend && row.Button == button) {
				return row.Bit;
			}
		}
		return -1;
	}
}
