using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//PRD Part B §6, §13.5.2 W-P8 (G.4): in Player mode the Settings page shows only
//the essentials, as one strip in this order: Display | Look | Audio | Controls | System
//(ADR-0256).
//Display is the window (full screen, aspect ratio, scale) and Look (W-P10,
//ADR-0246) is what the pixels look like, so Advanced's Video tab is not part of
//Play; everything else is Classic › Settings. The ConfigWindow shows a separate
//tab strip in Player mode, bound through this order, and the initial tab is
//clamped here so a non-essentials selection (e.g. Preferences from the
//Advanced GUI path) cannot land on a hidden tab.
public enum PlayerSettingsRowKind { Switch, Slider, Picker, Status }

public sealed record PlayerSettingsRow(string Id, PlayerSettingsRowKind Kind);

public static class PlayerSettingsEssentials
{
	public static readonly ConfigWindowTab[] Tabs = {
		ConfigWindowTab.Display,
		ConfigWindowTab.Look,
		ConfigWindowTab.Audio,
		ConfigWindowTab.Input,
		//ADR-0256 Decision 8: the first run's two questions (storage, keyboard
		//preset) are a Settings surface the pad can drive, and this is the strip
		//it lives on. Not a fifth door (ADR-0250): the tab is inside Settings.
		ConfigWindowTab.System
	};

	public static bool IsEssentials(ConfigWindowTab tab) => Array.IndexOf(Tabs, tab) >= 0;

	//W-P8 opens on Display.
	public static ConfigWindowTab ClampToEssentials(ConfigWindowTab tab)
	{
		return IsEssentials(tab) ? tab : ConfigWindowTab.Display;
	}

	public static int IndexOf(ConfigWindowTab tab) => Array.IndexOf(Tabs, tab);

	//W-P8 is a 340 px sheet - three rows, then the "Everything else" hint (or,
	//on Audio and Controls, the "More in Options..." link in the hint's place)
	//right under the group ("Everything else: Classic › Settings", ADR-0250
	//Decisions 2 and 3: Play's Tools ⋯ has no Options entry), and Done; Look is
	//W-P10's taller 480 px sheet (ADR-0249: a sheet in the main window, not a
	//window), and Play's System tab (ADR-0256 Decision 8) needs the same room: two storage choices with their
	//folder lines, two keyboard choices, and the restart line a folder change
	//puts there.
	public static double SheetHeight(ConfigWindowTab tab) => tab switch {
		ConfigWindowTab.Look or ConfigWindowTab.System => 480,
		//#1111: Display's fourth row (Interface size) is one 46 px row and its hairline taller.
		ConfigWindowTab.Display => 387,
		//#1105: Audio's fourth row (Menu sounds) needs one more row's height, but
		//only while the host can play it.
		ConfigWindowTab.Audio when MenuSoundsAvailable() => 388,
		ConfigWindowTab.Input when MenuTickAimable() => 388,
		_ => 340
	};

	//#1105: the Menu sounds row is shown only while the host reports an audio path
	//for it (App wires EmuApi.MenuSoundsAvailable). None exists yet (the audio path is its
	//own ADR), so today the row is hidden and Audio keeps its three rows. A test
	//swaps this seam to exercise the row.
	public static Func<bool> MenuSoundsAvailable { get; set; } = () => false;

	//#1112: the Menu tick row is shown only while the host reports the pad in the
	//player's hand as aimable (App wires HapticTickOutput.PadInHandAimable); with no
	//pad in hand, or on a backend that cannot aim, there is no row at all. A test
	//swaps this seam.
	public static Func<bool> MenuTickAimable { get; set; } = () => false;

	//#852: the strip's segment width. ADR-0249's sheet is 480 px wide behind
	//19 px of padding a side, and the reference mockups (docs/media/
	//gui-redesign/W-P8..W-P11.png) draw the strip at four tabs, where 96 px
	//segments leave the track centered and narrower than the sheet. ADR-0256
	//Decision 8 added the fifth (System) and the fixed 96 px ran 42 px past the
	//sheet's right edge, so the last label rendered as "Syst": the mockups were
	//drawn before the tab existed (since #1006 they draw all five, 84 px each). A segment is 96 px while 96 px still fits the
	//width the track is handed, and the segments share it evenly once they do
	//not, so no tab count can overflow the sheet again. Pure, so the rule is
	//pinned host-free in UI.Tests/Play/PlayerSettingsStripTests; the rendered
	//result is in UI.HeadlessTests (PlayerThemeSettingsRenderTests).
	public const double MaxSegment = 96;

	public static double SegmentWidth(double available, int count)
	{
		//Measure can hand over Infinity; dividing by it would be a NaN width,
		//and a NaN in a Rect arranges children nowhere.
		if(count <= 0 || double.IsNaN(available) || double.IsInfinity(available)) {
			return MaxSegment;
		}
		return Math.Min(MaxSegment, available / count);
	}

	//PRD rule 2: an inset list of at most three rows per essentials tab, except
	//Display's fourth (#1111, Interface size), which still fits the 7 elements,
	//and the fourth of Audio (Menu sounds, #1105) and Controls (Menu tick, #1112)
	//while the host reports it available.
	public const int MaxRows = 4;
	public static int MaxRowsFor(ConfigWindowTab tab) => tab switch {
		ConfigWindowTab.Display => 4,
		ConfigWindowTab.Audio when MenuSoundsAvailable() => 4,
		ConfigWindowTab.Input when MenuTickAimable() => 4,
		_ => 3
	};

