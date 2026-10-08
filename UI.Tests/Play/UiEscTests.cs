using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1080: Esc in Player mode is answered at the window's own keyboard as well as
//by the core's shortcut handler, and UiEsc is the one place both callers resolve
//what the press means. The pairing asserted here is the whole fix: a press the
//window owns has to be one Player mode's overlay (or its Settings sheet) answers,
//and every other context has to stay the core's - the Remaster and Share arms are
//the classic GUI's Esc meanings and moving them would change Advanced.
public class UiEscTests
{
	//A context with nothing up: Player mode, the Play workspace, every sheet
	//closed, no Remaster game view, not Share.
	private static UiEscAction Player(bool playWorkspace = true, bool bios = false, bool selectRom = false,
		bool tool = false, bool settings = false, bool remasterGameView = false, bool share = false)
	{
		return UiEsc.For(true, playWorkspace, bios, selectRom, tool, settings, remasterGameView, share);
	}

	private static UiEscAction Advanced(bool playWorkspace = true, bool bios = false, bool selectRom = false,
		bool tool = false, bool settings = false, bool remasterGameView = false, bool share = false)
	{
		return UiEsc.For(false, playWorkspace, bios, selectRom, tool, settings, remasterGameView, share);
	}

	[Fact]
	public void Player_mode_in_play_is_the_overlays_and_the_window_takes_it()
	{
		UiEscAction action = Player();
		Assert.Equal(UiEscAction.TogglePlayerOverlay, action);
		Assert.True(UiEsc.UiTakesThePress(action));
	}

	//ADR-0249: the three sheets that show in every workspace - the BIOS sheet
	//(W-P13), an archive's game list, and ADR-0250's tool sheet - are answered by
	//TogglePlayerOverlay first, wherever the player opened them from.
	[Theory]
	[InlineData(false, true, false, false)]
	[InlineData(false, false, true, false)]
	[InlineData(false, false, false, true)]
	public void A_sheet_that_shows_in_every_workspace_is_still_the_overlays(bool bios, bool selectRom, bool tool, bool classic)
	{
		UiEscAction action = Player(playWorkspace: classic, bios: bios, selectRom: selectRom, tool: tool);
		Assert.Equal(UiEscAction.TogglePlayerOverlay, action);
		Assert.True(UiEsc.UiTakesThePress(action));
	}

	//ADR-0250: Settings… opened from Remaster's or Share's Tools ⋯ closes on Esc,
	//keeping what was changed - the other Player-mode arm the window answers.
	[Fact]
	public void Settings_outside_play_closes_and_the_window_takes_it()
	{
		UiEscAction action = Player(playWorkspace: false, settings: true);
		Assert.Equal(UiEscAction.ClosePlayerSettings, action);
		Assert.True(UiEsc.UiTakesThePress(action));
	}

	//The play arm is asked first: a Settings sheet opened over Play is still the
	//overlay's press (it closes back to W-P4, which PlayEsc walks).
	[Fact]
	public void The_overlay_arm_outranks_the_settings_arm()
	{
		Assert.Equal(UiEscAction.TogglePlayerOverlay, Player(settings: true));
	}

	//Advanced keeps Esc as the core has it (UiModeShortcutPrecedence suppresses
	//the overlay's binding, not Pause's): the window must not take the press at
	//all, or the classic Pause would be swallowed by a Player-only surface.
	[Theory]
	[InlineData(true, false, false, false, false, false, false)]
	[InlineData(false, true, false, false, false, false, false)]
	[InlineData(false, false, true, false, false, false, false)]
	[InlineData(false, false, false, true, false, false, false)]
	[InlineData(false, false, false, false, true, false, false)]
	public void Advanced_leaves_every_context_to_the_core(bool play, bool bios, bool selectRom, bool tool, bool settings, bool remaster, bool share)
	{
		UiEscAction action = Advanced(play, bios, selectRom, tool, settings, remaster, share);
		Assert.Equal(UiEscAction.None, action);
		Assert.False(UiEsc.UiTakesThePress(action));
	}

	//G.3/G.6 and G.8: the Remaster game view and Share keep their own meanings,
	//and they stay the core's press - the window does not take them.
	[Fact]
	public void Remasters_game_view_and_share_resolve_but_stay_the_cores()
	{
		UiEscAction remaster = Player(playWorkspace: false, remasterGameView: true);
		Assert.Equal(UiEscAction.LeaveRemasterGameView, remaster);
		Assert.False(UiEsc.UiTakesThePress(remaster));

		UiEscAction share = Player(playWorkspace: false, share: true);
		Assert.Equal(UiEscAction.ShareEsc, share);
		Assert.False(UiEsc.UiTakesThePress(share));
	}

	//Player mode on a workspace with nothing of Play's up: nothing to answer, and
	//the window must let the press through untouched.
	[Fact]
	public void Player_mode_away_from_every_surface_answers_nothing()
	{
		UiEscAction action = Player(playWorkspace: false);
		Assert.Equal(UiEscAction.None, action);
		Assert.False(UiEsc.UiTakesThePress(action));
	}
}
