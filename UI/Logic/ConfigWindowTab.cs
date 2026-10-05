namespace Mesen.Logic;

//The tabs of the ConfigWindow (Settings). Kept host-free in UI/Logic so the
//Player-mode essentials decision (PlayerSettingsEssentials) is unit-testable
//without the Avalonia window. The values are ids, not TabControl indexes:
//removed systems leave holes (never reuse an id), and the window's markup
//order is ConfigWindowTabOrder's (ADR-0246, P.13).
public enum ConfigWindowTab
{
	Audio = 0,
	Emulation = 1,
	Input = 2,
	Video = 3,
	//separator
	Nes = 5,
	// 6 was Snes — do not reuse
	Gameboy = 7,
	Gba = 8,
	// 9 was PcEngine — do not reuse
	Sms = 10,
	// 11 was Ws — do not reuse
	// 12 was OtherConsoles (ColecoVision) — do not reuse
	//separator
	Preferences = 14,
	//ADR-0246 (P.13): Settings › Look - Art / Pixels / Screen.
	Look = 15,
	//G.4 (W-P8): Play's Settings › Display - the window, not the pixels. Shown
	//only in Player mode; not part of ConfigWindowTabOrder (the Advanced tabs).
	Display = 16,
	//ADR-0256 Decision 8: Play's Settings › System - the two things the retired
	//first-run wizard asked (where the settings file lives, which keyboard
	//preset), moved to a surface the pad drives. Player mode only, like Display;
	//not part of ConfigWindowTabOrder.
	System = 17
}

//The ConfigWindow.axaml TabControl, in markup order; null is a separator row
//(also a TabItem). The window binds its SelectedIndex through this, so a tab
//id never has to equal its position. Until P.13 the enum value was bound as
//the index directly, which sent Game Boy, GBA, SMS and Preferences one or
//more rows off once removed consoles had left holes in the ids.
public static class ConfigWindowTabOrder
{
	public static readonly ConfigWindowTab?[] Tabs = {
		ConfigWindowTab.Audio,
		ConfigWindowTab.Emulation,
		ConfigWindowTab.Input,
		ConfigWindowTab.Video,
		ConfigWindowTab.Look,
		null,
		ConfigWindowTab.Nes,
		ConfigWindowTab.Gameboy,
		ConfigWindowTab.Gba,
		ConfigWindowTab.Sms,
		null,
		ConfigWindowTab.Preferences
	};

	public static int IndexOf(ConfigWindowTab tab) => System.Array.IndexOf(Tabs, (ConfigWindowTab?)tab);

	public static ConfigWindowTab? TabAt(int index) => index >= 0 && index < Tabs.Length ? Tabs[index] : null;
}
