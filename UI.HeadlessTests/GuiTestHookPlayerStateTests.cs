using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Logic;
using Mesen.Logic.TestHook;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1282 (ADR-0272 item 3, ADR-0271): what a player judges by eye, as the hook's own state -
//the focus ring, the action bar, the open surface, whether the game is paused, the port lamps,
//the focused list's entries and the haptic ticks the menu asked for. The host-free halves live
//in UI.Tests/TestHook; this is the wiring: a real visual tree, a real shell view model and the
//real HapticTickOutput seam, with no pixel read anywhere.
[Collection(NativeCoreCollection.Name)]
public class GuiTestHookPlayerStateTests
{
	private static Control Named(Control control, string id)
	{
		AutomationProperties.SetAutomationId(control, id);
		return control;
	}

	//The ring is drawn on :focus-visible, which is the focus PlayFocusOnOpen.Enter takes
	//(NavigationMethod.Directional). The same control focused any other way is focused with no
	//ring, and the hook must say so rather than report the control it happens to be on.
	[AvaloniaFact]
	public void The_ring_is_the_directionally_focused_control_and_none_without_one()
	{
		Button continueButton = new() { Content = "Continue" };
		Button settingsButton = new() { Content = "Settings" };
		Window window = new() {
			Content = new StackPanel { Children = { Named(continueButton, "play.home.continue"), Named(settingsButton, "play.settings.open") } }
		};
		window.Show();
		TestHookWiring.WindowTarget target = new(window, () => false);

		//No NavigationMethod: what a click sends and what a host's Focus() sends, so the
		//control answers :focus and not :focus-visible - focused, with no ring (#1089).
		Assert.True(continueButton.Focus());

		JsonObject ring = target.State()["ring"]!.AsObject();
		Assert.False(ring["visible"]!.GetValue<bool>());
		Assert.Equal("play.home.continue", ring["target"]!.GetValue<string>());
		Assert.Equal(TestHookRing.None,
			TestHookRing.Observed(ring["visible"]!.GetValue<bool>(), ring["target"]!.GetValue<string>()));

		//The ring PlayFocusOnOpen.Enter takes, on the control that took it.
		Assert.True(settingsButton.Focus(NavigationMethod.Directional));

		ring = target.State()["ring"]!.AsObject();
		Assert.True(ring["visible"]!.GetValue<bool>());
		Assert.Equal("play.settings.open",
			TestHookRing.Observed(ring["visible"]!.GetValue<bool>(), ring["target"]!.GetValue<string>()));
	}

	//The lamps are the shell's own strip and the status line's own visibility: while the game
	//runs unpaused the bar is hidden (ADR-0261), and a lamp that is not drawn is never "dim".
	//`paused` is the application's own answer, never inferred from the bar.
	[AvaloniaFact]
	public void The_lamps_come_from_the_shell_and_paused_from_the_application()
	{
		WorkspaceShellViewModel shell = new();
		bool paused = false;
		Window window = new();
		window.Show();
		TestHookWiring.WindowTarget target = new(window, () => true, () => paused, () => shell);

		JsonObject lamps = target.State()["lamps"]!.AsObject();
		Assert.True(lamps["shown"]!.GetValue<bool>());
		Assert.Equal(4, lamps["ports"]!.AsArray().Count);
		Assert.Equal("dim", TestHookLamps.Observed(lamps, 1));

		Assert.False(target.State()["paused"]!.GetValue<bool>());
		paused = true;
		Assert.True(target.State()["paused"]!.GetValue<bool>());
	}

	//A window with no Play arbiter has no sheet open over the screen and no bar: `ui.surface` is
	//`none` and the footer is empty, which is a fact of the application and not a missing answer.
	[AvaloniaFact]
	public void A_window_with_no_play_surface_reports_none_and_an_empty_bar()
	{
		Window window = new() { Content = new Button() };
		window.Show();

		JsonObject state = new TestHookWiring.WindowTarget(window, () => false).State();

		Assert.Equal(TestHookSurfaces.None, state["surface"]!.GetValue<string>());
		Assert.Empty(state["footer"]!.AsArray());
	}

	//`ui.items` is the focused list's entries, in the order it draws them, read off the ids the
	//entries carry - the container's own id, or the nearest one inside it.
	[AvaloniaFact]
	public void The_items_are_the_focused_lists_entries_by_id()
	{
		ItemsControl list = new() {
			ItemsSource = new[] { "Contra (USA).nes", "Metroid (USA).nes" },
			ItemTemplate = new FuncDataTemplate<string>((_, _) => new Button { Content = "tile" })
		};
		Window window = new() { Content = Named(list, "play.library") };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		//The entries are named the way the library grid names them (PlayerLibraryTile.EntryId).
		for(int i = 0; i < list.ItemCount; i++) {
			Control container = (Control)list.ContainerFromIndex(i)!;
			container.GetVisualDescendants().OfType<Button>().First().SetValue(
				AutomationProperties.AutomationIdProperty, "play.library.tile." + list.Items[i]);
		}
		//The focus is on the entry itself - the tile inside the container, which is what a pad
		//lands on - and the hook's walk finds the list it belongs to.
		Control second = (Control)list.ContainerFromIndex(1)!;
		Assert.True(second.GetVisualDescendants().OfType<Button>().First().Focus(NavigationMethod.Directional));

		JsonArray items = new TestHookWiring.WindowTarget(window, () => false).State()["items"]!.AsArray();
		Assert.Equal(new[] { "play.library.tile.Contra (USA).nes", "play.library.tile.Metroid (USA).nes" },
			items.Select(item => item!.GetValue<string>()).ToArray());
	}

	//`ui.haptics` counts the tick requests the menu made on the pad in hand, recorded at the
	//HapticTickOutput seam (#1106), and "since the last step" means since the request that
	//started it: a state read in between (a wait polls state, ADR-0272 item 4) consumes nothing.
	[AvaloniaFact]
	public void The_haptics_count_the_tick_requests_since_the_last_step()
	{
		Window window = new() { Content = new Button() };
		window.Show();
		TestHookHaptics counters = new();
		TestHookWiring.WindowTarget target = new(window, () => false, () => false, null, counters);

		Assert.Empty(target.State()["haptics"]!.AsArray());

		HapticTickOutput.SetSeamsForTest(null, _ => { });
		HapticTickOutput.Observed = counters.Record;
		try {
			HapticTickOutput.Tick(0);
			HapticTickOutput.Tick(0);
			HapticTickOutput.Tick(1);

			//A state read is what a wait polls: it must not look like a step boundary.
			JsonArray haptics = target.State()["haptics"]!.AsArray();
			Assert.Equal(new[] { 1, 2 }, haptics.Select(entry => entry!["pad"]!.GetValue<int>()).ToArray());
			Assert.Equal(new[] { 2, 1 }, haptics.Select(entry => entry!["count"]!.GetValue<int>()).ToArray());

			target.Step();
			Assert.Empty(target.State()["haptics"]!.AsArray());
		} finally {
			HapticTickOutput.Observed = null;
			HapticTickOutput.SetSeamsForTest(null, null);
		}
	}
}
