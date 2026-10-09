using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//"Sim, quadro congelado" (2026-10-03): behind W-P4 and the Play sheets the
	//game's last frame shows, frozen, where the hidden native picture was.
	public class PlayFrozenFrameTests
	{
		[Fact]
		public void A_surface_over_the_game_shows_the_frozen_frame()
		{
			Assert.True(PlayFrozenFrame.Shows(true, false, false, true));
		}

		[Fact]
		public void The_running_game_shows_its_live_picture_instead()
		{
			Assert.False(PlayFrozenFrame.Shows(true, false, false, false));
		}

		[Theory]
		[InlineData(false, false, false)]
		[InlineData(true, true, false)]
		[InlineData(true, false, true)]
		public void Outside_the_game_view_under_the_slot_grid_or_with_the_software_picture_it_does_not_show(bool gameView, bool recents, bool softwareFrame)
		{
			Assert.False(PlayFrozenFrame.Shows(gameView, recents, softwareFrame, true));
		}

		[Fact]
		public void The_frame_is_captured_when_a_surface_first_covers_a_loaded_game()
		{
			Assert.Equal(FrozenFrameStep.Capture, PlayFrozenFrame.Next(false, true, true));
		}

		[Fact]
		public void Moving_between_w_p4_and_its_sheets_keeps_the_same_frame()
		{
			Assert.Equal(FrozenFrameStep.Keep, PlayFrozenFrame.Next(true, true, true));
		}

		[Fact]
		public void The_frame_is_dropped_when_the_game_resumes()
		{
			Assert.Equal(FrozenFrameStep.Drop, PlayFrozenFrame.Next(true, false, true));
		}

		[Fact]
		public void The_frame_is_dropped_when_the_game_is_gone()
		{
			Assert.Equal(FrozenFrameStep.Drop, PlayFrozenFrame.Next(true, true, false));
		}

		[Fact]
		public void Without_a_game_nothing_is_captured()
		{
			Assert.Equal(FrozenFrameStep.Keep, PlayFrozenFrame.Next(false, true, false));
		}

		//ADR-0254: a pause that cut the load card short came before the game's
		//first picture, so the core's last frame is not one - W-P4 sits over
		//no frozen frame rather than an empty image in the picture's place.
		[Fact]
		public void Before_the_first_picture_nothing_is_captured_or_shown()
		{
			Assert.Equal(FrozenFrameStep.Keep, PlayFrozenFrame.Next(false, true, true, pictureOut: false));
			Assert.False(PlayFrozenFrame.Shows(true, false, false, true, pictureOut: false));
		}

		//ADR-0254, #1129: the load card of an OPEN covers the home, and #734 keeps
		//the home up over the card until the game's first picture. So during that
		//card the game is not the thing on screen: `FrameCaptureApi.CaptureFrame`
		//would take the core's last frame - the previous game's, or an unpainted
		//one - and hold it as W-P4's picture (#1155: the focus pause did exactly
		//that, because the decision ran before the core's own GamePaused
		//notification had reached the UI thread to cut the picture wait short).
		[Fact]
		public void A_pause_while_the_load_card_covers_the_home_takes_no_frame()
		{
			Assert.Equal(FrozenFrameStep.Keep, PlayFrozenFrame.Next(false, surfaceOverGame: true, gameLoaded: true, gameOnScreen: false));
		}

		//The reload half of the same input: the card there is over a game that IS
		//on screen (PlayLoadWait.ShowsReloadFor), so its last picture is exactly
		//the one the frame stands in for and the capture stands.
		[Fact]
		public void A_pause_while_the_load_card_covers_the_game_keeps_its_frame()
		{
			Assert.Equal(FrozenFrameStep.Capture, PlayFrozenFrame.Next(false, surfaceOverGame: true, gameLoaded: true, gameOnScreen: true));
		}
	}

	//"Seguir o render" (2026-10-03): a sheet opened from W-P4 leaves the card
	//on screen, dimmed behind it (W-P5, W-P6, W-P7, W-P8, W-P11, W-P16).
	public class PauseCardTests
	{
		[Fact]
		public void W_p4_alone_is_the_active_card()
		{
			Assert.Equal(PauseCardLayer.Active, PauseCard.Layer(true, true, PlaySheet.None));
		}

		[Theory]
		[InlineData(PlaySheet.PackPickerFromOverlay)]
		[InlineData(PlaySheet.PackDetail)]
		[InlineData(PlaySheet.Enhancements)]
		[InlineData(PlaySheet.Cheats)]
		[InlineData(PlaySheet.SaveStates)]
		[InlineData(PlaySheet.Replays)]
		[InlineData(PlaySheet.Settings)]
		[InlineData(PlaySheet.PackDep)]
		public void A_sheet_opened_from_w_p4_dims_the_card_behind_it(PlaySheet sheet)
		{
			//The sheets hide the overlay as they open (replace-not-stack).
			Assert.Equal(PauseCardLayer.Dimmed, PauseCard.Layer(true, false, sheet));
		}

		[Fact]
		public void The_first_start_pack_picker_was_not_opened_from_w_p4()
		{
			Assert.Equal(PauseCardLayer.Hidden, PauseCard.Layer(true, false, PlaySheet.PackPickerOnLoad));
		}

		[Fact]
		public void The_running_game_shows_no_card()
		{
			Assert.Equal(PauseCardLayer.Hidden, PauseCard.Layer(true, false, PlaySheet.None));
		}

		[Fact]
		public void Without_a_game_a_sheet_shows_no_card_behind_it()
		{
			Assert.Equal(PauseCardLayer.Hidden, PauseCard.Layer(false, false, PlaySheet.Cheats));
		}

		//The renders' card goes from 250 to 181 and the game behind it from
		//23/38/67 to 17/28/49: one black layer whose multiplier is ~0.725.
		[Theory]
		[InlineData(250, 181)]
		[InlineData(38, 28)]
		[InlineData(67, 49)]
		public void The_dim_layer_matches_the_renders(int before, int after)
		{
			double kept = 1 - PauseCard.DimAlpha / 255.0;
			Assert.InRange(before * kept, after - 1.5, after + 1.5);
		}
	}
}
