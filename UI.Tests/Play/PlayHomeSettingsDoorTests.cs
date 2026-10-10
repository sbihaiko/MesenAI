using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Play;

//#1177: with no game loaded the pad drives the GUI (ADR-0256 Decision 2) and the
//header is unpadded (#1137), so the home needs a way to Settings that does not
//change its layout (ADR-0241: W-P1 one control, W-P2 two besides the tiles). The
//pad's Start button (Options on a DualShock) is that door; these are its
//host-free rules - which pad button, when it answers, what the bar names.
public class PlayHomeSettingsDoorTests
{
	private static ushort KeyCode(string name) => name switch {
		"Pad1 Start" => 0x1010,
		"Joy1 But10" => 0x2010,
		_ => (ushort)0
	};

	[Theory]
	[InlineData(PadFamily.Xbox, "Pad1 Start")]
	[InlineData(PadFamily.Ps4, "Joy1 But10")]
	public void The_settings_button_is_the_familys_own_start(PadFamily family, string expected)
	{
		Assert.Equal(KeyCode(expected), PadNavControls.SheetCode(family, 0, PadSheetControl.Settings, KeyCode));
	}

	[Theory]
	[InlineData(true, true, false, true)]
	[InlineData(true, false, true, true)]
	[InlineData(true, false, false, false)]
	[InlineData(false, true, false, false)]
	[InlineData(false, false, true, false)]
	public void Start_answers_only_while_the_home_is_what_the_window_shows(bool playWorkspace, bool firstRun, bool recents, bool answers)
	{
		Assert.Equal(answers, PlayHome.PadOpensSettings(playWorkspace, firstRun, recents));
	}

	[Fact]
	public void The_home_bar_names_Start_as_Settings_for_each_pad()
	{
		IReadOnlyList<PlayBarEntry> bar = PlayBarDeclarations.WithSettings(PlayBarDeclarations.Home);
		Assert.Equal("A BarPlay     Start BarSettings", PlayActionBar.Text(bar, PlayInputDevice.Controller, PadFamily.Xbox, false, key => key));
		Assert.Equal("Cross BarPlay     Options BarSettings", PlayActionBar.Text(bar, PlayInputDevice.Controller, PadFamily.Ps4, false, key => key));
	}

	[Fact]
	public void The_first_run_bar_names_it_too_and_the_keyboard_bar_does_not()
	{
		IReadOnlyList<PlayBarEntry> bar = PlayBarDeclarations.WithSettings(PlayBarDeclarations.HomeFirstRun);
		Assert.Equal("A BarOpenGame     Start BarSettings", PlayActionBar.Text(bar, PlayInputDevice.Controller, PadFamily.Xbox, false, key => key));
		Assert.Equal("Enter BarOpenGame", PlayActionBar.Text(bar, PlayInputDevice.Keyboard, null, false, key => key));
	}

	[Fact]
	public void Settings_is_not_repeated_when_applied_twice()
	{
		IReadOnlyList<PlayBarEntry> twice = PlayBarDeclarations.WithSettings(PlayBarDeclarations.WithSettings(PlayBarDeclarations.Home));
		Assert.Single(twice, e => e.Action == PlayAction.Settings);
	}
}
