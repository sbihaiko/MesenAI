using System.Linq;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Debugger.Utilities;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0169 section 4, amended 2026-09-23: the Tools menu's live recorder keeps
//Record/Stop (the recorder still publishes into <HomeFolder>/LiveRecording and
//feeds the artist kit) and no longer offers "Open Viewer" -
//scripts/record_viewer.py is a developer/diagnostic tool run by hand. This
//checks the menu MainWindow actually builds (MainWindowViewModel.Init ->
//MainMenuViewModel.Initialize, run from OnOpened), so re-adding a viewer entry
//or a separator for one fails here.
//
//Needs a MainWindow, whose constructor calls EmuApi.InitDll(), so it runs only
//where the native core is built (NativeCore).
public class LiveRecorderMenuTests
{
	private static MainMenuAction LiveRecorderMenu()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		MainWindow window = new();
		window.Show();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.MainMenu.Initialize(window);
		return Assert.Single(model.MainMenu.ToolsMenuItems.OfType<MainMenuAction>(), a => a.ActionType == ActionType.LiveRecorder);
	}

	[AvaloniaFact]
	public void Live_recorder_submenu_is_exactly_record_and_stop()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		MainMenuAction menu = LiveRecorderMenu();

		Assert.NotNull(menu.SubActions);
		//Every child, separators included: a leftover separator would be the
		//trace of a removed viewer entry.
		Assert.All(menu.SubActions!, child => Assert.IsType<MainMenuAction>(child));
		Assert.Equal(
			new[] { ActionType.Record, ActionType.Stop },
			menu.SubActions!.Cast<MainMenuAction>().Select(a => a.ActionType).ToArray());
	}

	[AvaloniaFact]
	public void No_tools_menu_entry_opens_the_live_viewer()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		MainMenuAction menu = LiveRecorderMenu();

		//The submenu caption no longer advertises a viewer either.
		Assert.DoesNotContain("viewer", menu.Name, System.StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(menu.SubActions!.Cast<MainMenuAction>(),
			a => a.Name.Contains("Viewer", System.StringComparison.OrdinalIgnoreCase));
	}
}