	private static readonly PlayerSettingsRow[] DisplayRows = {
		new("Fullscreen", PlayerSettingsRowKind.Switch),
		new("AspectRatio", PlayerSettingsRowKind.Picker),
		new("Scale", PlayerSettingsRowKind.Picker),
		//#1111: scales Play's chrome, never the picture (Scale's meaning).
		new("InterfaceSize", PlayerSettingsRowKind.Picker)
	};
	private static readonly PlayerSettingsRow[] AudioRows = {
		new("Sound", PlayerSettingsRowKind.Switch),
		new("Volume", PlayerSettingsRowKind.Slider),
		new("OutputDevice", PlayerSettingsRowKind.Picker)
	};
	//#1105: soft sounds on move / confirm / back, off until turned on.
	private static readonly PlayerSettingsRow[] AudioRowsWithMenuSounds = AudioRows
		.Append(new("MenuSounds", PlayerSettingsRowKind.Switch)).ToArray();
	//Per-player controller types live in each console's own config, so the
	//essentials are what is console-independent: which pads are connected,
	//rumble strength and stick deadzone (InputConfig).
	private static readonly PlayerSettingsRow[] ControlsRows = {
		new("Controllers", PlayerSettingsRowKind.Status),
		new("Rumble", PlayerSettingsRowKind.Slider),
		new("Deadzone", PlayerSettingsRowKind.Slider)
	};
	//#1112: the optional focus-move tick, off until turned on.
	private static readonly PlayerSettingsRow[] ControlsRowsWithMenuTick = ControlsRows
		.Append(new("MenuTick", PlayerSettingsRowKind.Switch)).ToArray();

	//The rows of a tab's inset list; Look has its own W-P10 page (empty here).
	public static IReadOnlyList<PlayerSettingsRow> Rows(ConfigWindowTab tab) => tab switch {
		ConfigWindowTab.Display => DisplayRows,
		ConfigWindowTab.Audio => MenuSoundsAvailable() ? AudioRowsWithMenuSounds : AudioRows,
		ConfigWindowTab.Input => MenuTickAimable() ? ControlsRowsWithMenuTick : ControlsRows,
		_ => Array.Empty<PlayerSettingsRow>()
	};

	//No essentials tab embeds a classic option page any more (the bug:
	//Audio and Controls did - sub-tabs, a per-console button row, scrollbars).
	public static bool EmbedsClassicPage(ConfigWindowTab tab) => false;

	//The classic Options page "More in Options..." expands to; null when the tab
	//has no such link (Display keeps the hint). Controls' row asks the window for
	//the Play Controller sheet first (ADR-0255 slice 1); its Input page is what a
	//view nobody wired still expands to.
	public static ConfigWindowTab? OptionsTabFor(ConfigWindowTab tab) => tab switch {
		ConfigWindowTab.Audio => ConfigWindowTab.Audio,
		ConfigWindowTab.Input => ConfigWindowTab.Input,
		ConfigWindowTab.Look => ConfigWindowTab.Video,
		_ => null
	};

	public static ConfigWindowTab? TabAt(int index) => index >= 0 && index < Tabs.Length ? Tabs[index] : null;

	//#910 (PRD Part B §13.5.2 W-P4/W-P8): "Exit fullscreen moves into Settings".
	//The explicit control is offered only while the window is fullscreen - in a
	//window there is nothing to exit, and the Fullscreen switch already says so -
	//and only on Display, the tab that owns the window. It sits in Done's row,
	//where Look keeps its own footer (Hold to Compare), so it never shows on Look.
	public static bool ShowsExitFullscreen(ConfigWindowTab? tab, bool isFullscreen)
	{
		return isFullscreen && tab == ConfigWindowTab.Display;
	}
}

//W-P8's Display rows. A value set elsewhere that is not in the short list is
//shown as the current item and never rewritten (§6.1's restore-not-clobber,
//kept), the same rule Look applies to Pixels (ADR-0246).
public static class PlayDisplaySettings
{
	public static readonly double[] Scales = { 1, 2, 3, 4, 5, 6 };

	//A scale within a hundredth of a whole number reads as that number (the
	//window rounds to two places when maximized, MainWindow.ProcessResolutionChange).
	public static double? WholeScale(double scale)
	{
		double rounded = Math.Round(scale);
		return Math.Abs(scale - rounded) < 0.01 && rounded >= 1 ? rounded : null;
	}

	//The popup's selection: the current value when it is listed, else the
	//nearest listed one - a window under 1× (or none yet, 0) is not listed,
	//and a blank popup reads as broken (W-P8).
	public static double Nearest(IReadOnlyList<double> items, double current)
	{
		return items.OrderBy(v => Math.Abs(v - current)).First();
	}

	//The popup's items: the short list, plus the current value at the end when
	//it is not one of them.
	public static List<T> ItemsWithCurrent<T>(IReadOnlyList<T> shortList, T current)
	{
		List<T> items = new(shortList);
		if(!items.Contains(current)) {
			items.Add(current);
		}
		return items;
	}
}

//W-P8's sliders edit the classic pages' unsigned config values.
public static class PlayerSliders
{
	public static uint ToConfig(double value, uint max)
	{
		return (uint)Math.Min(max, Math.Max(0, Math.Round(value)));
	}
}

//W-P8's Audio rows. The device picker lists what is enumerated plus a device set
//in Options that is not enumerated now, as the current item, never replaced.
public static class PlayerAudioSettings
{
	public static List<string> Devices(IReadOnlyList<string> enumerated, string current)
	{
		return current == "" ? new List<string>(enumerated) : PlayDisplaySettings.ItemsWithCurrent(enumerated, current);
	}
}
