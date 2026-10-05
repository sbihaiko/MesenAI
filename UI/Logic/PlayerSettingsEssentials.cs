using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//PRD Part B §6, §13.5.2 W-P8 (G.4): in Player mode the Settings page shows only
//the essentials, as one strip in this order: Display | Look | Audio | Controls.
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
	//right under the group, and Done; Look is W-P10's taller 480 px sheet
	//(ADR-0249: a sheet in the main window, not a window), and Play's System tab
	//(ADR-0256 Decision 8) needs the same room: two storage choices with their
	//folder lines, two keyboard choices, and the restart line a folder change
	//puts there.
	public static double SheetHeight(ConfigWindowTab tab) => tab is ConfigWindowTab.Look or ConfigWindowTab.System ? 480 : 340;

	//PRD rule 2: an inset list of at most three rows per essentials tab.
	public const int MaxRows = 3;

	private static readonly PlayerSettingsRow[] DisplayRows = {
		new("Fullscreen", PlayerSettingsRowKind.Switch),
		new("AspectRatio", PlayerSettingsRowKind.Picker),
		new("Scale", PlayerSettingsRowKind.Picker)
	};
	private static readonly PlayerSettingsRow[] AudioRows = {
		new("Sound", PlayerSettingsRowKind.Switch),
		new("Volume", PlayerSettingsRowKind.Slider),
		new("OutputDevice", PlayerSettingsRowKind.Picker)
	};
	//Per-player controller types live in each console's own config, so the
	//essentials are what is console-independent: which pads are connected,
	//rumble strength and stick deadzone (InputConfig).
	private static readonly PlayerSettingsRow[] ControlsRows = {
		new("Controllers", PlayerSettingsRowKind.Status),
		new("Rumble", PlayerSettingsRowKind.Slider),
		new("Deadzone", PlayerSettingsRowKind.Slider)
	};

	//The rows of a tab's inset list; Look has its own W-P10 page (empty here).
	public static IReadOnlyList<PlayerSettingsRow> Rows(ConfigWindowTab tab) => tab switch {
		ConfigWindowTab.Display => DisplayRows,
		ConfigWindowTab.Audio => AudioRows,
		ConfigWindowTab.Input => ControlsRows,
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
