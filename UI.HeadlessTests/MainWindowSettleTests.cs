using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.HeadlessTests;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#840: the window a test showed goes with the test. The assembly's
//SettleMainWindows hook (#619) used to wait for the work those windows started and
//then leave them open, so an open window kept its own 50 ms DispatcherTimer
//(PlayPadNavigationWiring.Attach, one per MainWindow) and whatever else it wired;
//a tick landing after the test ended made the NEXT test's session setup throw in
//HeadlessUnitTestSession.EnsureIsolatedApplication - "The calling thread cannot
//access this object because a different thread owns it", reported at 1 ms against
//a case that has nothing to do with it, which is the shape #840 reports and the
//same family as #838 (a leaked window, in the focus arbiter's case).
//
//The hook runs as a BeforeAfterTest attribute, so its work cannot be observed from
//inside a test that relies on it; this calls it directly, which is what the
//attribute does after every test in this assembly.
[Collection(NativeCoreCollection.Name)]
public class MainWindowSettleTests
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	[AvaloniaFact]
	public void The_window_a_test_showed_is_closed_when_the_test_ends()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;

		MainWindow window = new();
		//ShowStarted is also what registers the window with the hook, so this is the
		//path every other test takes.
		window.ShowStarted();
		Assert.True(window.IsVisible, "the window the test showed was never visible, so nothing about closing it can be told");

		MainWindowStartup.SettleShownWindows();

		Assert.False(window.IsVisible, "the hook left the test's window open (#840)");

		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
	}
}
