using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//PRD Part B §6, §13.5.2 W-P8 (G.4): in Player mode the Settings page shows only
//the essentials, as one strip in this order: Display | Look | Audio | Controls.
//Display is the window (full screen, aspect ratio, scale) and Look (W-P10,
//ADR-0246) is what the pixels look like, so Advanced's Video tab is not part of
//Play; everything else is Tools ⋯ › Options. The ConfigWindow shows a separate
//tab strip in Player mode, bound through this order, and the initial tab is
//clamped here so a non-essentials selection (e.g. Preferences from the
//Advanced GUI path) cannot land on a hidden tab.
public static class PlayerSettingsEssentials
{
	public static readonly ConfigWindowTab[] Tabs = {
		ConfigWindowTab.Display,
		ConfigWindowTab.Look,
		ConfigWindowTab.Audio,
		ConfigWindowTab.Input
	};

	public static bool IsEssentials(ConfigWindowTab tab) => Array.IndexOf(Tabs, tab) >= 0;

	//W-P8 opens on Display.
	public static ConfigWindowTab ClampToEssentials(ConfigWindowTab tab)
	{
		return IsEssentials(tab) ? tab : ConfigWindowTab.Display;
	}

	public static int IndexOf(ConfigWindowTab tab) => Array.IndexOf(Tabs, tab);

	//W-P8 is a 340 px sheet - its three rows, then the "Everything else" hint
	//right under the group and Done; Look (W-P10), Audio and Controls keep the
	//500 px sheet their pages need.
	public static double SheetHeight(ConfigWindowTab tab) => tab == ConfigWindowTab.Display ? 340 : 500;

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
