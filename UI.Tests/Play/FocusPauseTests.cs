using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0254 (accepted 2026-10-04): losing focus pauses the game and, in Play,
//says so with the W-P4 overlay instead of freezing silently. The two halves of
//the user's decision are host-free rules in UI/Logic/FocusPause; this pins
//them, and `MainWindow.UpdateAutoPause` is the only caller.
//
//The answers, quoted from the user's own pick on 2026-10-04:
//  * "Fica pausado na tela de Esc" - the overlay waits for the player's Esc, so
//    the automatic resume must not fire under it;
//  * "Ligado por padrão no Play" - the preference is on by default (asserted on
//    a fresh PreferencesConfig in UI.HeadlessTests/FocusPauseDefaultTests,
//    which can see UI/Config).
public class FocusPauseTests
{
	//Which pauses get a voice. Outside Play there is no W-P4, so the pause stays
	//the silent one it has always been (Classic and Advanced keep their look);
	//with no game loaded the overlay's rows would have nothing to describe; and
	//the menus-and-config pause keeps its silence, because the player is using
	//the app and can see what they opened.
	[Theory]
	[InlineData(true, true, true, true)]
	[InlineData(true, true, false, false)]
	[InlineData(true, false, true, false)]
	[InlineData(true, false, false, false)]
	[InlineData(false, true, true, false)]
	[InlineData(false, true, false, false)]
	[InlineData(false, false, true, false)]
	[InlineData(false, false, false, false)]
	public void Only_the_focus_pause_in_play_over_a_loaded_game_has_an_overlay(bool focusLost, bool isPlayDoor, bool gameLoaded, bool expected)
	{
		Assert.Equal(expected, FocusPause.ShowsOverlay(focusLost, isPlayDoor, gameLoaded));
	}

	//The question that decides whether the feature exists at all: if the resume
	//fires under the overlay, the game comes back running behind a pause card -
	//which is both useless and a lie about the state of the game.
	[Fact]
	public void The_focus_pause_does_not_resume_under_its_own_overlay()
	{
		Assert.False(FocusPause.AutoResumes(pausedByFocusWithOverlay: true, overlayOpen: true));
	}

	//Everything the focus path did not open keeps the automatic resume it always
	//had: the menus-and-config pause, and the focus pause once the player has
	//answered the overlay.
	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(false, false)]
	public void Every_other_pause_still_resumes_by_itself(bool pausedByFocusWithOverlay, bool overlayOpen)
	{
		Assert.True(FocusPause.AutoResumes(pausedByFocusWithOverlay, overlayOpen));
	}
}
