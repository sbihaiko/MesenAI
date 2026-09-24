using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0169 section 4, amended 2026-09-23: the Tools menu's live recorder keeps
//Record/Stop (the recorder still publishes into <HomeFolder>/LiveRecording and
//feeds the artist kit) and no longer offers "Open Viewer" -
//scripts/record_viewer.py is a developer/diagnostic tool run by hand.
//
//The policy itself (exactly Record then Stop) is asserted host-free in
//UI.Tests/Recording/LiveRecorderMenuTests.cs against LiveRecorderMenu.Entries.
//This checks the wiring: the MenuItem tree MainMenuView.axaml actually
//realizes when a user opens Tools > Live Recorder, found by its visible
//labels, so a menu that stopped binding SubActions (or a viewer entry added
//straight to the realized menu) fails here.
//
//Needs a MainWindow, whose constructor calls EmuApi.InitDll(), so it runs only
//where the native core is built (NativeCore) and self-skips on the core-less
//CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class LiveRecorderMenuTests
{
	private static MenuItem OpenSubmenu(ItemsControl parent, string header)
	{
		MenuItem? item = parent.GetRealizedContainers().OfType<MenuItem>()
			.FirstOrDefault(m => Label(m) == header);
		if(item == null) {
			throw new XunitException($"No realized MenuItem '{header}' under {parent.GetType().Name}; saw: " +
				string.Join(", ", parent.GetRealizedContainers().OfType<MenuItem>().Select(Label)));
		}
		item.Open();
		Dispatcher.UIThread.RunJobs();
		return item;
	}

	//The header as a user reads it: access-key underscores dropped.
	private static string Label(MenuItem item) => (item.Header as string ?? "").Replace("_", "");

	//MainWindow.OnOpened builds the menus itself, from a background Task
	//(InitializeEmu, then MainWindowViewModel.Init -> MainMenuViewModel.Initialize).
	//Calling Initialize again from the test races that Task (two threads
	//registering the same shortcut handlers), so wait for the window's own
	//pass instead: HelpMenuItems is the last list Initialize assigns.
	private static void WaitForMenus(MainMenuViewModel menu)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(30);
		while(menu.HelpMenuItems.Count == 0) {
			if(DateTime.UtcNow > deadline) {
				throw new XunitException("MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(50);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static MenuItem RealizedLiveRecorderMenu()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		MainWindow window = new();
		window.Show();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitForMenus(model.MainMenu);

		Menu menu = window.FindNamed<Menu>("ActionMenu");
		MenuItem tools = OpenSubmenu(menu, "Tools");
		return OpenSubmenu(tools, "Live Recorder");
	}

	[AvaloniaFact]
	public void Realized_live_recorder_submenu_is_exactly_record_and_stop()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		MenuItem liveRecorder = RealizedLiveRecorderMenu();

		//Every realized child, separators included: a leftover separator would
		//be the trace of a removed viewer entry.
		Control[] children = liveRecorder.GetRealizedContainers().ToArray();
		Assert.All(children, c => Assert.IsType<MenuItem>(c));
		Assert.Equal(new[] { "Record...", "Stop" }, children.Cast<MenuItem>().Select(Label).ToArray());
	}

	[AvaloniaFact]
	public void No_realized_tools_menu_entry_mentions_a_viewer_for_the_live_recorder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		MenuItem liveRecorder = RealizedLiveRecorderMenu();

		//The submenu caption no longer advertises a viewer either.
		Assert.DoesNotContain("viewer", Label(liveRecorder), StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(liveRecorder.GetRealizedContainers().OfType<MenuItem>(),
			m => Label(m).Contains("viewer", StringComparison.OrdinalIgnoreCase));
	}
}
