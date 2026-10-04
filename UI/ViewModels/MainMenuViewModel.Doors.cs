using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.ViewModels
{
	//ADR-0250: every menu entry has one place per door. The host-free rule
	//(UI/Logic/WorkspaceMenu.cs) decides which entries a door shows; this
	//file only turns them into actions (Decision 6: hiding is presentation).
	//  - DoorMenuItems: the task door's Tools ⋯ (Decision 3), rebuilt when the
	//    door or the loaded game changes. Classic has none.
	//  - WorkspaceMenuItems: Classic's Workspace ▸ menu (Decision 4).
	//  - ApplyClassicPlacement: Classic's menu bar without its duplicates.
	public partial class MainMenuViewModel
	{
		[ObservableProperty] public partial List<object> DoorMenuItems { get; private set; } = new();
		[ObservableProperty] public partial List<object> WorkspaceMenuItems { get; private set; } = new();

		private MainWindow? _window;
		private readonly Dictionary<object, MenuEntry> _classicEntries = new(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<MenuEntry, object> _doorItems = new();
		private Workspace? _doorMenuDoor;
		private GameCapabilities? _doorMenuGame;

		private static bool IsMacOS => OperatingSystem.IsMacOS();

		//Marks a classic action with the entry the rule knows it by.
		private T Tag<T>(T action, MenuEntry entry) where T : notnull
		{
			_classicEntries[action] = entry;
			return action;
		}

		//Decision 4: one Pause/Resume entry whose label follows the state.
		private MainMenuAction GetPauseMenuItem()
		{
			Func<bool> showsResume = () => PauseMenuEntry.ShowsResume(ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig, AutoPaused, EmuApi.IsPaused());
			MainMenuAction pause = new MainMenuAction(EmulatorShortcut.Pause) {
				ActionType = ActionType.Custom,
				DynamicText = () => ResourceHelper.GetEnumText(showsResume() ? ActionType.Resume : ActionType.Pause),
				DynamicIcon = () => showsResume() ? "MediaPlay" : "MediaPause",
			};
			Action shortcut = pause.OnClick;
			pause.OnClick = () => {
				if(PauseMenuEntry.TogglesAutoPause(ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig)) {
					AutoPaused = !AutoPaused;
				} else {
					shortcut();
				}
			};
			return pause;
		}

		private void InitWorkspaceMenu()
		{
			WorkspaceMenuItems = new List<object>() {
				GetSwitchToItem(Workspace.Play),
				GetSwitchToItem(Workspace.Remaster),
				GetSwitchToItem(Workspace.Share),
			};
		}

		private MainMenuAction GetSwitchToItem(Workspace door)
		{
			return new MainMenuAction() {
				ActionType = ActionType.Custom,
				DynamicText = () => ResourceHelper.GetMessage("WorkspaceName" + door),
				CustomShortcutText = () => MainWindow.Shell.ShortcutHint(door),
				OnClick = () => MainWindow.SelectWorkspace(door)
			};
		}

		//Decision 4: the classic lists keep only what ClassicMenuBar lists.
		//Untagged actions are not touched by the rule and stay.
		private void ApplyClassicPlacement()
		{
			FileMenuItems = ClassicItems(FileMenuItems);
			GameMenuItems = ClassicItems(GameMenuItems);
			OptionsMenuItems = ClassicItems(OptionsMenuItems);
			ToolsMenuItems = ClassicItems(ToolsMenuItems);
			DebugMenuItems = ClassicItems(DebugMenuItems);
			HelpMenuItems = ClassicItems(HelpMenuItems);
		}

		private List<object> ClassicItems(List<object> items)
		{
			List<object> kept = items.Where(i => !_classicEntries.TryGetValue(i, out MenuEntry entry) || WorkspaceMenu.ShowsInClassic(entry, IsMacOS)).ToList();
			return TrimSeparators(kept);
		}

		//No separator first, last or next to another one.
		private static List<object> TrimSeparators(List<object> items)
		{
			List<object> result = new();
			foreach(object item in items) {
				if(item is ContextMenuSeparator && (result.Count == 0 || result[^1] is ContextMenuSeparator)) {
					continue;
				}
				result.Add(item);
			}
			while(result.Count > 0 && result[^1] is ContextMenuSeparator) {
				result.RemoveAt(result.Count - 1);
			}
			return result;
		}

		//What the loaded game uses, for Play's console items.
		public GameCapabilities CurrentGameCapabilities()
		{
			if(!IsGameRunning) {
				return GameCapabilities.None;
			}
			return new GameCapabilities(
				FdsDisk: IsFdsGame,
				VsSystem: IsVsSystemGame,
				VsDualSystem: IsVsDualSystemGame,
				Barcode: EmuApi.IsShortcutAllowed(EmulatorShortcut.InputBarcode),
				Tape: EmuApi.IsShortcutAllowed(EmulatorShortcut.RecordTape) || EmuApi.IsShortcutAllowed(EmulatorShortcut.StopRecordTape)
			);
		}

		//Rebuilds the door's Tools ⋯ when the door or the game's capabilities
		//changed since the last build; returns true when it did.
		public bool RefreshDoorMenu()
		{
			if(_window == null) {
				return false;
			}
			Workspace door = MainWindow.Shell.Active;
			GameCapabilities game = door == Workspace.Play ? CurrentGameCapabilities() : GameCapabilities.None;
			if(_doorMenuDoor == door && _doorMenuGame == game) {
				return false;
			}
			_doorMenuDoor = door;
			_doorMenuGame = game;

			List<object> items = new();
			foreach(IReadOnlyList<MenuEntry> group in WorkspaceMenu.ToolsMenu(door, IsMacOS, game)) {
				if(items.Count > 0) {
					items.Add(new ContextMenuSeparator());
				}
				items.AddRange(group.Select(GetDoorItem));
			}
			DoorMenuItems = items;
			return true;
		}

		private object GetDoorItem(MenuEntry entry)
		{
			if(!_doorItems.TryGetValue(entry, out object? item)) {
				item = CreateDoorItem(entry, _window!);
				_doorItems[entry] = item;
			}
			return item;
		}

		private static Func<string> Label(string key) => () => ResourceHelper.GetMessage(key);

		private object CreateDoorItem(MenuEntry entry, MainWindow wnd)
		{
			return entry switch {
				MenuEntry.Reset => new MainMenuAction(EmulatorShortcut.Reset) { ActionType = ActionType.Reset },
				MenuEntry.PowerCycle => new MainMenuAction(EmulatorShortcut.PowerCycle) { ActionType = ActionType.PowerCycle, DynamicText = Label("DoorMenuPowerCycle") },
				MenuEntry.FdsSelectDisk => new MainMenuAction() {
					ActionType = ActionType.SelectDisk,
					SubActions = Enumerable.Range(0, 8).Select(GetFdsInsertDiskItem).ToList<object>()
				},
				MenuEntry.FdsEjectDisk => new MainMenuAction(EmulatorShortcut.FdsEjectDisk) { ActionType = ActionType.EjectDisk },
				MenuEntry.InsertCoin1 => new MainMenuAction(EmulatorShortcut.VsInsertCoin1) { ActionType = ActionType.InsertCoin1 },
				MenuEntry.InsertCoin2 => new MainMenuAction(EmulatorShortcut.VsInsertCoin2) { ActionType = ActionType.InsertCoin2 },
				MenuEntry.InsertCoin3 => new MainMenuAction(EmulatorShortcut.VsInsertCoin3) { ActionType = ActionType.InsertCoin3 },
				MenuEntry.InsertCoin4 => new MainMenuAction(EmulatorShortcut.VsInsertCoin4) { ActionType = ActionType.InsertCoin4 },
				MenuEntry.InputBarcode => new MainMenuAction(EmulatorShortcut.InputBarcode) { ActionType = ActionType.InputBarcode },
				MenuEntry.TapeRecorder => GetTapeRecorderMenu(),
				MenuEntry.Screenshot => new MainMenuAction(EmulatorShortcut.TakeScreenshot) {
					ActionType = ActionType.TakeScreenshot,
					DynamicText = Label("DoorMenuScreenshot")
				},
				MenuEntry.Fullscreen => new MainMenuAction(EmulatorShortcut.ToggleFullscreen) { ActionType = ActionType.Fullscreen },
				MenuEntry.ReloadPackImages => WithLabel(GetReloadPackImagesItem(), "DoorMenuReloadPackImages"),
				MenuEntry.MusicRecorder => WithLabel(GetMusicRecorderMenu(wnd), "DoorMenuRecordMusic"),
				MenuEntry.EnhancementPacks => WithLabel(GetEnhancementPacksItem(wnd), "DoorMenuEnhancementPacks"),
				MenuEntry.LogWindow => WithLabel(GetLogWindowItem(wnd), "DoorMenuLogWindow"),
				MenuEntry.MoviePlay => WithLabel(GetMoviePlayItem(wnd), "DoorMenuPlayReplay"),
				MenuEntry.Record => new MainMenuAction() {
					ActionType = ActionType.Record,
					DynamicText = Label("DoorMenuRecord"),
					SubActions = new List<object> { GetVideoRecorderMenu(wnd), GetSoundRecorderMenu(wnd) }
				},
				MenuEntry.NetPlay => WithLabel(GetNetPlayMenu(wnd), "DoorMenuNetplay"),
				MenuEntry.Settings => new MainMenuAction() {
					ActionType = ActionType.Preferences,
					DynamicText = Label("DoorMenuSettings"),
					OnClick = () => OpenSettings(wnd)
				},
				MenuEntry.Help => new MainMenuAction() {
					ActionType = ActionType.Custom,
					DynamicText = Label("DoorMenuHelp"),
					DynamicIcon = () => "Help",
					SubActions = new List<object> {
						new MainMenuAction() {
							ActionType = ActionType.CheckForUpdates,
							DynamicText = Label("DoorMenuCheckForUpdates"),
							OnClick = () => CheckForUpdate(wnd, false)
						},
						new MainMenuAction() {
							ActionType = ActionType.CommandLineHelp,
							DynamicText = Label("DoorMenuCommandLine"),
							OnClick = () => OpenCommandLineHelp(wnd)
						}
					}
				},
				MenuEntry.About => new MainMenuAction() {
					ActionType = ActionType.About,
					DynamicText = Label("DoorMenuAbout"),
					OnClick = () => OpenAbout(wnd)
				},
				MenuEntry.Exit => new MainMenuAction(EmulatorShortcut.Exit) {
					ActionType = ActionType.Exit,
					DynamicText = Label("DoorMenuQuit")
				},
				_ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "not a Tools ⋯ entry")
			};
		}

		private static MainMenuAction WithLabel(MainMenuAction action, string key)
		{
			action.DynamicText = Label(key);
			return action;
		}

		//ADR-0250 Decision 3: what a task door's entries open in Player mode -
		//the tool sheet in the main window (PlayerToolSheetView) for the small
		//dialogs, the Player look (PlayerWindowLook) for the big tool windows.
		//Classic (Advanced mode) keeps every classic window.
		public void OpenAbout(Window wnd)
		{
			if(MainWindow.IsPlayerMode) {
				MainWindow.ToolSheet.OpenAbout();
			} else {
				new AboutWindow().ShowCenteredDialog((Control)wnd);
			}
		}

		public void OpenCommandLineHelp(Window wnd)
		{
			if(MainWindow.IsPlayerMode) {
				MainWindow.ToolSheet.OpenCommandLine();
			} else {
				new CommandLineHelpWindow().ShowCenteredDialog((Control)wnd);
			}
		}

		public void OpenVideoRecord(Window wnd)
		{
			if(MainWindow.IsPlayerMode) {
				MainWindow.ToolSheet.OpenVideoRecord();
			} else {
				new VideoRecordWindow() {
					DataContext = new VideoRecordConfigViewModel()
				}.ShowCenteredDialog((Control)wnd);
			}
		}

		public static EnhancementPacksWindow OpenEnhancementPacks(Control opener)
		{
			return PlayerWindowLook.Apply(ApplicationHelper.GetOrCreateUniqueWindow(opener, () => PlayerWindowLook.Apply(new EnhancementPacksWindow(), opener)), opener);
		}

		public static LogWindow OpenLogWindow(Control opener)
		{
			return PlayerWindowLook.Apply(ApplicationHelper.GetOrCreateUniqueWindow(opener, () => PlayerWindowLook.Apply(new LogWindow(), opener)), opener);
		}

		//The shared tail's Settings…: a task door's is the W-P8 sheet; Classic's
		//is the Options window on Preferences, as its menu bar had it.
		public void OpenSettings(MainWindow wnd)
		{
			if(MainWindow.Shell.Active == Workspace.Classic) {
				OpenConfig(wnd, ConfigWindowTab.Preferences);
			} else {
				wnd.OpenPlayerSettingsSheet();
			}
		}
	}
}
