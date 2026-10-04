using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0250 Decision 3's last clause: "No task-door entry opens a classic
//window: each one opens its Player sheet, or is not offered in that door."
//The entries are clicked through the door's own Tools ⋯ actions
//(MainMenuViewModel.DoorMenuItems) and checked against UI/Logic/DoorEntryOpens:
//the small dialogs are the tool sheet in the main window (no window opens),
//the big tool windows open in the Player look; Classic keeps every classic
//window and its look (the scope test). Each test saves a PNG of what it asserts.
[Collection(NativeCoreCollection.Name)]
public partial class DoorMenuNoClassicWindowTests : IDisposable
{
	private static readonly Color Card = Colors.White;
	private static readonly Color WindowBackground = Color.Parse("#F5F5F7");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly IApplicationLifetime? _lifetime = Application.Current?.ApplicationLifetime;
	private ClassicDesktopStyleApplicationLifetime? _installed;

	//Every window opened during the test (Avalonia.Headless lists no window
	//that is not owned, so the unique tool windows are caught here).
	private static readonly List<Window> Opened = new();
	private static bool _recording;

	private static void RecordOpenedWindows()
	{
		if(!_recording) {
			_recording = true;
			Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Opened.Add(w));
		}
		Opened.Clear();
	}

	public void Dispose()
	{
		//Only the tool windows: closing a MainWindow shuts the emulator down.
		foreach(Window w in Opened.Where(w => w is not MainWindow && w.IsVisible).ToArray()) {
			w.Close();
		}
		Opened.Clear();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		InstallLifetime(_lifetime);
	}

	private (MainWindow Window, MainWindowViewModel Model) Show(UiMode mode, Workspace door)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		RecordOpenedWindows();
		MainWindow window = new() { Width = 1100, Height = 740 };
		//The unique tool windows are found through the application lifetime,
		//which Avalonia.Headless does not install.
		_installed = new ClassicDesktopStyleApplicationLifetime() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
		InstallLifetime(_installed);
		_installed.MainWindow = window;
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		if(door != Workspace.Play) {
			model.SelectWorkspace(door);
		}
		model.MainMenu.RefreshDoorMenu();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void InstallLifetime(IApplicationLifetime? lifetime)
	{
		FieldInfo field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
			.First(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
		field.SetValue(Application.Current, lifetime);
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

	//A leaf of the door's Tools ⋯, by its labels (or ActionType names) from
	//the top ("Help", "Command Line").
	private static MainMenuAction DoorItem(MainWindowViewModel model, params string[] path)
	{
		IEnumerable<object> items = model.MainMenu.DoorMenuItems;
		MainMenuAction? found = null;
		foreach(string label in path) {
			found = items.OfType<MainMenuAction>().FirstOrDefault(a => a.Name == label || a.ActionType.ToString() == label)
				?? throw new XunitException($"no '{label}' in {string.Join(" › ", path)}; have: {string.Join(", ", items.OfType<MainMenuAction>().Select(a => a.Name))}");
			items = found.SubActions ?? new List<object>();
		}
		return found!;
	}

	private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

	private static TextBlock LabelOf(Control control) => control.FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));

	private static void AssertPlayerButton(Button button)
	{
		Assert.Equal(new CornerRadius(8), button.CornerRadius);
		Assert.Equal("Inter", LabelOf(button).FontFamily.Name);
	}

	//The tool sheet is up, in the Player look, and nothing opened a window.
	private static Border AssertToolSheet(MainWindow window, MainWindowViewModel model, PlayerToolSheet kind, string title)
	{
		AssertNoOtherWindow(window);
		Assert.Equal(kind, model.ToolSheet.Kind);
		Border sheet = window.FindNamed<Border>("ToolSheet");
		Assert.True(sheet.IsOnScreen(), $"the {kind} sheet is not on screen");
		Assert.Contains("sheet", sheet.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		TextBlock heading = window.FindNamed<TextBlock>("ToolSheetTitle");
		Assert.Equal(title, heading.Text);
		Assert.Equal("Inter", heading.FontFamily.Name);
		return sheet;
	}

	private static void AssertNoOtherWindow(MainWindow window)
	{
		Assert.Empty(Opened.Where(w => w != window && w.IsVisible).Select(w => w.GetType().Name));
	}

	private static void SaveRender(TopLevel top, string name) => PlayerRender.Save(PlayerRender.Capture(top), name);

	//Help › Check for Updates and Help › Command Line, from Remaster's Tools ⋯:
	//sheets, and Esc (through the shortcut handler, as the Core delivers it)
	//closes them in that door.
	[AvaloniaFact]
	public void Help_entries_open_the_tool_sheet_not_a_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Remaster);
		Assert.All(DoorEntryOpensTable.Of(MenuEntry.Help), o => Assert.Equal(EntryOpens.PlayerSheet, o));

		DoorItem(model, "Help", "Check for Updates").OnClick();
		Dispatcher.UIThread.RunJobs();
		AssertToolSheet(window, model, PlayerToolSheet.CheckForUpdates, "Check for Updates");
		Border banner = window.FindNamed<Border>("ToolSheetUpdates");
		Assert.Contains("banner", banner.Classes);
		Assert.Contains(UpdateChannel.ReleasesPageUrl, window.FindNamed<TextBlock>("ToolSheetUpdatesText").Text);
		Assert.True(window.FindNamed<Button>("ToolSheetOpenReleases").IsOnScreen());
		AssertPlayerButton(window.FindNamed<Button>("ToolSheetOpenReleases"));
		AssertPlayerButton(window.FindNamed<Button>("ToolSheetCancel"));
		SaveRender(window, "ADR-0250-check-for-updates-sheet");
		Click(window.FindNamed<Button>("ToolSheetCancel"));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible);

		DoorItem(model, "Help", "Command Line").OnClick();
		Dispatcher.UIThread.RunJobs();
		AssertToolSheet(window, model, PlayerToolSheet.CommandLine, "Command-line options");
		Assert.NotEmpty(model.ToolSheet.CommandLineTabs);
		Assert.Equal(model.ToolSheet.CommandLineTabs[0].Content, window.FindNamed<SelectableTextBlock>("ToolSheetCommandLineText").Text);
		Assert.Contains("segmented", window.FindNamed<ListBox>("ToolSheetCommandLineTabs").Classes);
		Assert.True(window.FindNamed<Button>("ToolSheetDone").IsOnScreen());
		Assert.False(window.FindNamed<Button>("ToolSheetCancel").IsOnScreen());
		SaveRender(window, "ADR-0250-command-line-sheet");

		new ShortcutHandler(window).ExecuteShortcut(EmulatorShortcut.ToggleOverlay);
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible, "Esc did not close the tool sheet in Remaster");
		Assert.Equal(Workspace.Remaster, model.Shell.Active);
		AssertNoOtherWindow(window);
	}

	//About MesenAI (the tail's About, in the macOS app menu or the end of
	//Tools ⋯): the app, its build and the credits on the sheet; Done closes it.
	[AvaloniaFact]
	public void About_opens_the_tool_sheet_not_a_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Share);
		Assert.Equal(new[] { EntryOpens.PlayerSheet }, DoorEntryOpensTable.Of(MenuEntry.About));

		if(OperatingSystem.IsMacOS()) {
			model.MainMenu.OpenAbout(window);
		} else {
			DoorItem(model, "About MesenAI").OnClick();
		}
		Dispatcher.UIThread.RunJobs();
		AssertToolSheet(window, model, PlayerToolSheet.About, "About MesenAI");
		Assert.StartsWith("Version ", window.FindNamed<TextBlock>("ToolSheetAboutVersion").Text);
		Assert.Contains(model.ToolSheet.Libraries, l => l.Name == "Avalonia");
		Assert.Contains("inset", window.FindNamed<Border>("ToolSheetAboutCredits").Classes);
		AssertPlayerButton(window.FindNamed<Button>("ToolSheetDone"));
		SaveRender(window, "ADR-0250-about-sheet");

		Click(window.FindNamed<Button>("ToolSheetDone"));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible);
		AssertNoOtherWindow(window);
	}

	//Share's Record › Video › Record: the video recorder's settings on the
	//sheet (codec, the HUD switches), the game's picture stepping aside; Cancel
	//records nothing.
	[AvaloniaFact]
	public void Record_video_opens_its_settings_on_the_tool_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Share);
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Dispatcher.UIThread.RunJobs();

		DoorItem(model, "Record", "VideoRecorder", "Record").OnClick();
		Dispatcher.UIThread.RunJobs();
		AssertToolSheet(window, model, PlayerToolSheet.VideoRecord, "Record Video");
		Assert.NotNull(model.ToolSheet.VideoRecord);
		Assert.Contains("popup", window.FindNamed<Control>("ToolSheetVideoCodec").Classes);
		Assert.Contains("switch", window.FindNamed<CheckBox>("ToolSheetVideoSystemHud").Classes);
		Assert.Equal(model.ToolSheet.VideoRecord!.SavePath, window.FindNamed<TextBlock>("ToolSheetVideoPath").Text);
		AssertPlayerButton(window.FindNamed<Button>("ToolSheetRecord"));
		//Share's green, not the BIOS layer's Play blue.
		Assert.Equal(Color.Parse("#34C759"), PlayerRender.SolidColor(window.FindNamed<Button>("ToolSheetRecord").Background));
		Assert.False(model.IsNativeRendererVisible, "the native picture would cover the sheet");
		SaveRender(window, "ADR-0250-record-video-sheet");

		Click(window.FindNamed<Button>("ToolSheetCancel"));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible);
		Assert.Null(model.ToolSheet.VideoRecord);
		AssertNoOtherWindow(window);
	}

	//Insert Barcode (Play's console item, and its shortcut in any door): the
	//sheet's field keeps digits only; Esc closes it.
	[AvaloniaFact]
	public void A_barcode_is_asked_on_the_tool_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Play);

		new ShortcutHandler(window).InputBarcode();
		Dispatcher.UIThread.RunJobs();
		AssertToolSheet(window, model, PlayerToolSheet.Barcode, "Insert Barcode");
		model.ToolSheet.Barcode = "49ab12";
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("4912", window.FindNamed<TextBox>("ToolSheetBarcode").Text);
		Assert.True(window.FindNamed<Button>("ToolSheetSubmitBarcode").IsEnabled);
		AssertPlayerButton(window.FindNamed<Button>("ToolSheetSubmitBarcode"));
		SaveRender(window, "ADR-0250-barcode-sheet");

		new ShortcutHandler(window).ExecuteShortcut(EmulatorShortcut.ToggleOverlay);
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible);
		AssertNoOtherWindow(window);
	}

	//Leaving Player mode (switching to Classic) closes the sheet, as every
	//other Player surface.
	[AvaloniaFact]
	public void Leaving_player_mode_closes_the_tool_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Remaster);
		model.MainMenu.OpenCommandLineHelp(window);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.ToolSheet.IsVisible);

		model.SelectWorkspace(Workspace.Classic);
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.ToolSheet.IsVisible);
	}
}
