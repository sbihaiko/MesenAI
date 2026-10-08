using System;

namespace Mesen.Logic;

//#1080: the six keys the shortcut config can name as a modifier, in the shared
//key table's own order and codes (Core/Shared/KeyDefinitions.h: Left/Right
//Shift 116-117, Left/Right Ctrl 118-119, Left/Right Alt 120-121). Those are the
//codes a KeyCombination stores, and the resolution goes through the host's key
//manager rather than a table of its own, so naming them here cannot drift from
//the key space the config is written in.
//
//There is no Meta here on purpose: the shared table has no name for the
//Windows/Command key, so no binding can hold one - which is why a press carrying
//it is never the overlay's press (see IsThePress).
public readonly record struct ModifierKeyCodes(UInt16 LeftShift, UInt16 RightShift, UInt16 LeftCtrl, UInt16 RightCtrl, UInt16 LeftAlt, UInt16 RightAlt)
{
	public static ModifierKeyCodes Of(Func<string, UInt16> keyCode)
	{
		return new ModifierKeyCodes(
			keyCode("Left Shift"), keyCode("Right Shift"),
			keyCode("Left Ctrl"), keyCode("Right Ctrl"),
			keyCode("Left Alt"), keyCode("Right Alt")
		);
	}

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
		//#1080 pre-fix, kept for one commit: the window takes Esc whatever the
		//binding says. The tests in UI.Tests/Play/OverlayKeyPressTests are the
		//RED for the binding-aware rule below.
		return pressedKeyCode == 13;
	}
}
