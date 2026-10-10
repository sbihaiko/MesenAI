using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1199 (ADR-0271 Consequences): the hook's UI state carries the four fields Jev
//navigates from - screen, focused control, visible controls and menu options -
//as named ids, never pixels. Goal navigation in the agent-squad fork reads them.
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
		settings.Items.Add(display);
		menu.Items.Add(settings);
		root.Children.Add(menu);
		root.Children.Add(Named(new Button(), "play.home.open-rom"));
		root.Children.Add(Named(new Button { IsVisible = false }, "play.home.hidden"));
		Window window = new() { Content = root };
		window.Show();

		JsonObject state = new TestHookWiring.WindowTarget(window, () => false).State();

		string?[] visible = state["visible"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
		Assert.Contains("play.home.open-rom", visible);
		Assert.DoesNotContain("play.home.hidden", visible);
		JsonArray options = state["options"]!.AsArray();
		Assert.Contains(options, o => o!["id"]!.GetValue<string>() == "menu.settings" && o["enabled"]!.GetValue<bool>());
		Assert.Contains(options, o => o!["id"]!.GetValue<string>() == "menu.settings.display");
	}
}
