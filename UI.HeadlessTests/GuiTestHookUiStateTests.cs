using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Logic;
using Mesen.Interop;
using Mesen.ViewModels;
using Mesen.Views;
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

	//#1236: `screen` is the ACTIVE screen, and a surface drawn over another one is
	//the active one. Both stay effectively visible (the library sheet is drawn
	//over the home, which keeps painting under it), so a hook that answers with
	//every visible screen id and no order answers `play.home` forever - and the
	//pilot's `home.open-library` step, which waits for `ui.screen == play.library`,
	//could never be met however well the sheet opened.
	[AvaloniaFact]
	public void State_reports_the_surface_drawn_over_another_as_the_active_screen()
	{
		StackPanel root = new();
		root.Children.Add(Named(new Border(), "play.home"));
		Border library = new() { IsVisible = false };
		root.Children.Add(Named(library, "play.library"));
		Window window = new() { Content = root };
		window.Show();
		TestHookWiring.WindowTarget target = new(window, () => false);

		Assert.Equal("play.home", target.State()["screen"]!.GetValue<string>());

		library.IsVisible = true;
		Assert.Equal("play.library", target.State()["screen"]!.GetValue<string>());

		//...and the home underneath is still reported as visible: the screen is
		//which one is active, not which one is drawn.
		Assert.Contains("play.home", target.State()["visible"]!.AsArray().Select(n => n!.GetValue<string>()));
	}

	//#1236, the app's own side of the same rule: the library surface the home's
	//*Open a ROM…* opens carries the id the pilot names it by, so the screen the
	//hook answers while the sheet is up is `play.library`. The host's binding is
	//repeated here because the view is drawn by MainWindow, which needs the native
	//core; everything read below is the shipped XAML's.
	[AvaloniaFact]
	public void State_reports_the_real_library_sheet_as_the_active_screen()
	{
		PlayerRomPickerViewModel model = new() {
			RunScanInline = true,
			RunLibraryScanInline = true,
			LibraryFolderSource = () => new List<string>(),
			SuggestionSource = _ => Array.Empty<RomPickerHit>()
		};
		PlayerRomPickerView picker = new() { DataContext = model };
		picker.Bind(Visual.IsVisibleProperty, new Binding(nameof(PlayerRomPickerViewModel.IsVisible)));
		StackPanel root = new();
		root.Children.Add(Named(new Border(), "play.home"));
		root.Children.Add(picker);
		Window window = new() { Width = 1100, Height = 740, Content = root };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		TestHookWiring.WindowTarget target = new(window, () => false);

		Assert.Equal("play.home", target.State()["screen"]!.GetValue<string>());

		model.Open();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("play.library", target.State()["screen"]!.GetValue<string>());
		Assert.Contains("play.library", target.State()["visible"]!.AsArray().Select(n => n!.GetValue<string>()));
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
