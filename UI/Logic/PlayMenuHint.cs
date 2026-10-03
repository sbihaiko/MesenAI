using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//ADR-0251: Play teaches its pause menu (W-P4) in the W-P3 entry toast, and a
//controller opens it. The bar stays a pause-only surface; this rule decides
//whether a game start shows the hint, which binding it names, and the
//default controller binding for ToggleOverlay.
public enum PlayInputDevice
{
	Keyboard,
	Controller
}

//One DisplayMessage call (title key, message key, %1 parameter). ShowsHint
//says whether the toast spends one of the PlayMenuHintsShown starts.
public sealed record PlayEntryToast(string Title, string Message, string Param, bool ShowsHint);

public static class PlayMenuHint
{
	//The hint shows during the first three game starts after install.
	public const int Starts = 3;

	//Home/Guide where the platform reports one, else Select and Start pressed
	//together (XInput calls Select "Back").
	private static readonly string[][] ControllerCandidates = {
		new[] { "Pad1 Home" },
		new[] { "Pad1 Guide" },
		new[] { "Pad1 Select", "Pad1 Start" },
		new[] { "Pad1 Back", "Pad1 Start" },
	};

	public static bool Shows(int hintsShown) => hintsShown < Starts;

	public static string Text(string binding) => binding + " for the menu";

	//packText is the pack toast's parameter ("Contra 80s — textures"), empty
	//when the game runs without a pack. gameStart is false for a power-cycle
	//reload (a pack pick), which is not a new start. Returns null when there
	//is nothing to show.
	public static PlayEntryToast? EntryToast(string? packText, bool gameStart, bool inPlay, int hintsShown, string? binding)
	{
		bool hint = gameStart && inPlay && Shows(hintsShown) && !string.IsNullOrEmpty(binding);
		if(!string.IsNullOrEmpty(packText)) {
			return hint
				? new PlayEntryToast("MEP", "MepPackApplied", packText + " · " + Text(binding!), true)
				: new PlayEntryToast("MEP", "MepPackApplied", packText, false);
		}
		return hint ? new PlayEntryToast("GameLoaded", Text(binding!), "", true) : null;
	}

	//The PlayMenuHintsShown value after the toast was shown.
	public static int Next(int hintsShown, PlayEntryToast? toast) => toast?.ShowsHint == true ? hintsShown + 1 : hintsShown;

	//The host has no record of which device pressed "open"; a connected
	//controller is taken as the input the player is holding.
	public static PlayInputDevice ActiveDevice(uint connectedGamepads) => connectedGamepads > 0 ? PlayInputDevice.Controller : PlayInputDevice.Keyboard;

	//"Pad1 Select", "Joy2 But3": a host controller button, not a keyboard key.
	public static bool IsControllerKey(string keyName)
	{
		foreach(string prefix in new[] { "Pad", "Joy" }) {
			if(keyName.StartsWith(prefix, StringComparison.Ordinal)) {
				int i = prefix.Length;
				int digits = 0;
				while(i < keyName.Length && char.IsAsciiDigit(keyName[i])) {
					i++;
					digits++;
				}
				if(digits > 0 && i < keyName.Length && keyName[i] == ' ') {
					return true;
				}
			}
		}
		return false;
	}

	//"Pad1 Select" -> "Select"; keyboard names are kept.
	public static string ShortKeyName(string keyName) => IsControllerKey(keyName) ? keyName.Substring(keyName.IndexOf(' ') + 1) : keyName;

	//The two ToggleOverlay slots, as key names. Each slot is classified by its
	//keys, so swapped slots still name the right one; a device with no binding
	//of its own names the other device's.
	public static string? BindingName(PlayInputDevice device, IReadOnlyList<string> firstSlot, IReadOnlyList<string> secondSlot)
	{
		IReadOnlyList<string>? keyboard = null;
		IReadOnlyList<string>? controller = null;
		foreach(IReadOnlyList<string> slot in new[] { firstSlot, secondSlot }) {
			if(slot.Count == 0) {
				continue;
			}
			if(slot.All(IsControllerKey)) {
				controller ??= slot;
			} else {
				keyboard ??= slot;
			}
		}
		IReadOnlyList<string>? chosen = device == PlayInputDevice.Controller ? controller ?? keyboard : keyboard ?? controller;
		return chosen == null ? null : string.Join("+", chosen.Select(ShortKeyName));
	}

	//keyExists: does the platform's key manager define this key name.
	public static IReadOnlyList<string> DefaultControllerKeys(Func<string, bool> keyExists)
	{
		foreach(string[] candidate in ControllerCandidates) {
			if(candidate.All(keyExists)) {
				return candidate;
			}
		}
		return Array.Empty<string>();
	}

	//An upgrade gives ToggleOverlay the controller binding only in an empty
	//second slot, and only when no other shortcut already uses that
	//combination: a binding the user made is never replaced or shadowed.
	public static bool SeedsControllerBinding(bool secondSlotEmpty, bool combinationTaken, IReadOnlyList<string> defaultKeys)
	{
		return secondSlotEmpty && !combinationTaken && defaultKeys.Count > 0;
	}
}
