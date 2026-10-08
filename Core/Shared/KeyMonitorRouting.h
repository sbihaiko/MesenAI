#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"

//#1080: the decision the macOS key monitor makes for a host keyboard event.
//
//MacOS/MacOSKeyManager.mm installs an app-level NSEvent monitor: every key-down,
//key-up and flags-changed event the app receives passes through it before any
//window (or the core) sees the key, and what the handler returns is what decides
//who gets it - `nil` discards the event and publishes it to the core's key state,
//the event itself lets AppKit dispatch it normally, which is how it reaches the
//Avalonia window. Avalonia.Native installs no monitor of its own (its keys arrive
//on the responder chain from the key window), so a key consumed here never
//reaches MainWindow.OnPreviewKeyDown: that is why an overlay key answered only at
//the window was unreachable on this platform (#1080), and why the routing has to
//be right here.
//
//Extracted host-free (ADR-0127; Core/Shared/ShortcutKeyRules.h and
//Core/Shared/AliasedKeyState.h are the same pattern) so the case the fix is about
//- the overlay's own press while the app is foreground and not InBackground - is
//asserted without an NSEvent, a window or an Emulator (scripts/core_unit_tests.cpp,
//Bloco U).
//
//The overlay's press rule is the twin of UI/Logic/OverlayKeyPress.IsThePress, which
//is the copy the window answers presses with. Both exist because only the managed
//side holds the shortcut config in the key space the window feeds the core with
//(UI/Logic/WorkspaceMenu.cs's ShortcutModifiers), and only the native side can
//decide before the event reaches the managed side at all. They must agree: a press
//this rule hands to the UI and that rule does not recognise would be a key both
//sides drop.
//
//One consequence, stated where the rule is: this decides *routing*, not the
//answer. In Background, a Command chord, or a press that IS the overlay's, the
//event is left to the app; whether any of those answers it is the window's
//(MainWindow.HandleEscInTheUi, UiEsc), and a press the window does not answer is
//handed back to the core there, so every arm the core owns keeps working exactly
//as it did.
namespace KeyMonitorRouting
{
	//The modifier families a press or a binding can name. The numbers are
	//UI/Logic/WorkspaceMenu.cs's ShortcutModifiers, which is where the press side of
	//the C# twin keeps them: Alt 1, Control 2, Shift 4. There is no Meta bit on
	//purpose - the shared key table (Core/Shared/KeyDefinitions.h) has no name for
	//the Windows/Command key, so no binding can hold one, and the monitor passes
	//Command chords through before this rule is asked.
	static constexpr int NoModifier = 0;
	static constexpr int AltModifier = 1;
	static constexpr int ControlModifier = 2;
	static constexpr int ShiftModifier = 4;

	//The shared table's codes for the six keys the config can name as a modifier
	//(KeyDefinitions.h: 116-117 Shift, 118-119 Ctrl, 120-121 Alt). The backend hands
	//them in rather than this reading a table of its own, so naming them cannot
	//drift from the key space the config is written in - the same reason
	//UI/Logic/OverlayKeyPress takes a ModifierKeyCodes instead of holding one.
	struct ModifierKeyCodes
	{
		uint16_t LeftShift;
		uint16_t RightShift;
		uint16_t LeftCtrl;
		uint16_t RightCtrl;
		uint16_t LeftAlt;
		uint16_t RightAlt;
	};

	//The modifier a key code names, or NoModifier when it is an ordinary key - the
	//press's own. Both sides of a pair answer the same family: the config cannot
	//tell which hand a "Ctrl" came from, and a press only ever reports the family.
	inline int ModifierFamilyOf(uint16_t keyCode, const ModifierKeyCodes& modifierKeys)
	{
		if(keyCode == modifierKeys.LeftShift || keyCode == modifierKeys.RightShift) {
			return ShiftModifier;
		}
		if(keyCode == modifierKeys.LeftCtrl || keyCode == modifierKeys.RightCtrl) {
			return ControlModifier;
		}
		if(keyCode == modifierKeys.LeftAlt || keyCode == modifierKeys.RightAlt) {
			return AltModifier;
		}
		return NoModifier;
	}

	//Whether a press is the one this combination names: its own key code is one of
	//the combination's keys and the modifier families it carries are exactly the
	//ones the combination names. Extra modifiers make it a different press (a press
	//carrying Ctrl is not a bare-Esc combination) and so does a missing one; a
	//combination with no key answers nothing, so an overlay bound to a pad alone
	//owns no keyboard key at all.
	//
	//The twin of OverlayKeyPress.IsThePress, case for case - including the two
	//refusals that keep a press from being read as a chord it is not: the same key
	//twice, and a combination that wants a key the press cannot carry (a pad code,
	//or a second modifier of a family it can hold only once).
	inline bool IsThePress(const KeyCombination& combination, uint16_t pressedKeyCode, int pressedModifiers, const ModifierKeyCodes& modifierKeys)
	{
		if(pressedKeyCode == 0) {
			return false;
		}

		uint16_t keys[3] = { (uint16_t)combination.Key1, (uint16_t)combination.Key2, (uint16_t)combination.Key3 };
		int named = NoModifier;
		bool ownsTheKey = false;
		for(uint16_t code : keys) {
			if(code == 0) {
				continue;
			}
			if(code == pressedKeyCode) {
				if(ownsTheKey) {
					//The same key twice is not a binding this press can be.
					return false;
				}
				ownsTheKey = true;
				continue;
			}

			int family = ModifierFamilyOf(code, modifierKeys);
			if(family == NoModifier || (named & family) != 0) {
				//The combination wants a key that is not a modifier the press can
				//carry, or two of a family it can carry only once.
				return false;
			}
			named |= family;
		}

		return ownsTheKey && named == pressedModifiers;
	}

	//The overlay's own press, either of the two key sets its config can hold (the
	//pad slot is a third set and carries pad codes, which no keyboard event maps
	//to).
	inline bool IsTheOverlaysPress(uint16_t pressedKeyCode, int pressedModifiers,
		const KeyCombination& firstBinding, const KeyCombination& secondBinding, const ModifierKeyCodes& modifierKeys)
	{
		return IsThePress(firstBinding, pressedKeyCode, pressedModifiers, modifierKeys)
			|| IsThePress(secondBinding, pressedKeyCode, pressedModifiers, modifierKeys);
	}

	enum class Route
	{
		//Published to the core's key state and the event discarded - the game's key.
		FeedTheCore,
		//The event is returned, so AppKit dispatches it to the app: the UI's key.
		LeaveItToTheUi
	};

	//What the monitor does with one host keyboard event. `isTheOverlaysPress` is
	//the overlay's own press, answered at the window (#1080).
	inline Route For(bool inBackground, bool isCommandChord, bool isTheOverlaysPress)
	{
		if(inBackground) {
			//Allow UI to handle key-events when main window is not in focus
			return Route::LeaveItToTheUi;
		}

		if(isCommandChord) {
			//Pass through command-based keydown events so cmd+Q etc still works
			return Route::LeaveItToTheUi;
		}

		//#1080: the overlay's own press is the UI's. Its arm is the window's, and on
		//macOS this monitor is the only path a host key has into the app: swallowing
		//the event here is what left the press unanswered in the reported repro.
		//Returning it publishes nothing to the core, so the press is never answered
		//twice - the window answers it, or hands it back (MainWindow), and the arms
		//the core owns (Remaster, Share, Classic) keep the press they always had.
		if(isTheOverlaysPress) {
			return Route::LeaveItToTheUi;
		}

		return Route::FeedTheCore;
	}
}
