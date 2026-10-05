using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.2 (PRD Part B §8, §13.5.2 W-P4): the pause overlay's seven controls, the
	//home of every action the P.4 overlay had, and its row values.
	public class PauseOverlayTests
	{
		[Fact]
		public void Overlay_has_exactly_seven_controls_in_wireframe_order()
		{
			Assert.Equal(new[] {
				PauseOverlayControl.Resume, PauseOverlayControl.SaveStates, PauseOverlayControl.Pack,
				PauseOverlayControl.Enhancements, PauseOverlayControl.Cheats, PauseOverlayControl.Settings,
				PauseOverlayControl.QuitGame
			}, PauseOverlay.Controls);
			Assert.Equal(PauseOverlay.MaxControls, PauseOverlay.Controls.Count);
			Assert.Equal(Enum.GetValues<PauseOverlayControl>().Length, PauseOverlay.Controls.Distinct().Count());
		}

		//The stop rule's "every former overlay action still reachable".
		[Fact]
		public void Every_former_overlay_action_has_a_destination()
		{
			foreach(FormerOverlayAction action in Enum.GetValues<FormerOverlayAction>()) {
				PauseOverlayDestination where = PauseOverlay.WhereNow(action);
				Assert.True(where.Control.HasValue || where.ToolsPath.StartsWith("Tools ⋯ › ") || where.ToolsPath.StartsWith("Switcher › "), action.ToString());
				if(where.Control.HasValue) {
					Assert.Contains(where.Control.Value, PauseOverlay.Controls);
				}
			}
		}

		[Fact]
		public void Save_and_load_merge_into_the_save_states_row()
		{
			Assert.Equal(PauseOverlayControl.SaveStates, PauseOverlay.WhereNow(FormerOverlayAction.SaveSlot).Control);
			Assert.Equal(PauseOverlayControl.SaveStates, PauseOverlay.WhereNow(FormerOverlayAction.LoadSlot).Control);
		}

		[Fact]
		public void Advanced_gui_moves_to_the_classic_door_and_quitting_the_app_under_tools()
		{
			Assert.Null(PauseOverlay.WhereNow(FormerOverlayAction.AdvancedGui).Control);
			Assert.Equal("Switcher › Classic", PauseOverlay.WhereNow(FormerOverlayAction.AdvancedGui).ToolsPath);
			Assert.Null(PauseOverlay.WhereNow(FormerOverlayAction.QuitApp).Control);
			//ADR-0250: the shared tail's Quit (the app menu on macOS).
			Assert.Equal("Tools ⋯ › Quit MesenAI", PauseOverlay.WhereNow(FormerOverlayAction.QuitApp).ToolsPath);
		}

		[Fact]
		public void Enhancements_count_skips_overclock_where_the_console_has_none()
		{
			Assert.Equal(4, PauseOverlay.EnhancementsOn(true, true, true, true, overclockSupported: true));
			Assert.Equal(3, PauseOverlay.EnhancementsOn(true, true, true, true, overclockSupported: false));
			Assert.Equal(0, PauseOverlay.EnhancementsOn(false, false, false, false, true));
			Assert.Equal(2, PauseOverlay.EnhancementsOn(true, false, true, false, true));
		}

		[Fact]
		public void Save_states_row_names_the_newest_slot()
		{
			DateTime now = new(2026, 10, 2, 12, 0, 0);
			SaveStateSlotSummary? newest = SaveStatesSummary.Newest(new (int, DateTime?)[] {
				(1, now.AddMinutes(-30)), (2, null), (3, now.AddMinutes(-2)), (4, now.AddDays(-1))
			}, now);
			Assert.NotNull(newest);
			Assert.Equal(3, newest!.Slot);
			Assert.Equal(TimeSpan.FromMinutes(2), newest.Age);
		}

		[Fact]
		public void Save_states_row_is_empty_without_any_slot_file()
		{
			Assert.Null(SaveStatesSummary.Newest(new (int, DateTime?)[] { (1, null), (2, null) }, DateTime.Now));
		}

		[Fact]
		public void A_file_written_in_the_future_reads_just_now()
		{
			DateTime now = new(2026, 10, 2, 12, 0, 0);
			SaveStateSlotSummary? newest = SaveStatesSummary.Newest(new (int, DateTime?)[] { (5, now.AddMinutes(3)) }, now);
			Assert.Equal(TimeSpan.Zero, newest!.Age);
		}

		[Theory]
		[InlineData(30, SaveStateAgeKind.JustNow, 0)]
		[InlineData(120, SaveStateAgeKind.Minutes, 2)]
		[InlineData(3599, SaveStateAgeKind.Minutes, 59)]
		[InlineData(3600, SaveStateAgeKind.Hours, 1)]
		[InlineData(86399, SaveStateAgeKind.Hours, 23)]
		[InlineData(86400 * 3, SaveStateAgeKind.Days, 3)]
		public void Age_reads_in_the_largest_whole_unit(int seconds, SaveStateAgeKind kind, int count)
		{
			Assert.Equal((kind, count), SaveStatesSummary.Age(TimeSpan.FromSeconds(seconds)));
		}
	}

	//Rule 8: Esc order is game → W-P4 → resume; a sheet from W-P4 returns to it.
	public class PlayEscTests
	{
		[Fact]
		public void Esc_over_a_running_game_opens_the_overlay()
		{
			Assert.Equal(PlayEscAction.OpenOverlayAndPause, PlayEsc.Next(gameLoaded: true, PlaySheet.None, overlayVisible: false));
		}

		[Fact]
		public void Esc_on_the_overlay_resumes()
		{
			Assert.Equal(PlayEscAction.CloseOverlayAndResume, PlayEsc.Next(true, PlaySheet.None, overlayVisible: true));
		}

		[Fact]
		public void Esc_on_the_home_does_nothing()
		{
			Assert.Equal(PlayEscAction.None, PlayEsc.Next(gameLoaded: false, PlaySheet.None, overlayVisible: false));
		}

		[Theory]
		[InlineData(PlaySheet.PackPickerFromOverlay)]
		[InlineData(PlaySheet.Enhancements)]
		[InlineData(PlaySheet.Cheats)]
		[InlineData(PlaySheet.SaveStates)]
		[InlineData(PlaySheet.SaveStateGrid)]
		//G.4 (W-P6): the current-pack detail.
		[InlineData(PlaySheet.PackDetail)]
		//R.2 (ADR-0205 §7): Shared replays, opened from the Save states sheet.
		[InlineData(PlaySheet.Replays)]
		//ADR-0249 (W-P8, W-P10): Settings is an in-window sheet, not a window.
		[InlineData(PlaySheet.Settings)]
		public void A_sheet_opened_from_the_overlay_closes_back_to_it(PlaySheet sheet)
		{
			Assert.Equal(PlayEscAction.CloseSheetToOverlay, PlayEsc.Next(true, sheet, overlayVisible: false));
		}

		[Fact]
		public void The_first_start_picker_is_dismissed_not_returned_to_an_overlay()
		{
			Assert.Equal(PlayEscAction.DismissPackPicker, PlayEsc.Next(true, PlaySheet.PackPickerOnLoad, overlayVisible: false));
		}

		//ADR-0255 slice 3: while the Controller sheet is capturing "press a
		//control", Esc is the capture's own state - it releases the capture and the
		//sheet stays up, so a second Esc is what closes the sheet. One chain, one
		//more state; no second key handler.
		[Fact]
		public void Esc_releases_a_capture_before_it_closes_the_sheet()
		{
			Assert.Equal(PlayEscAction.CancelCapture, PlayEsc.Next(true, PlaySheet.Controller, overlayVisible: false, capturing: true));
			Assert.Equal(PlayEscAction.CloseSheetToOverlay, PlayEsc.Next(true, PlaySheet.Controller, overlayVisible: false, capturing: false));
			//The capture is the Controller sheet's state alone: it never changes what
			//Esc does on any other sheet.
			Assert.Equal(PlayEscAction.CloseSheetToOverlay, PlayEsc.Next(true, PlaySheet.Settings, overlayVisible: false, capturing: true));
		}

		//The full rule-8 walk: three presses from a sheet end with the game running.
		[Fact]
		public void Esc_sequence_from_a_sheet_is_overlay_then_resume()
		{
			Assert.Equal(PlayEscAction.CloseSheetToOverlay, PlayEsc.Next(true, PlaySheet.Cheats, false));
			Assert.Equal(PlayEscAction.CloseOverlayAndResume, PlayEsc.Next(true, PlaySheet.None, true));
			Assert.Equal(PlayEscAction.OpenOverlayAndPause, PlayEsc.Next(true, PlaySheet.None, false));
		}
	}
}
