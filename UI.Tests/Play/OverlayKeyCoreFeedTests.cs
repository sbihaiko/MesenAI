using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1095: on Windows and Linux the window's own keyboard is the only path a host
//key has into the core (`InputApi.SetKeyState` in `MainWindow.OnPreviewKeyDown`),
//so a press the window declines is still fed to the core. That is wrong for the
//overlay's key while the keyboard is a control's: the core's shortcut handler
//answers it by opening the overlay over the box that is keeping the key (the
//barcode field, a search box). The rule the window applies is host-free and is
//asserted here - the headless suite cannot see it, having no key manager.
public class OverlayKeyCoreFeedTests
{
	//What the window knows at the moment the press arrives: whether the key is
	//the one ToggleOverlay is bound to, and whether the keyboard is somewhere
	//else (a focused text box, an open menu).
	private static bool TheCoreIsFed(bool isTheOverlayKey, bool theKeyboardIsSomewhereElse)
	{
		return OverlayKeyPress.TheCoreIsFedThePress(isTheOverlayKey, theKeyboardIsSomewhereElse);
	}

	//The bug: Esc in a focused barcode field is the field's key, the window
	//declines it (HandleEscInTheUi), and the core must not be handed the press
	//anyway - that press opens the pause sheet over the field.
	[Fact]
	public void The_overlay_key_in_a_text_box_is_not_fed_to_the_core()
	{
		Assert.False(TheCoreIsFed(isTheOverlayKey: true, theKeyboardIsSomewhereElse: true));
	}

	//The unchanged half: the overlay's key with the keyboard in the game view is
	//the core's own press on Windows and Linux (Advanced answers Esc with Pause,
	//and the overlay's arm only takes it in Player mode).
	[Fact]
	public void The_overlay_key_in_the_game_view_is_still_fed_to_the_core()
	{
		Assert.True(TheCoreIsFed(isTheOverlayKey: true, theKeyboardIsSomewhereElse: false));
	}

	//No other key changes hands: every key that is not the overlay's is the
	//game's on Windows and Linux, with the keyboard in a control or in the game
	//view (a key typed into a text box reaches the emulator, as it always has).
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void A_key_that_is_not_the_overlays_is_fed_wherever_the_keyboard_is(bool theKeyboardIsSomewhereElse)
	{
		Assert.True(TheCoreIsFed(isTheOverlayKey: false, theKeyboardIsSomewhereElse: theKeyboardIsSomewhereElse));
	}
}
