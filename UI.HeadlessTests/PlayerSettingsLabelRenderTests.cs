using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1006: the Player Settings labels are the ones PRD Part B §13.5 draws in
//W-P7, W-P8 and W-P10 - the strip reads Display | Look | Audio | Controls
//(plus ADR-0256 Decision 8's System, drawn after the mockups) and W-P7 points
//at Settings › Look. The #926 real-display pass found Window | Video and
//Settings › Video instead.
[Collection(NativeCoreCollection.Name)]
public class PlayerSettingsLabelRenderTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPaused()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow main = new() { Width = 1100, Height = 740 };
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(main.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		return (main, model);
	}

	[AvaloniaFact]
	public void The_settings_strip_reads_display_look_audio_controls()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPaused();
		try {
			window.FindNamed<Button>("OverlaySettingsButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();
			TabControl strip = window.FindNamed<TabControl>("PlayerSettingsTabs");
			string[] headers = strip.Items.OfType<TabItem>().Select(t => t.Header?.ToString() ?? "").ToArray();
			Assert.Equal(new[] { "Display", "Look", "Audio", "Controls", "System" }, headers);
		} finally {
			model.ClosePlayerSettings();
		}
	}

	[AvaloniaFact]
	public void The_enhancements_hint_points_at_settings_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPaused();
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Border sheet = window.FindNamed<Border>("PlayerEnhancementsPanel");
		TextBlock hint = sheet.FindAll<TextBlock>().Single(t => t.Text?.StartsWith("How the picture looks") == true);
		Assert.Equal("How the picture looks: Settings › Look", hint.Text);
	}
}
