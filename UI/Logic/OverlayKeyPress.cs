using System;

namespace Mesen.Logic;

//#1080: the six keys the shortcut config can name as a modifier, in the shared
//key table's own codes (Core/Shared/KeyDefinitions.h: Left/Right Shift 116-117,
//Left/Right Ctrl 118-119, Left/Right Alt 120-121). Those are the codes a
//KeyCombination stores, and the host hands them in rather than this reading a
//table of its own, so naming them cannot drift from the key space the config is
//written in.
//
//There is no Meta here on purpose: the shared table has no name for the
//Windows/Command key, so no binding can hold one - which is why a press carrying
//it is never the overlay's press (see IsThePress).
public readonly record struct ModifierKeyCodes(UInt16 LeftShift, UInt16 RightShift, UInt16 LeftCtrl, UInt16 RightCtrl, UInt16 LeftAlt, UInt16 RightAlt)
{
	//The modifier a key code names, or None when it is an ordinary key - the
	//press's own. Both sides of a pair answer the same family: the config cannot
	//tell which hand a "Ctrl" came from (InputApi's own name table merges them,
	//KeyCombination.GetKeyNames), and a press only ever reports the family.
	public ShortcutModifiers FamilyOf(UInt16 code)
	{
		if(code == LeftShift || code == RightShift) {
			return ShortcutModifiers.Shift;
		}
		if(code == LeftCtrl || code == RightCtrl) {
			return ShortcutModifiers.Control;
		}
		if(code == LeftAlt || code == RightAlt) {
			return ShortcutModifiers.Alt;
		}
		return ShortcutModifiers.None;
	}
}

//#1080: whether a key the window answers at its own keyboard is the key
//ToggleOverlay is bound to. The window took Esc by name (MainWindow's
//HandleEscInTheUi), and the overlay's key is the player's to choose - so
//rebinding the overlay to a controller or to F1 left Esc opening the pause sheet
//anyway, and a modified Esc the player had bound elsewhere was swallowed with it.
//
//The rule is the config's: the press is the overlay's when its own key code is
//one of the combination's keys and the modifier families the press carries are
//exactly the ones the combination names. Extra modifiers make it a different
//press (Ctrl+Esc is not Esc), and so does a missing one.
public static class OverlayKeyPress
{
	public static bool IsThePress(UInt16 key1, UInt16 key2, UInt16 key3, UInt16 pressedKeyCode, ShortcutModifiers pressedModifiers, ModifierKeyCodes modifierKeys)
	{
		//A press with no key, and a combination with no key, answer nothing: the
		//overlay may hold no keyboard binding at all (its chord may be a pad's),
		//and then no key is its.
		if(pressedKeyCode == 0) {
			return false;
		}

		UInt16[] keys = { key1, key2, key3 };
		ShortcutModifiers named = ShortcutModifiers.None;
		bool ownsTheKey = false;
		foreach(UInt16 code in keys) {
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

			ShortcutModifiers family = modifierKeys.FamilyOf(code);
			if(family == ShortcutModifiers.None || (named & family) != ShortcutModifiers.None) {
				//The combination wants a key that is not a modifier the press can
				//carry, or two of a family it can carry only once.
				return false;
			}
			named |= family;
		}

		//The press's own key is one of the combination's, and it carries exactly
		//the modifiers the combination names - one extra (Ctrl+Esc is not Esc) or
		//one missing and it is a different press.
		return ownsTheKey && named == pressedModifiers;
	}

	//#1095: which presses the window's own keyboard hands the core. On Windows and
	//Linux this window is the only path a host key has into the core
	//(InputApi.SetKeyState), so a press the window declines is still fed to it -
	//right for the game's own keys, wrong for the overlay's while the keyboard is a
	//control's: the core's shortcut handler answers that one by opening the overlay
	//over the box keeping the key (a barcode field, a search box, an open menu).
	//
	//The keyboard being somewhere else is therefore what withholds this one press,
	//on every platform - macOS's own arm asks the same two things before it hands
	//the press back to the core.
	public static bool TheCoreIsFedThePress(bool isTheOverlayKey, bool theKeyboardIsSomewhereElse)
	{
		return !(isTheOverlayKey && theKeyboardIsSomewhereElse);
	}
}
