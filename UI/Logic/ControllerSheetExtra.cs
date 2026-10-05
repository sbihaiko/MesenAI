using System;
using System.Collections.Generic;
using Mesen.Config.Shortcuts;

namespace Mesen.Logic;

//ADR-0255 slice 4 (PRD Part B §13.5.2 W-P17): EXTRA BUTTONS, "a pad with more
//buttons than the console needs should be able to carry the emulator's own
//actions on them" - the user's fourth requirement, *"se o jostick tiver mais
//botoes que o nitendo quero poder atribuir outros controles como retroceder,
//avancar, compartilhar, home, etc"*.
//
//The section is a filtered view of the one shortcut list
//(PreferencesConfig.ShortcutKeys), never a second store: a row's binding *is*
//the shortcut's own spare slot (ShortcutKeyInfo.PadBinding), so the classic
//Input page and this sheet edit the same object, and a shortcut that already had
//a keyboard combination keeps it - the pad slot is a third binding beside the
//two key sets, not a replacement for them. The engine holds three key sets for
//exactly this (ShortcutKeySets, Core/Shared/SettingTypes.h): with two, the slot
//of a shortcut that already had both filled was overwritten before it reached
//the core, and the three actions this section exists for - Rewind, FastForward,
//ToggleOverlay - are the three that ship with both filled.
//
//This file is the host-free half, so it speaks in a pair and not in the config
//type: UI/Logic dual-compiles into UI.Tests, which globs that folder, and
//PadShortcutBinding reaches InputApi (it asks the host to name its own codes),
//which is exactly what cannot. The caller adapts - two one-line helpers in
//ControllerSheetViewModel.Extra - and everything decided here is decided once.
//
//**The filter, and why this one.** The list carries no per-action metadata that
//says "this one belongs on a pad" - its entries are an enum, two key
//combinations and the pad slot, and its ~120 actions include every console's own
//device (the Zapper, the Famicom keyboard, the VS coin slots), the debugger's,
//the audio player's and the Advanced-only view toggles. Two filters were
//rejected:
//
//- *Every shortcut*: most of the list is unreachable or meaningless in Player
//  mode, and a section of 120 rows is not a surface anyone can use.
//- *Every shortcut the player has a key bound to*: that would make the row set
//  drift with the keyboard configuration, so the same pad button could be
//  offered for two different actions on two machines, and an action with no key
//  - the case this ADR is about - would never appear at all.
//
//What is listed instead is the short set below: the four the user named
//(retroceder/avancar/compartilhar/home) plus the save-state pair, which is what
//spare pad buttons are for on every other emulator. Each is a Player-mode action
//a player reaches for mid-game; the console-specific devices, the debugger and
//the view toggles are deliberately out.
public static class ControllerSheetExtra
{
	//The actions the section lists, in the order it lists them. Read off the
	//shortcut list by EmulatorShortcut, so a row edits the entry the player
	//already has (with its keyboard combination intact) and never a copy.
	private static readonly EmulatorShortcut[] _actions = {
		//"retroceder, avancar"
		EmulatorShortcut.Rewind,
		EmulatorShortcut.FastForward,
		//The save-state pair: the two every other emulator puts on a spare button.
		EmulatorShortcut.SaveState,
		EmulatorShortcut.LoadState,
		//"compartilhar"
		EmulatorShortcut.TakeScreenshot,
		//"home" - the pause menu, the one surface that reaches the rest while a
		//game runs.
		EmulatorShortcut.ToggleOverlay
	};

	public static IReadOnlyList<EmulatorShortcut> Actions => _actions;

	//A shortcut's spare slot as this file reads it: the control's code and the
	//threshold that travels with it when it is an axis direction.
	public static (ushort KeyCode, int? ThresholdPercent)? Slot(bool isEmpty, ushort keyCode, int? thresholdPercent)
	{
		return isEmpty ? null : (keyCode, thresholdPercent);
	}

	//The bind rule: what the slot becomes when `code` is captured on the row.
	//
	//The threshold survives only when the *same* code is bound again - the player
	//re-picking the control that is already there. It is a property of the
	//direction, not of the row: rebinding to a different direction starts at the
	//default (PadAxisAction.DefaultThresholdPercent), and binding a plain button
	//drops it entirely, because a threshold with nothing to threshold is a field
	//that lies about being in use (PadShortcutBinding).
	public static (ushort KeyCode, int? ThresholdPercent) Bind((ushort KeyCode, int? ThresholdPercent)? current, ushort code)
	{
		int? threshold = current is { KeyCode: var boundCode, ThresholdPercent: var boundThreshold } && boundCode == code
			? boundThreshold
			: null;
		return (code, threshold);
	}

	//The clear rule: the slot goes back to "none". Null - not an empty slot - is
	//how the config says so (ShortcutKeyInfo.PadBinding), so nothing is written
	//for this shortcut afterwards.
	public static (ushort KeyCode, int? ThresholdPercent)? Clear()
	{
		return null;
	}

	//What a row shows as the bound control: the host's own name for the code, or
	//the caller's "not bound" text when the slot is empty. The name is the
	//backend's ("Pad1 X+", "Joy2 But3"), the same string the classic Input page
	//and the capture show.
	public static string Describe((ushort KeyCode, int? ThresholdPercent)? slot, Func<ushort, string> keyName, string unbound)
	{
		return slot is { KeyCode: var code } ? keyName(code) : unbound;
	}
}
