using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//G.1 (PRD Part B §8, ADR-0241, §13.2/§13.5.1): the shell's host-free rules -
	//the switcher's fixed order and shortcuts, the workspace switch, and when
	//the bar (and its status line) is on screen.
	public class WorkspaceShellTests
	{
		[Fact]
		public void Switcher_order_is_fixed_play_remaster_share_classic()
		{
			//ADR-0250 Decision 2: Classic is the fourth door, last.
			Assert.Equal(new[] { Workspace.Play, Workspace.Remaster, Workspace.Share, Workspace.Classic }, WorkspaceShell.Ordered.ToArray());
		}

		[Theory]
		[InlineData(1, Workspace.Play)]
		[InlineData(2, Workspace.Remaster)]
		[InlineData(3, Workspace.Share)]
		[InlineData(4, Workspace.Classic)]
		public void Shortcut_digit_selects_the_row_at_that_position(int digit, Workspace expected)
		{
			Assert.Equal(expected, WorkspaceShell.FromShortcutDigit(digit));
			Assert.Equal(digit, WorkspaceShell.ShortcutDigit(expected));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(5)]
		[InlineData(9)]
		[InlineData(-1)]
		public void Other_digits_select_nothing(int digit)
		{
			Assert.Null(WorkspaceShell.FromShortcutDigit(digit));
		}

		[Fact]
		public void Order_never_depends_on_the_active_workspace()
		{
			//The check mark moves, the rows do not (W-S3).
			WorkspaceState state = new(Workspace.Share);
			Assert.Equal(WorkspaceShell.Ordered.ToArray(), state.Rows.Select(r => r.Workspace).ToArray());
			Assert.Equal(new[] { false, false, true, false }, state.Rows.Select(r => r.IsActive).ToArray());

			state.Select(Workspace.Play);
			Assert.Equal(WorkspaceShell.Ordered.ToArray(), state.Rows.Select(r => r.Workspace).ToArray());
			Assert.Equal(new[] { true, false, false, false }, state.Rows.Select(r => r.IsActive).ToArray());
		}

		[Fact]
		public void Default_workspace_is_play()
		{
			Assert.Equal(Workspace.Play, new WorkspaceState().Active);
		}

		[Fact]
		public void Selecting_another_workspace_changes_only_the_active_one()
		{
			WorkspaceState state = new(Workspace.Play);
			Assert.True(state.Select(Workspace.Remaster));
			Assert.Equal(Workspace.Remaster, state.Active);
			Assert.False(state.IsPlay);
		}

		[Fact]
		public void Selecting_the_active_workspace_is_a_no_op()
		{
			WorkspaceState state = new(Workspace.Share);
			Assert.False(state.Select(Workspace.Share));
			Assert.Equal(Workspace.Share, state.Active);
		}

		[Fact]
		public void Exactly_one_workspace_is_active_after_any_sequence_of_switches()
		{
			WorkspaceState state = new();
			foreach(Workspace w in new[] { Workspace.Share, Workspace.Remaster, Workspace.Classic, Workspace.Remaster, Workspace.Play, Workspace.Classic, Workspace.Share }) {
				state.Select(w);
				Assert.Single(state.Rows, r => r.IsActive);
				Assert.Equal(w, state.Rows.Single(r => r.IsActive).Workspace);
			}
		}

		[Fact]
		public void Unknown_persisted_value_falls_back_to_play()
		{
			//A settings.json edited by hand (or from a later build) must not
			//leave the window with no workspace on screen.
			Assert.Equal(Workspace.Play, new WorkspaceState((Workspace)42).Active);
		}

		[Theory]
		//Play: hidden only while a game runs and nothing is paused (W-S1).
		[InlineData(Workspace.Play, false, false, true)]
		[InlineData(Workspace.Play, false, true, true)]
		[InlineData(Workspace.Play, true, false, false)]
		[InlineData(Workspace.Play, true, true, true)]
		//Remaster and Share: always visible.
		[InlineData(Workspace.Remaster, true, false, true)]
		[InlineData(Workspace.Remaster, false, false, true)]
		[InlineData(Workspace.Share, true, false, true)]
		[InlineData(Workspace.Share, true, true, true)]
		//Classic (ADR-0250): the original GUI has no shell bar - its switcher
		//is the Workspace menu of the classic menu bar.
		[InlineData(Workspace.Classic, false, false, false)]
		[InlineData(Workspace.Classic, true, false, false)]
		[InlineData(Workspace.Classic, true, true, false)]
		public void Bar_visibility(Workspace workspace, bool gameRunning, bool paused, bool expected)
		{
			Assert.Equal(expected, WorkspaceShell.IsBarVisible(workspace, gameRunning, paused));
		}

		//W-P5 (final audit): the first-start pack picker opens over the running,
		//un-enhanced game without pausing it; the render keeps the bar and the
		//status line around its scrim, so a Play sheet on screen shows them too.
		[Fact]
		public void A_play_sheet_over_a_running_game_shows_the_bar()
		{
			Assert.True(WorkspaceShell.IsBarVisible(Workspace.Play, gameRunning: true, paused: false, sheetOpen: true));
			Assert.False(WorkspaceShell.IsBarVisible(Workspace.Play, gameRunning: true, paused: false, sheetOpen: false));
		}
	
		//ADR-0250 Decision 2: Classic owns UiMode.Advanced; every task door is Player.
		[Theory]
		[InlineData(Workspace.Play, UiMode.Player)]
		[InlineData(Workspace.Remaster, UiMode.Player)]
		[InlineData(Workspace.Share, UiMode.Player)]
		[InlineData(Workspace.Classic, UiMode.Advanced)]
		public void Each_door_has_its_ui_mode(Workspace door, UiMode expected)
		{
			Assert.Equal(expected, WorkspaceShell.UiModeFor(door));
		}

		//Play is the default door: a fresh install (Player, no Workspace key ->
		//Play), and any Player settings file keeps the task door it saved. An
		//upgraded install whose UiMode is Advanced opens in Classic, so nobody
		//loses the GUI they chose; an unknown value opens in Play.
		[Theory]
		[InlineData(UiMode.Player, Workspace.Play, Workspace.Play)]
		[InlineData(UiMode.Player, Workspace.Remaster, Workspace.Remaster)]
		[InlineData(UiMode.Player, Workspace.Share, Workspace.Share)]
		[InlineData(UiMode.Player, Workspace.Classic, Workspace.Play)]
		[InlineData(UiMode.Advanced, Workspace.Play, Workspace.Classic)]
		[InlineData(UiMode.Advanced, Workspace.Share, Workspace.Classic)]
		[InlineData(UiMode.Advanced, Workspace.Classic, Workspace.Classic)]
		[InlineData(UiMode.Player, (Workspace)42, Workspace.Play)]
		public void Initial_door(UiMode uiMode, Workspace persisted, Workspace expected)
		{
			Assert.Equal(expected, WorkspaceShell.InitialDoor(uiMode, persisted));
		}

		[Fact]
		public void A_fresh_install_opens_in_play_and_an_upgrade_in_classic()
		{
			Assert.Equal(Workspace.Play, WorkspaceShell.InitialDoor(UiModeDefaultRule.ForMissingKey(settingsFileExists: false), default));
			Assert.Equal(Workspace.Classic, WorkspaceShell.InitialDoor(UiModeDefaultRule.ForMissingKey(settingsFileExists: true), default));
		}

		//Every place that flips UiMode goes through the door: Advanced means
		//Classic, and Player leaves Classic for Play (a task door stays put).
		[Theory]
		[InlineData(UiMode.Advanced, Workspace.Play, Workspace.Classic)]
		[InlineData(UiMode.Advanced, Workspace.Remaster, Workspace.Classic)]
		[InlineData(UiMode.Advanced, Workspace.Classic, Workspace.Classic)]
		[InlineData(UiMode.Player, Workspace.Classic, Workspace.Play)]
		[InlineData(UiMode.Player, Workspace.Share, Workspace.Share)]
		[InlineData(UiMode.Player, Workspace.Play, Workspace.Play)]
		public void Door_after_a_ui_mode_change(UiMode uiMode, Workspace current, Workspace expected)
		{
			Assert.Equal(expected, WorkspaceShell.DoorForUiMode(uiMode, current));
		}

		//The game's own screen (renderer, home, music player) is shown by Play
		//and by Classic, the original GUI's plain game view.
		[Theory]
		[InlineData(Workspace.Play, true)]
		[InlineData(Workspace.Classic, true)]
		[InlineData(Workspace.Remaster, false)]
		[InlineData(Workspace.Share, false)]
		public void Game_screen_doors(Workspace door, bool expected)
		{
			Assert.Equal(expected, WorkspaceShell.ShowsGameScreen(door));
		}
	}
}
