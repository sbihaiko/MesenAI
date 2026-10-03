using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0249 (W-X1): Player mode never asks through a classic message box.
	//Quit game on W-P4 and closing the window ask - only while the
	//ConfirmExitResetPower preference is on, as before - in place, as the
	//shared stop banner: a game's unsaved progress is what would be lost, so
	//it is the pale red banner with the dark button that goes on, in Play's tint.
	public class PlayerConfirmationsTests
	{
		[Theory]
		[InlineData(true, InterruptionKind.QuitGame)]
		[InlineData(false, InterruptionKind.None)]
		public void Quit_game_asks_only_while_the_preference_is_on(bool confirm, InterruptionKind expected)
		{
			Assert.Equal(expected, Interruptions.ForQuitGame(confirm));
		}

		[Theory]
		[InlineData(true, InterruptionKind.QuitApp)]
		[InlineData(false, InterruptionKind.None)]
		public void Closing_the_window_asks_only_while_the_preference_is_on(bool confirm, InterruptionKind expected)
		{
			Assert.Equal(expected, Interruptions.ForQuitApp(confirm));
		}

		[Theory]
		[InlineData(InterruptionKind.QuitGame)]
		[InlineData(InterruptionKind.QuitApp)]
		public void A_quit_is_a_stop_banner_with_a_dark_button_in_plays_tint(InterruptionKind kind)
		{
			Assert.Equal(BannerKind.Stop, InterruptionBanner.KindOf(kind));
			Assert.False(InterruptionBanner.GoIsTinted(kind));
			Assert.Equal(Workspace.Play, InterruptionBanner.WorkspaceOf(kind));
		}
	}
}
