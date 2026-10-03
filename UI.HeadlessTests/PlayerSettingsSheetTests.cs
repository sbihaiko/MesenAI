using System;
using System.Linq;
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

namespace Mesen.HeadlessTests;

//ADR-0249 (W-P8, W-P10): W-P4's Settings row opens Settings as a sheet inside
//the main window, over the dimmed game, like the other Play sheets - not a
//separate OS window. Done and Esc keep what was changed and close back to
//W-P4; "More in Options…" hands over to the classic Options window.
[Collection(NativeCoreCollection.Name)]
public class PlayerSettingsSheetTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly VideoFilterType _filter = ConfigManager.Config.Video.VideoFilter;
	private readonly string _shader = ConfigManager.Config.Video.ShaderFile;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Video.VideoFilter = _filter;
		ConfigManager.Config.Video.ShaderFile = _shader;
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowOverlay()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static Border OpenSettings(MainWindow window)
	{
		Click(window.FindNamed<Button>("OverlaySettingsButton"));
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		return window.FindNamed<Border>("PlayerSettingsSheet");
	}

	[AvaloniaFact]
	public void The_settings_row_opens_a_sheet_in_the_main_window_not_a_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowOverlay();

		Border sheet = OpenSettings(window);

		Assert.True(sheet.IsOnScreen(), "the Settings sheet is not on screen in the main window");
		Assert.Contains("sheet", sheet.Classes);
		Assert.Equal(480, sheet.Bounds.Width, 0.5);
		Assert.Null(model.MainMenu.OptionsWindow);
		Assert.False(model.IsPlayerOverlayVisible);
		Assert.True(window.FindNamed<Border>("PlayerOverlayScrim").IsOnScreen());
		Assert.True(window.FindNamed<TabControl>("PlayerSettingsTabs").IsOnScreen());
	}

	[AvaloniaFact]
	public void Done_closes_the_sheet_back_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowOverlay();
		Border sheet = OpenSettings(window);
		Assert.True(sheet.IsOnScreen());

		Click(window.FindNamed<Button>("btnPlayerSettingsDone"));

		Assert.False(sheet.IsOnScreen());
		Assert.True(model.IsPlayerOverlayVisible);
		Assert.Null(model.PlayerSettings);
	}

	[AvaloniaFact]
	public void Esc_keeps_the_change_and_closes_the_sheet_back_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model) = ShowOverlay();
		Border sheet = OpenSettings(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Dispatcher.UIThread.RunJobs();
		ComboBox pixels = window.FindNamed<ComboBox>("cboLookPixels");
		pixels.SelectedItem = pixels.Items.Cast<LookChoice>().First(c => c.Pixels?.Kind == PixelsItemKind.SmoothHq4x);
		Dispatcher.UIThread.RunJobs();

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Assert.False(sheet.IsOnScreen());
		Assert.True(model.IsPlayerOverlayVisible);
		Assert.Equal(VideoFilterType.HQ4x, ConfigManager.Config.Video.VideoFilter);
	}

	//"More in Options…" is Advanced territory: the sheet closes (keeping what
	//was changed) and the classic Options window opens on Video.
	[AvaloniaFact]
	public void More_in_options_hands_over_to_the_classic_options_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, MainWindowViewModel model) = ShowOverlay();
		Border sheet = OpenSettings(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Dispatcher.UIThread.RunJobs();
		ComboBox pixels = window.FindNamed<ComboBox>("cboLookPixels");
		pixels.SelectedItem = pixels.Items.Cast<LookChoice>().First(c => c.Pixels?.Kind == PixelsItemKind.MoreInOptions);
		Dispatcher.UIThread.RunJobs();

		Assert.False(sheet.IsOnScreen());
		Assert.Null(model.PlayerSettings);
		//The same window Classic › Settings opens (one at a time).
		ConfigWindow options = Assert.IsType<ConfigWindow>(model.MainMenu.OptionsWindow);
		try {
			ConfigViewModel optionsModel = Assert.IsType<ConfigViewModel>(options.DataContext);
			Assert.False(optionsModel.PlayerMode);
			Assert.Equal(ConfigWindowTab.Video, optionsModel.SelectedIndex);
			Assert.True(options.FindNamed<TabControl>("AdvancedSettingsTabs").IsOnScreen());
		} finally {
			options.Close();
		}
	}

	//W-P10's copy: the first Pixels item reads as the render.
	[AvaloniaFact]
	public void Sharp_reads_as_the_render()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";
		(MainWindow window, _) = ShowOverlay();
		OpenSettings(window);
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Dispatcher.UIThread.RunJobs();
		ComboBox pixels = window.FindNamed<ComboBox>("cboLookPixels");
		Assert.Equal("Sharp — original pixels", Assert.IsType<LookChoice>(pixels.SelectedItem).Label);
	}
}
