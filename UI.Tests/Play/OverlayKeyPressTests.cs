using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1080: the window answers a key at its own keyboard before the core sees it, so
//*which* key that is has to be the binding the player chose - ToggleOverlay's.
//Esc taken by name is the reported bug: rebind the overlay to F1 and Esc still
//opens the pause sheet, while a Ctrl+Esc the player bound to something else is
//swallowed by the overlay's arm.
//
//The codes are the shared key table's (Core/Shared/KeyDefinitions.h), mirrored
//here as literals because this project is host-free: Esc 13, F1 90, Shift
//116-117, Ctrl 118-119, Alt 120-121.
public class OverlayKeyPressTests
{
	private const ushort Esc = 13;
	private const ushort F1 = 90;
	private static readonly ModifierKeyCodes Keys = new(116, 117, 118, 119, 120, 121);

	private static bool IsThePress(ushort key1, ushort key2, ushort key3, ushort pressed, ShortcutModifiers modifiers)
	{
		return OverlayKeyPress.IsThePress(key1, key2, key3, pressed, modifiers, Keys);
	}

	//The default binding, and the press it answers: Esc, no modifiers. This is the
	//behavior the window has today and it has to survive the fix.
	[Fact]
	public void The_default_Esc_binding_is_the_Esc_press()
	{
		Assert.True(IsThePress(Esc, 0, 0, Esc, ShortcutModifiers.None));
	}

	//(a) The overlay rebound to F1 is opened by F1 - the player's own binding, not
	//the window's idea of the key.
	[Fact]
	public void The_overlay_bound_to_F1_is_the_F1_press()
	{
		Assert.True(IsThePress(F1, 0, 0, F1, ShortcutModifiers.None));
	}

	//(b) With the overlay on F1, Esc is nobody's here: the press has to reach the
	//core's shortcut path, where the player's other bindings are answered.
	[Fact]
	public void Esc_is_not_the_press_when_the_overlay_is_bound_to_F1()
	{
		Assert.False(IsThePress(F1, 0, 0, Esc, ShortcutModifiers.None));
	}

	//(c) A bare Esc binding is bare: the modifiers are part of the press, so
	//Ctrl+Esc is a different key and stays available to the core.
	[Fact]
	public void Ctrl_Esc_is_not_the_press_of_a_bare_Esc_binding()
	{
		Assert.False(IsThePress(Esc, 0, 0, Esc, ShortcutModifiers.Control));
	}

	//The same rule one modifier over: Shift+F1 is not the F1 binding.
	[Fact]
	public void Shift_F1_is_not_the_press_of_a_bare_F1_binding()
	{
		Assert.False(IsThePress(F1, 0, 0, F1, ShortcutModifiers.Shift));
	}

	//A combination that *names* a modifier is answered by that modifier, left or
	//right (the config merges the two hands; a press reports the family).
	[Theory]
	[InlineData(118)]
	[InlineData(119)]
	public void A_binding_that_names_Ctrl_is_the_Ctrl_Esc_press(ushort ctrl)
	{
		Assert.True(IsThePress(ctrl, Esc, 0, Esc, ShortcutModifiers.Control));
	}

	//...and only with it: the same combination pressed without Ctrl is not it.
	[Fact]
	public void A_binding_that_names_Ctrl_is_not_the_bare_Esc_press()
	{
		Assert.False(IsThePress(118, Esc, 0, Esc, ShortcutModifiers.None));
	}

	//A modifier the combination does not name makes it a different press, even
	//when the combination names one of its own.
	[Fact]
	public void An_unnamed_modifier_makes_it_a_different_press()
	{
		Assert.False(IsThePress(118, Esc, 0, Esc, ShortcutModifiers.Control | ShortcutModifiers.Shift));
	}

	//An overlay with no keyboard binding owns no key, so the window takes none:
	//the empty combination must not answer the bare press it would otherwise equal.
	[Fact]
	public void An_empty_binding_takes_no_key()
	{
		Assert.False(IsThePress(0, 0, 0, Esc, ShortcutModifiers.None));
		Assert.False(IsThePress(0, 0, 0, 0, ShortcutModifiers.None));
	}
}
