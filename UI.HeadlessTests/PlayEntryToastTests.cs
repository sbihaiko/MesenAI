using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0251: the W-P3 entry toast teaches the way into W-P4 for the first three
//game starts, and ToggleOverlay has a controller binding by default. The rule
//is pinned host-free in UI.Tests/Play/PlayMenuHintTests; this checks the real
//GameLoaded path shows it, spends the count, and stops after the third start.
public partial class PlayEdgeFlowsTests
{
	//The headless Core has no key manager (every key name is empty), so the
	//tests hand the toast the default slots' names and the pad count. The third
	//slot is the pad binding (ADR-0255 slice 4), empty for a config that has none.
	private static readonly List<string> EscSlot = new() { "Esc" };
	private static readonly List<string> SelectStartSlot = new() { "Pad1 Select", "Pad1 Start" };
	private static readonly List<string> NoPadSlot = new();

	private static void UseDefaultBindings(MainWindowViewModel model, uint pads)
	{
		model.OverlayBindingKeyNames = () => (EscSlot, SelectStartSlot, NoPadSlot);
		model.ConnectedGamepadCount = () => pads;
	}

	//The window stays open after the test and still hears the next test's
	//GameLoaded: without bindings it shows no hint, so it spends no start.
	private static void ForgetBindings(MainWindowViewModel model)
	{
		model.OverlayBindingKeyNames = () => (new List<string>(), new List<string>(), new List<string>());
	}

	[AvaloniaFact]
	public void A_game_start_teaches_the_pause_menu_in_its_toast()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		int saved = prefs.PlayMenuHintsShown;
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			UseDefaultBindings(model, pads: 0);
			prefs.PlayMenuHintsShown = 0;
			RunSyntheticGame(model);
			WaitFor(() => model.LastEntryToast != null, "the game start showed no entry toast");

			//The synthetic game has no pack: the toast is the hint alone.
			Assert.Equal(new PlayEntryToast("GameLoaded", "Esc for the menu", "", true), model.LastEntryToast);
			Assert.Equal(1, prefs.PlayMenuHintsShown);

			//W-P4 opens the way the hint says, through the shortcut handler.
			new ShortcutHandler(window).ExecuteShortcut(EmulatorShortcut.ToggleOverlay);
			WaitFor(() => model.IsPlayerOverlayVisible, "the overlay shortcut did not open W-P4");
		} finally {
			EmuApi.Stop();
			ForgetBindings(model);
			prefs.PlayMenuHintsShown = saved;
		}
	}

	[AvaloniaFact]
	public void A_controller_start_names_the_controller_binding()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		int saved = prefs.PlayMenuHintsShown;
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			UseDefaultBindings(model, pads: 1);
			prefs.PlayMenuHintsShown = 2;
			RunSyntheticGame(model);
			WaitFor(() => model.LastEntryToast != null, "the game start showed no entry toast");
			Assert.Equal("Select+Start for the menu", model.LastEntryToast!.Message);
			Assert.Equal(3, prefs.PlayMenuHintsShown);
		} finally {
			EmuApi.Stop();
			ForgetBindings(model);
			prefs.PlayMenuHintsShown = saved;
		}
	}

	[AvaloniaFact]
	public void After_the_third_start_the_toast_no_longer_teaches()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		int saved = prefs.PlayMenuHintsShown;
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			UseDefaultBindings(model, pads: 0);
			prefs.PlayMenuHintsShown = PlayMenuHint.Starts;
			RunSyntheticGame(model);
			Dispatcher.UIThread.RunJobs();

			//No pack and no hint left: nothing to show, nothing spent.
			Assert.Null(model.LastEntryToast);
			Assert.Equal(PlayMenuHint.Starts, prefs.PlayMenuHintsShown);
		} finally {
			EmuApi.Stop();
			ForgetBindings(model);
			prefs.PlayMenuHintsShown = saved;
		}
	}

	//An upgrade seeds the controller binding (no platform reports Home today,
	//so Select+Start) into the empty second slot only. The key codes are a
	//stand-in table: the headless Core has no key manager.
	[AvaloniaFact]
	public void An_upgrade_seeds_the_controller_binding_only_into_an_empty_slot()
	{
		Dictionary<string, UInt16> codes = new() { ["Esc"] = 1, ["F6"] = 2, ["Pad1 Select"] = 0x1007, ["Pad1 Start"] = 0x1006 };
		UInt16 Code(string name) => codes.TryGetValue(name, out UInt16 code) ? code : (UInt16)0;

		PreferencesConfig upgraded = new();
		upgraded.ShortcutKeys.Add(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.Pause, KeyCombination = new() { Key1 = 1 } });
		ShortcutKeyInfo overlay = new() { Shortcut = EmulatorShortcut.ToggleOverlay, KeyCombination = new() { Key1 = 1 } };
		upgraded.ShortcutKeys.Add(overlay);

		upgraded.SeedOverlayControllerBinding(Code);
		Assert.Equal((0x1007, 0x1006, 0), (overlay.KeyCombination2.Key1, overlay.KeyCombination2.Key2, overlay.KeyCombination2.Key3));
		Assert.Equal(1, overlay.KeyCombination.Key1);

		//A binding the user made is kept.
		KeyCombination mine = new() { Key1 = 2 };
		overlay.KeyCombination2 = mine;
		upgraded.SeedOverlayControllerBinding(Code);
		Assert.Same(mine, overlay.KeyCombination2);

		//A combination another shortcut already uses is not taken from it.
		overlay.KeyCombination2 = new KeyCombination();
		upgraded.ShortcutKeys[0].KeyCombination2 = new() { Key1 = 0x1007, Key2 = 0x1006 };
		upgraded.SeedOverlayControllerBinding(Code);
		Assert.True(overlay.KeyCombination2.IsEmpty);
	}
}
