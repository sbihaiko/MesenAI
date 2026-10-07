using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1013: Settings > System is a fixed 480 px sheet (PlayerSettingsEssentials.
//SheetHeight), and its two folder lines wrap. A portable folder under a long
//app path (the headless host's bin folder is one) wraps to three lines, and the
//group then outgrew the page: the keyboard caption's last line was cut by the
//group's bottom edge. The wireframe (W-P12) draws the path on one line, so the
//path is ellipsized and the whole caption stays inside the group.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlaySystemSettingsLayoutTests : IDisposable
{
	private const string LongPath = "/Users/someone/Projects/MesenCE/UI.HeadlessTests/bin/Release/net10.0/osx-arm64/portable-storage-folder/with/a/very/long/path/that/wraps";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private MainWindow? _window;

	public void Dispose()
	{
		if(_window != null) {
			_window.ReleaseCore = () => { };
			_window.Close();
		}
		Dispatcher.UIThread.RunJobs();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
	}

	private static void Settle()
	{
		for(int i = 0; i < 6; i++) {
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(15);
		}
	}

	[AvaloniaFact]
	public void The_keyboard_caption_stays_inside_the_group_when_the_folder_path_is_long()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		_window = new MainWindow() { Width = 1100, Height = 740 };
		_window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(_window.DataContext);
		Stopwatch();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		Settle();
		PlayerSystemSettingsViewModel system = new(() => LongPath, () => LongPath, () => LongPath);
		model.OpenPlayerSettings(new ConfigViewModel(ConfigWindowTab.System, playerMode: true, createSystem: () => system));
		Settle();

		//Written for a person to look at (MESEN_PLAYER_RENDERS picks the folder).
		PlayerRender.Save(PlayerRender.Capture(_window), "settings-system-long-path");

		Border group = _window.FindNamed<Border>("SystemSettingsGroup");
		TextBlock caption = group.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Xbox and PlayStation") == true);
		Border sheet = _window.FindNamed<Border>("PlayerSettingsSheet");

		Point captionTop = caption.TranslatePoint(new Point(0, 0), _window)!.Value;
		double captionBottom = captionTop.Y + caption.Bounds.Height;
		double groupBottom = group.TranslatePoint(new Point(0, group.Bounds.Height), _window)!.Value.Y;
		double sheetBottom = sheet.TranslatePoint(new Point(0, sheet.Bounds.Height), _window)!.Value.Y;

		Assert.True(captionBottom <= groupBottom - 8,
			$"the keyboard caption ends at {captionBottom} but the group ends at {groupBottom}; it needs 8 px of padding inside the group");
		Assert.True(groupBottom <= sheetBottom,
			$"the group ends at {groupBottom}, below the sheet's bottom edge {sheetBottom}");
	}

	private static void Stopwatch() => Settle();
}
