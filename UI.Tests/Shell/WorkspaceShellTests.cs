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
		public void Switcher_order_is_fixed_play_remaster_share()
		{
			Assert.Equal(new[] { Workspace.Play, Workspace.Remaster, Workspace.Share }, WorkspaceShell.Ordered.ToArray());
		}

		[Theory]
		[InlineData(1, Workspace.Play)]
		[InlineData(2, Workspace.Remaster)]
		[InlineData(3, Workspace.Share)]
		public void Shortcut_digit_selects_the_row_at_that_position(int digit, Workspace expected)
		{
			Assert.Equal(expected, WorkspaceShell.FromShortcutDigit(digit));
			Assert.Equal(digit, WorkspaceShell.ShortcutDigit(expected));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(4)]
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
			Assert.Equal(new[] { false, false, true }, state.Rows.Select(r => r.IsActive).ToArray());

			state.Select(Workspace.Play);
			Assert.Equal(WorkspaceShell.Ordered.ToArray(), state.Rows.Select(r => r.Workspace).ToArray());
			Assert.Equal(new[] { true, false, false }, state.Rows.Select(r => r.IsActive).ToArray());
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
			foreach(Workspace w in new[] { Workspace.Share, Workspace.Remaster, Workspace.Remaster, Workspace.Play, Workspace.Share }) {
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
	}
}
