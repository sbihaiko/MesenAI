using System;
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

//#1081: the Enhancements sheet's disabled Overclock row names the console it
//cannot overclock. The rule that picks the name is pinned host-free in
//UI.Tests/Play/ConsoleTypeNamesTests; this checks the panel the player reads -
//the realized text is the console's name, and never the raw id the resource
//helper falls back to when a string is missing.
[Collection(NativeCoreCollection.Name)]
public class EnhancementsOverclockReasonTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
	}

	private static MainWindow ShowPlayWith(ConsoleType console)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new();
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = console };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	[AvaloniaFact]
	public void The_reason_names_the_SMS_console_without_brackets()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		MainWindow window = ShowPlayWith(ConsoleType.Sms);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);

		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal("not available on Sega Master System", model.EnhOverclockReason);
		TextBlock reason = window.FindNamed<TextBlock>("EnhancementsOverclockReason");
		Assert.Equal(model.EnhOverclockReason, reason.Text);
		Assert.DoesNotContain("[[", reason.Text ?? "");
	}

	//The row's text follows the console, so nothing is left over from the
	//console that was loaded before it (the same panel, another game). The panel
	//outlives the game - it is one realized view toggled by IsVisible - so the
	//binding itself has to re-evaluate, not just the ViewModel property: a
	//one-time binding would keep the old console's sentence on screen while the
	//ViewModel says otherwise. Hence the assertion on the realized TextBlock.
	[AvaloniaFact]
	public void The_reason_follows_the_loaded_console()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		MainWindow window = ShowPlayWith(ConsoleType.Sms);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("not available on Sega Master System", model.EnhOverclockReason);

		model.CloseEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Ws };
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("not available on WonderSwan", model.EnhOverclockReason);

		TextBlock reason = window.FindNamed<TextBlock>("EnhancementsOverclockReason");
		Assert.Equal("not available on WonderSwan", reason.Text);
		Assert.DoesNotContain("[[", reason.Text ?? "");
	}
}
