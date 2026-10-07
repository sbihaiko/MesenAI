using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0262 (#966, pick (a)): the shared on-screen pad keyboard. A pad fills any
//Play text field through it - edit, delete, commit and cancel by pad alone,
//cancel giving back the field's original value, a secret field drawn masked.
//Every expected value is written out, never derived the way the rule derives it.
public class PadKeyboardTests
{
	//Moves the cursor onto a key and presses it, the way a player does: D-pad
	//steps (Right only, one key at a time), then A.
	private static void Type(PadKeyboard keyboard, string text)
	{
		foreach(char c in text) {
			PressKey(keyboard, keyboard.IndexOf(c));
		}
	}

	private static PadKeyboardOutcome PressKey(PadKeyboard keyboard, int index)
	{
		Assert.True(index >= 0, "the key is not on this keyboard");
		while(keyboard.Cursor != index) {
			keyboard.Press(PadNavAction.Right);
		}
		return keyboard.Press(PadNavAction.Confirm);
	}

	private static int KeyOf(PadKeyboard keyboard, PadKeyKind kind)
	{
		return keyboard.Keys.ToList().FindIndex(key => key.Kind == kind);
	}

	[Fact]
	public void A_code_field_offers_the_game_genie_letters_hex_digits_and_separators_and_nothing_else()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Code, "");

		string chars = new(keyboard.Keys.Where(k => k.Kind == PadKeyKind.Char).Select(k => k.Char).ToArray());
		Assert.Equal("APZLGITYEOXUKSVN0123456789BCDF-:+", chars);
		Assert.Equal(new[] { PadKeyKind.Delete, PadKeyKind.Commit }, keyboard.Keys.Where(k => k.Kind != PadKeyKind.Char).Select(k => k.Kind));
		Assert.Equal(-1, keyboard.IndexOf('Q'));
		Assert.Equal(-1, keyboard.IndexOf(' '));
	}

	//#994 review 1: a game title or a cheat description carries brackets,
	//slashes and the like ("Bubble Bobble (Part 2)", "Kid Icarus / Of Myths").
	[Fact]
	public void A_text_field_offers_the_title_punctuation_a_game_name_uses()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Text, "");

		string chars = new(keyboard.Keys.Where(k => k.Kind == PadKeyKind.Char).Select(k => k.Char).ToArray());
		Assert.Equal("abcdefghijklmnopqrstuvwxyz0123456789.,'-!?&:()/_+#@", chars);
	}

	[Fact]
	public void A_text_field_types_a_title_with_brackets_and_a_slash()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Text, "");

		Type(keyboard, "bubble bobble (part 2) / #1 @_+");

		Assert.Equal("bubble bobble (part 2) / #1 @_+", keyboard.Draft);
	}

	[Fact]
	public void A_text_field_offers_letters_digits_punctuation_space_and_a_case_key()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Text, "");

		Assert.Equal(0, keyboard.IndexOf('a'));
		Assert.True(keyboard.IndexOf('z') > 0);
		Assert.True(keyboard.IndexOf('9') > 0);
		Assert.True(keyboard.IndexOf(' ') > 0);
		Assert.True(KeyOf(keyboard, PadKeyKind.Shift) > 0);
		Assert.True(KeyOf(keyboard, PadKeyKind.Delete) > 0);
		Assert.True(KeyOf(keyboard, PadKeyKind.Commit) > 0);
	}

	[Fact]
	public void Typing_a_game_genie_code_and_committing_keeps_it()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Code, "");

		Type(keyboard, "SZKGPAVG");

		Assert.Equal("SZKGPAVG", keyboard.Draft);
		Assert.Equal(PadKeyboardOutcome.Committed, PressKey(keyboard, KeyOf(keyboard, PadKeyKind.Commit)));
		Assert.Equal("SZKGPAVG", keyboard.Draft);
	}

	[Fact]
	public void Delete_removes_the_last_character_and_does_nothing_on_an_empty_draft()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Code, "00B0:FF");

		Assert.Equal(PadKeyboardOutcome.Edited, PressKey(keyboard, KeyOf(keyboard, PadKeyKind.Delete)));
		Assert.Equal("00B0:F", keyboard.Draft);

		PadKeyboard empty = new(PadKeyboardShape.Code, "");
		Assert.Equal(PadKeyboardOutcome.None, PressKey(empty, KeyOf(empty, PadKeyKind.Delete)));
		Assert.Equal("", empty.Draft);
	}

	[Fact]
	public void Back_cancels_and_gives_back_the_original_value()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Text, "contra");
		Type(keyboard, " lives");
		PressKey(keyboard, KeyOf(keyboard, PadKeyKind.Delete));
		Assert.Equal("contra live", keyboard.Draft);

		Assert.Equal(PadKeyboardOutcome.Cancelled, keyboard.Press(PadNavAction.Back));
		Assert.Equal("contra", keyboard.Draft);
		Assert.Equal("contra", keyboard.Original);
	}

	[Fact]
	public void The_case_key_types_the_next_letters_in_capitals_until_pressed_again()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Text, "");
		int shift = KeyOf(keyboard, PadKeyKind.Shift);

		PressKey(keyboard, shift);
		Type(keyboard, "ab");
		PressKey(keyboard, shift);
		Type(keyboard, "c1");

		Assert.Equal("ABc1", keyboard.Draft);
		Assert.Equal("c", keyboard.Label(keyboard.Keys[keyboard.IndexOf('c')]));
		PressKey(keyboard, shift);
		Assert.Equal("C", keyboard.Label(keyboard.Keys[keyboard.IndexOf('c')]));
	}

	[Fact]
	public void A_secret_field_is_displayed_masked_and_a_plain_one_is_not()
	{
		PadKeyboard secret = new(PadKeyboardShape.Secret, "ab");
		Type(secret, "-_");
		Assert.Equal("ab-_", secret.Draft);
		Assert.Equal("••••", secret.Display);

		PadKeyboard plain = new(PadKeyboardShape.Text, "ab");
		Assert.Equal("ab", plain.Display);
	}

	[Fact]
	public void A_full_field_refuses_another_character()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Code, "SZK", maxLength: 3);

		Assert.Equal(PadKeyboardOutcome.None, PressKey(keyboard, keyboard.IndexOf('A')));
		Assert.Equal("SZK", keyboard.Draft);
	}

	[Fact]
	public void The_cursor_wraps_left_and_right_and_moves_a_row_on_up_and_down()
	{
		PadKeyboard keyboard = new(PadKeyboardShape.Code, "");
		int last = keyboard.Keys.Count - 1;

		Assert.Equal(PadKeyboardOutcome.Moved, keyboard.Press(PadNavAction.Left));
		Assert.Equal(last, keyboard.Cursor);
		keyboard.Press(PadNavAction.Right);
		Assert.Equal(0, keyboard.Cursor);

		keyboard.Press(PadNavAction.Down);
		Assert.Equal(PadKeyboard.Columns, keyboard.Cursor);
		keyboard.Press(PadNavAction.Up);
		Assert.Equal(0, keyboard.Cursor);
		//Up on the top row stays; Down past the last row lands on the last key.
		Assert.Equal(PadKeyboardOutcome.None, keyboard.Press(PadNavAction.Up));
		Assert.Equal(0, keyboard.Cursor);
		for(int i = 0; i < 10; i++) {
			keyboard.Press(PadNavAction.Down);
		}
		Assert.Equal(last, keyboard.Cursor);
	}

	[Theory]
	[InlineData(true, false, PadKeyboardShape.Secret)]
	[InlineData(true, true, PadKeyboardShape.Secret)]
	[InlineData(false, true, PadKeyboardShape.Code)]
	[InlineData(false, false, PadKeyboardShape.Text)]
	public void A_field_declares_its_shape_by_its_mask_or_its_code_class(bool masked, bool codeClass, PadKeyboardShape shape)
	{
		Assert.Equal(shape, PadKeyboard.ShapeOf(masked, codeClass));
	}
}
