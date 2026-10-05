using System;

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
	private readonly bool _confirmExit = ConfigManager.Config.Preferences.ConfirmExitResetPower;

	[AvaloniaFact]
	public void The_window_a_test_showed_is_closed_when_the_test_ends()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		//The two values are process-global, so they go back whatever happens: a
		//failing assertion here would otherwise leave every later case running in
		//Player mode (#843 review).
		try {
			prefs.UiMode = UiMode.Player;
			prefs.Workspace = Workspace.Play;

			MainWindow window = new();
			//ShowStarted is also what registers the window with the hook, so this is the
			//path every other test takes.
			window.ShowStarted();
			Assert.True(window.IsVisible, "the window the test showed was never visible, so nothing about closing it can be told");

			MainWindowStartup.SettleShownWindows();

			Assert.False(window.IsVisible, "the hook left the test's window open (#840)");
		} finally {
			prefs.UiMode = _uiMode;
			prefs.Workspace = _workspace;
		}
	}

	//The half of #840 that a plain Close() does not cover, and the reason the hook
	//sets SkipCloseConfirmation: MainWindow.OnClosing cancels the close when the
	//quit confirmation is on (ConfirmExitResetPower, then Player mode's stop
	//banner), so a window shown under that setting survives the hook with its
	//50 ms pad timer still armed - the leak, one test later. The product's own
	//case for the *player* path is PlayerNoClassicDialogTests'.
	[AvaloniaFact]
	public void The_hook_closes_a_window_that_would_ask_before_closing()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		try {
			prefs.UiMode = UiMode.Player;
			prefs.Workspace = Workspace.Play;
			prefs.ConfirmExitResetPower = true;

			MainWindow window = new();
			window.ShowStarted();
			Assert.True(window.IsVisible, "the window the test showed was never visible, so nothing about closing it can be told");

			MainWindowStartup.SettleShownWindows();

			Assert.False(window.IsVisible, "the hook left a window that would have asked before closing (#840)");
		} finally {
			prefs.UiMode = _uiMode;
			prefs.Workspace = _workspace;
			prefs.ConfirmExitResetPower = _confirmExit;
		}
	}
}
