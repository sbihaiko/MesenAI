using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//What a field is to the keyboard, declared by the field itself (ADR-0262
//Decision 2): a masked box is a secret, a box carrying the padCode style class
//is code-shaped, and every other box is free text.
public enum PadKeyboardShape
{
	Text,
	Code,
	Secret
}

public enum PadKeyKind
{
	Char,
	Space,
	Shift,
	Delete,
	Commit
}

public readonly record struct PadKeyboardKey(PadKeyKind Kind, char Char = '\0');

//What a press did, for the host to apply: Edited writes the draft into the
//field, Committed and Cancelled close the keyboard (Cancelled with the draft
//already put back to the original), Moved repaints the cursor.
public enum PadKeyboardOutcome
{
	None,
	Moved,
	Edited,
	Committed,
	Cancelled
}

//Why the keyboard closed without the pad pressing OK or B: the field went
//away under it, the focus moved off the field, or the pad lost authority.
public enum PadKeyboardLeave
{
	FieldGone,
	FocusMoved,
	AuthorityLost
}

//ADR-0262 (#966, pick (a)): the one on-screen pad keyboard, a grid of keys the
//D-pad walks and A presses, owned by the one pad bridge
//(PlayPadNavigationWiring) - per-view pad code is ADR-0256's non-goal.
//Host-free (ADR-0122): the bridge feeds it the pad's actions and applies its
//outcomes, so the alphabet, editing, commit and cancel are all pinned here.
public sealed class PadKeyboard
{
	public const int Columns = 10;
	public const char MaskChar = '•';

	//The NES Game Genie's sixteen letters first (the shape most codes take),
	//then the hex digits the letters do not already cover (Pro Action Replay,
	//the GB/SMS Game Genie), then the separators those shapes use.
	private const string CodeChars = "APZLGITYEOXUKSVN" + "0123456789" + "BCDF" + "-:+";
	private const string TextChars = "abcdefghijklmnopqrstuvwxyz0123456789" + ".,'-!?&:()/_+#@";
	private const string SecretChars = "abcdefghijklmnopqrstuvwxyz0123456789" + "-_.";

	private readonly int _maxLength;

	public PadKeyboard(PadKeyboardShape shape, string original, int maxLength = 0)
	{
		Shape = shape;
		Original = original ?? "";
		Draft = Original;
		_maxLength = maxLength;
		Keys = BuildKeys(shape);
	}

	public PadKeyboardShape Shape { get; }
	public string Original { get; }
	public string Draft { get; private set; }
	public int Cursor { get; private set; }
	public bool Shifted { get; private set; }
	public IReadOnlyList<PadKeyboardKey> Keys { get; }

	//What the keyboard draws as the draft: a secret never shows its characters.
	public string Display => Shape == PadKeyboardShape.Secret ? new string(MaskChar, Draft.Length) : Draft;

	public static PadKeyboardShape ShapeOf(bool masked, bool codeClass)
	{
		return masked ? PadKeyboardShape.Secret : codeClass ? PadKeyboardShape.Code : PadKeyboardShape.Text;
	}

	//The key a character is typed with, or -1 when this keyboard has none.
	//Letters match either case on a keyboard with a case key.
	public int IndexOf(char c)
	{
		char key = Keys.Any(k => k.Kind == PadKeyKind.Shift) ? char.ToLowerInvariant(c) : c;
		if(c == ' ') {
			return Keys.ToList().FindIndex(k => k.Kind == PadKeyKind.Space);
		}
		return Keys.ToList().FindIndex(k => k.Kind == PadKeyKind.Char && k.Char == key);
	}

	public string Label(PadKeyboardKey key)
	{
		return key.Kind switch {
			PadKeyKind.Char => (Shifted ? char.ToUpperInvariant(key.Char) : key.Char).ToString(),
			PadKeyKind.Space => "␣",
			PadKeyKind.Shift => Shifted ? "abc" : "ABC",
			PadKeyKind.Delete => "⌫",
			_ => "OK"
		};
	}

	//The text the field keeps when the keyboard is closed from outside
	//(ADR-0262 Decision 4): losing pad authority commits the draft, so a
	//stray key or mouse event never silently drops a half-typed code - except
	//on a secret, which is never stored unconfirmed (#1004); the field going
	//away or the focus moving elsewhere is a cancel.
	public string TextOnLeave(PadKeyboardLeave why)
	{
		return why == PadKeyboardLeave.AuthorityLost && Shape != PadKeyboardShape.Secret ? Draft : Original;
	}

	public PadKeyboardOutcome Press(PadNavAction action)
	{
		switch(action) {
			case PadNavAction.Left:
				return MoveTo((Cursor - 1 + Keys.Count) % Keys.Count);
			case PadNavAction.Right:
				return MoveTo((Cursor + 1) % Keys.Count);
			case PadNavAction.Up:
				return Cursor < Columns ? PadKeyboardOutcome.None : MoveTo(Cursor - Columns);
			case PadNavAction.Down:
				return MoveTo(Math.Min(Cursor + Columns, Keys.Count - 1));
			case PadNavAction.Back:
				Draft = Original;
				return PadKeyboardOutcome.Cancelled;
			case PadNavAction.Confirm:
				return PressKey(Keys[Cursor]);
			default:
				return PadKeyboardOutcome.None;
		}
	}

	private PadKeyboardOutcome MoveTo(int index)
	{
		if(index == Cursor) {
			return PadKeyboardOutcome.None;
		}
		Cursor = index;
		return PadKeyboardOutcome.Moved;
	}

	private PadKeyboardOutcome PressKey(PadKeyboardKey key)
	{
		switch(key.Kind) {
			case PadKeyKind.Commit:
				return PadKeyboardOutcome.Committed;
			case PadKeyKind.Shift:
				Shifted = !Shifted;
				return PadKeyboardOutcome.Moved;
			case PadKeyKind.Delete:
				if(Draft.Length == 0) {
					return PadKeyboardOutcome.None;
				}
				Draft = Draft[..^1];
				return PadKeyboardOutcome.Edited;
			default:
				if(_maxLength > 0 && Draft.Length >= _maxLength) {
					return PadKeyboardOutcome.None;
				}
				char c = key.Kind == PadKeyKind.Space ? ' ' : Shifted ? char.ToUpperInvariant(key.Char) : key.Char;
				Draft += c;
				return PadKeyboardOutcome.Edited;
		}
	}

	private static IReadOnlyList<PadKeyboardKey> BuildKeys(PadKeyboardShape shape)
	{
		string chars = shape switch {
			PadKeyboardShape.Code => CodeChars,
			PadKeyboardShape.Secret => SecretChars,
			_ => TextChars
		};
		List<PadKeyboardKey> keys = chars.Select(c => new PadKeyboardKey(PadKeyKind.Char, c)).ToList();
		if(shape != PadKeyboardShape.Code) {
			//A code has no spaces and no case: its letters are capitals already.
			keys.Add(new PadKeyboardKey(PadKeyKind.Space));
			keys.Add(new PadKeyboardKey(PadKeyKind.Shift));
		}
		keys.Add(new PadKeyboardKey(PadKeyKind.Delete));
		keys.Add(new PadKeyboardKey(PadKeyKind.Commit));
		return keys;
	}
}
