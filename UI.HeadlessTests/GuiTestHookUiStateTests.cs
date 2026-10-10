using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.Interop;
using Mesen.ViewModels;
using Mesen.Windows;
using System;
using System.Threading;
using Xunit.Sdk;
using Xunit;

namespace Mesen.HeadlessTests;

//#1199 (ADR-0271 Consequences): the hook's UI state carries the four fields Jev
//navigates from - screen, focused control, visible controls and menu options -
//as named ids, never pixels. Goal navigation in the agent-squad fork reads them.
[Collection(NativeCoreCollection.Name)]
public class GuiTestHookUiStateTests
{
	private static Control Named(Control control, string id)
	{
		AutomationProperties.SetAutomationId(control, id);
		return control;
	}

	[AvaloniaFact]
	public void State_lists_the_visible_controls_and_the_menu_options_by_id()
	{
		StackPanel root = new();
		Menu menu = new();
		MenuItem settings = new() { Header = "Settings", IsEnabled = true };
		AutomationProperties.SetAutomationId(settings, "menu.settings");
		MenuItem display = new() { Header = "Display" };
		AutomationProperties.SetAutomationId(display, "menu.settings.display");
		MenuItem scale = new() { Header = "Scale" };
		AutomationProperties.SetAutomationId(scale, "menu.settings.display.scale");
		display.Items.Add(scale);
		settings.Items.Add(display);
		menu.Items.Add(settings);
		root.Children.Add(menu);
		root.Children.Add(Named(new Button(), "play.home.open-rom"));
		root.Children.Add(Named(new Button { IsVisible = false }, "play.home.hidden"));
		Window window = new() { Content = root };
		window.Show();
		TestHookWiring.WindowTarget target = new(window, () => false);

		JsonObject state = target.State();

		string?[] visible = state["visible"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
		Assert.Contains("play.home.open-rom", visible);
		Assert.DoesNotContain("play.home.hidden", visible);
		JsonArray options = state["options"]!.AsArray();
		Assert.Contains(options, o => o!["id"]!.GetValue<string>() == "menu.settings" && o["enabled"]!.GetValue<bool>());
		Assert.False(Option(options, "menu.settings.display")["visible"]!.GetValue<bool>());
		Assert.False(Option(options, "menu.settings.display.scale")["visible"]!.GetValue<bool>());
		Assert.Equal("Display", Option(options, "menu.settings.display")["text"]!.GetValue<string>());

		settings.IsSubMenuOpen = true;
		options = target.State()["options"]!.AsArray();
		Assert.True(Option(options, "menu.settings.display")["visible"]!.GetValue<bool>());
		Assert.False(Option(options, "menu.settings.display.scale")["visible"]!.GetValue<bool>());

		display.IsSubMenuOpen = true;
		options = target.State()["options"]!.AsArray();
		Assert.True(Option(options, "menu.settings.display.scale")["visible"]!.GetValue<bool>());
	}

	//#1231: the screen is the surface drawn last, not the home by name. The library
	//sheet opens over the home (ADR-0256 Decision 9's own layout, ADR-0272 item 3),
	//so while it is up the state has to name IT - the pad-only script's
	//`home.open-library` step waits on exactly that.
	[AvaloniaFact]
	public void State_names_the_surface_drawn_over_the_home()
	{
		StackPanel root = new();
		root.Children.Add(Named(new Border(), "play.home"));
		root.Children.Add(Named(new Border(), "play.home.open-rom"));
		root.Children.Add(Named(new Border(), "play.library"));
		Window window = new() { Content = root };
		window.Show();
		TestHookWiring.WindowTarget target = new(window, () => false);

		Assert.Equal("play.library", target.State()["screen"]?.GetValue<string>());

		//And back: with nothing over it, the home is the screen again.
		root.Children[2].IsVisible = false;
		Assert.Equal("play.home", target.State()["screen"]?.GetValue<string>());
	}

	private static JsonObject Option(JsonArray options, string id) =>
		options.Select(o => o!.AsObject()).First(o => o["id"]!.GetValue<string>() == id);

	//The shipped menu bar must expose ids too, or `options` is [] in the real app.
	[AvaloniaFact]
	public void State_lists_the_real_main_menu_options_with_text()
	{
		if(!NativeCore.IsAvailable) {
			return;
		}
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		DateTime deadline = DateTime.UtcNow.AddSeconds(30);
		while(model.MainMenu.HelpMenuItems.Count == 0) {
			if(DateTime.UtcNow > deadline) {
				throw new XunitException("MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(50);
		}
		Dispatcher.UIThread.RunJobs();
		TestHookWiring.WindowTarget target = new(window, () => false);

		JsonArray options = target.State()["options"]!.AsArray();
		Assert.Contains(options, o => o!["id"]!.GetValue<string>() == "menu.options");

		MenuItem optionsMenu = window.GetLogicalDescendants().OfType<MenuItem>()
			.First(m => AutomationProperties.GetAutomationId(m) == "menu.options");
		optionsMenu.IsSubMenuOpen = true;
		Dispatcher.UIThread.RunJobs();
		options = target.State()["options"]!.AsArray();
		Assert.Contains(options, o => o!["id"]!.GetValue<string>().StartsWith("menu.")
			&& o["id"]!.GetValue<string>() != "menu.options"
			&& o["visible"]!.GetValue<bool>()
			&& !string.IsNullOrEmpty(o["text"]?.GetValue<string>()));
		//Closing runs the exit path, which would release the process-global core
		//the next test still needs.
		window.ReleaseCore = () => { };
		window.SkipCloseConfirmation = true;
		window.Close();
		EmuApi.Stop();
		Dispatcher.UIThread.RunJobs();
	}
}
