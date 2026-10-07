using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1062 review (finding 2): the library sheet is the wireframe's 620 px tall
//when the window has room, and shrinks when it does not. MainWindow allows a
//window far shorter than 668 px (620 plus the sheet's 24 px margins), so a sheet
//that insists on 620 pushes the bottom-docked Back and the header's search,
//Clear and Browse controls off the window, where a mouse cannot reach them.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryShortWindowTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1062-" + Guid.NewGuid().ToString("N"));

	public PlayerLibraryShortWindowTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
		}
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
		}
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	//Visible is not reachable: IsEffectivelyVisible stays true for a control the
	//layout pushed past the window's edge. The control's box, in window
	//coordinates, has to sit inside the window's client area.
	private static void AssertWithinWindow(MainWindow window, string name)
	{
		Control control = window.FindNamed<Control>(name);
		Assert.True(control.IsEffectivelyVisible, name + " is not on the sheet");
		Point? origin = control.TranslatePoint(new Point(0, 0), window);
		Assert.NotNull(origin);
		Assert.True(origin!.Value.Y >= 0, $"{name} starts above the window ({origin.Value.Y})");
		Assert.True(origin.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 0.5,
			$"{name} ends at {origin.Value.Y + control.Bounds.Height}, below the {window.ClientSize.Height} px window");
	}

	[AvaloniaFact]
	public void A_short_window_keeps_Back_and_the_header_controls_on_screen()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string nes = Path.Combine(_folder, "games", "NES");
		Directory.CreateDirectory(nes);
		File.WriteAllBytes(Path.Combine(nes, "Metroid (USA).nes"), SyntheticNrom.Build());
		ConfigManager.Config.Preferences.GameFolder = Path.Combine(_folder, "games");
		ConfigManager.Config.Preferences.OverrideGameFolder = true;

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 400 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomPicker.RunLibraryScanInline = true;
		model.RomPicker.Open();
		Pump();
		Assert.Equal(RomPickerMode.Library, model.RomPicker.Mode);

		//Clear only shows while a query is on.
		model.RomPicker.SearchQuery = "met";
		Pump();

		AssertWithinWindow(window, "RomPickerBack");
		AssertWithinWindow(window, "RomPickerSearch");
		AssertWithinWindow(window, "RomPickerSearchClear");
		AssertWithinWindow(window, "RomPickerBrowseFile");
	}
}
