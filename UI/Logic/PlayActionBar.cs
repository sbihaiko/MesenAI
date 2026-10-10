using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1104 (spec #1102, ADR-0256 Decision 6, PRD Part B §13.3): the one footer every
//pad-drivable Play surface renders. A surface declares the actions it performs
//where it claims the focus (PlayFocusOnOpen), and this rule turns that
//declaration into a line: in the fixed order A, X, Y, shoulders, B, naming each
//control in words for the pad in hand - the vocabulary W-P4's footer already
//used (PlayMenuHint's PadFamily/PlayInputDevice), never glyphs.
//
//The enum order IS the display order. Move is the on-screen keyboard's own
//entry (ADR-0262) and leads, because it is not a button.
public enum PlayAction
{
	Move,
	Confirm,
	Favorite,
	Search,
	ConsoleFilter,
	//#1177: the home's door to Settings - Start on a pad, which has no key.
	Settings,
	Back
}

//LabelKey is a resource message id; the copy lives in resources, the decision
//lives here (same split as PlayEntryToast).
public sealed record PlayBarEntry(PlayAction Action, string LabelKey);

public static class PlayActionBar
{
	private const string Separator = "     ";

	//The three entries the shared on-screen keyboard shows while it is open
	//(ADR-0262 Decision 3): every press is the keyboard's, so these replace the
	//surface's own.
	public static readonly IReadOnlyList<PlayBarEntry> KeyboardEntries = new[] {
		new PlayBarEntry(PlayAction.Move, "BarKeyboardMove"),
		new PlayBarEntry(PlayAction.Confirm, "BarKeyboardPress"),
		new PlayBarEntry(PlayAction.Back, "BarKeyboardCancel"),
	};

	//family null with a controller is a pad whose mapping does not resolve: the
	//neutral wording names no button (Decision 4's reason - a guess would point
	//the player at a control their pad may not have). With the keyboard, an
	//action only a pad performs has no key and is not listed, so every label
	//stays true.
	public static string Text(IEnumerable<PlayBarEntry> declared, PlayInputDevice device, PadFamily? family, bool keyboardOpen, Func<string, string> label)
	{
		IEnumerable<PlayBarEntry> entries = keyboardOpen ? KeyboardEntries : declared;
		List<string> parts = new();
		foreach(PlayBarEntry entry in entries.OrderBy(e => e.Action)) {
			string? control = ControlName(entry.Action, device, family);
			if(control == null) {
				continue;
			}
			string text = label(entry.LabelKey);
			parts.Add(control.Length == 0 ? text : control + " " + text);
		}
		return string.Join(Separator, parts);
	}

	//Empty is "name no control" (neutral); null is "this device has no such
	//control" (listed nowhere).
	private static string? ControlName(PlayAction action, PlayInputDevice device, PadFamily? family)
	{
		if(device == PlayInputDevice.Keyboard) {
			return action switch {
				PlayAction.Move => "Arrows",
				PlayAction.Confirm => "Enter",
				PlayAction.Back => "Esc",
				_ => null
			};
		}
		return family switch {
			PadFamily.Xbox => action switch {
				PlayAction.Move => "D-pad",
				PlayAction.Confirm => "A",
				PlayAction.Favorite => "X",
				PlayAction.Search => "Y",
				PlayAction.ConsoleFilter => "LB / RB",
				PlayAction.Settings => "Start",
				_ => "B"
			},
			PadFamily.Ps4 => action switch {
				PlayAction.Move => "D-pad",
				PlayAction.Confirm => "Cross",
				PlayAction.Favorite => "Square",
				PlayAction.Search => "Triangle",
				PlayAction.ConsoleFilter => "L1 / R1",
				PlayAction.Settings => "Options",
				_ => "Circle"
			},
			_ => ""
		};
	}
}
