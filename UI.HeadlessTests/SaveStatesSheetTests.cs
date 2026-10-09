using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#909 (PRD Part B §13.5.2 W-P4): W-P4's Save states sheet is ONE grid. The rules
//(which actions a row offers, which are enabled, where the sheet opens focused)
//are pinned host-free in UI.Tests/Play/SaveStateSheetTests; this checks the
//crossing into the window against the real core - eleven rows, *Save here* and
//*Load* per slot with Load disabled while the slot is empty, the auto-save row
//offering Load alone, a save landing in the slot it names, and *Load* returning
//to the game.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class SaveStatesSheetTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-909-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Preferences.PauseWhenInBackground = _pauseInBackground;
		ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		//The headless window is never focused; without these the core would hold
		//the game paused and the case could not tell "paused by W-P4" from
		//"paused because nobody is looking".
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		return (window, model);
	}

	//A ROM whose name no other case in the suite uses, so the slots this case
	//writes are the slots it reads.
	private MainWindowViewModel LoadGameWithOwnSlots(MainWindow window, MainWindowViewModel model)
	{
		Directory.CreateDirectory(_folder);
		string rom = Path.Combine(_folder, "savesheet-" + Guid.NewGuid().ToString("N") + ".nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
		return model;
	}

	private static void OpenSaveStatesSheet(MainWindow window, MainWindowViewModel model)
	{
		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
		window.FindNamed<Button>("OverlaySaveStatesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		WaitFor(() => model.IsSaveStatesSheetVisible, "W-P4's Save states row did not open its sheet");
	}

	//The buttons of one row, by the row's own container - a grid of rows has no
	//per-slot names to look up. Either may be absent (the auto-save row has no
	//*Save here*), so both are answered as they are on screen.
	private static (Button? Save, Button? Load) RowActions(MainWindow window, MainWindowViewModel model, int slot)
	{
		ItemsControl grid = window.FindNamed<ItemsControl>("SaveStatesGrid");
		Control container = Assert.IsAssignableFrom<Control>(grid.ContainerFromIndex(model.SaveStateSlots.IndexOf(model.SaveStateSlot(slot)!)));
		return (container.FindAll<Button>().FirstOrDefault(b => b.Name == "SlotSaveButton"),
			container.FindAll<Button>().FirstOrDefault(b => b.Name == "SlotLoadButton"));
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//Acceptance: one grid, each slot with *Save here* and *Load*, Load disabled on
	//an empty slot.
	[AvaloniaFact]
	public void The_sheet_is_one_grid_of_ten_slots_plus_the_auto_save()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			LoadGameWithOwnSlots(window, model);
			OpenSaveStatesSheet(window, model);

			//Eleven rows, and no separate save/load sheets to open: the old two
			//buttons are gone with them.
			Assert.Equal(SaveStateSheet.ManualSlots + 1, model.SaveStateSlots.Count);
			Assert.True(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
			Assert.Equal(0, window.FindAll<Button>().Count(b => b.Name is "SaveStatesSaveButton" or "SaveStatesLoadButton"));

			//Every manual slot offers both actions; nothing holds a state yet, so
			//each Load is disabled and each Save here is not.
			for(int slot = 1; slot <= SaveStateSheet.ManualSlots; slot++) {
				(Button? save, Button? load) = RowActions(window, model, slot);
				Assert.True(save?.IsOnScreen() == true, $"slot {slot} has no Save here");
				Assert.True(save!.IsEffectivelyEnabled, $"slot {slot}'s Save here is disabled");
				Assert.True(load?.IsOnScreen() == true, $"slot {slot} has no Load");
				Assert.False(load!.IsEffectivelyEnabled, $"slot {slot}'s Load is armed with no state");
			}

			//The auto-save row: Load alone, and disabled while the core has not
			//written it.
			(Button? autoSave, Button? autoLoad) = RowActions(window, model, SaveStateSheet.AutoSaveSlot);
			Assert.True(autoSave?.IsEffectivelyVisible == false, "the auto-save row offers a Save here");
			Assert.True(autoLoad?.IsOnScreen() == true, "the auto-save row has no Load");
			Assert.False(autoLoad!.IsEffectivelyEnabled);
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//The slot's age and preview survive the merge, and *Save here* is what turns
	//the row's own Load on.
	[AvaloniaFact]
	public void Save_here_writes_the_slot_and_turns_that_rows_load_on()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			LoadGameWithOwnSlots(window, model);
			OpenSaveStatesSheet(window, model);

			SaveStateSlotViewModel row = model.SaveStateSlot(2)!;
			Assert.False(row.HasState);
			string empty = row.AgeText;
			Assert.True(empty.Length > 0, "the empty slot says nothing");

			(Button? save, Button? load) = RowActions(window, model, 2);
			save!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			WaitFor(() => row.HasState, "Save here did not write slot 2");

			//The state is on disk, the row says how old it is, and the Load beside
			//it is armed - the same control, re-read.
			Assert.True(File.Exists(row.FileName), $"slot 2's file was not written ({row.FileName})");
			Assert.True(row.Written.HasValue);
			Assert.True(model.SaveStateSlot(2)!.LoadEnabled);
			Assert.True(load!.IsEffectivelyEnabled, "the row's Load stayed disabled after the save");
			Assert.NotEqual(empty, row.AgeText);

			//The preview the wireframe keeps rides the same file.
			WaitFor(() => row.Thumbnail != null, "slot 2's preview never loaded");
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//The row's own *Load* puts the player back in the game.
	[AvaloniaFact]
	public void Load_returns_to_the_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			LoadGameWithOwnSlots(window, model);
			OpenSaveStatesSheet(window, model);

			(Button? save, Button? load) = RowActions(window, model, 1);
			save!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			WaitFor(() => model.SaveStateSlot(1)!.HasState, "Save here did not write slot 1");

			//The empty slot beside it stays unloadable: only the slot with a state
			//comes back to the game.
			Assert.False(model.SaveStateSlot(2)!.LoadEnabled);

			load!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			WaitFor(() => !model.IsSaveStatesSheetVisible && !model.IsPlayerOverlayVisible,
				"Load left the sheet or the overlay up");
			WaitFor(() => !EmuApi.IsPaused(), "Load did not return to the running game");
			Assert.False(window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
			Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}
}
