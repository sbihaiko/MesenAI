using System.Linq;
using System.Text.Json.Nodes;
using Mesen.Logic;
using Mesen.Logic.TestHook;
using Xunit;

namespace Mesen.Tests.TestHook
{
	//#1282 (ADR-0272 item 3, ADR-0271): the checks a player judges by eye, as
	//host-free rules over the window's own state - the focus ring, the action bar,
	//the open surface, the port lamps, the focused list and the haptic ticks the
	//menu asked for. The wiring that feeds them (a real window) is pinned in
	//UI.HeadlessTests; nothing here reads a pixel.
	public class TestHookPlayerStateTests
	{
		//The ring is the theme's PlayerFocusRing, drawn from :focus-visible
		//(PlayFocusOnOpen.Enter focuses Directionally, which is what sets it).
		[Fact]
		public void The_ring_names_the_control_it_is_drawn_on()
		{
			JsonObject ring = TestHookRing.Of(true, "play.home.open-rom");
			Assert.True(ring["visible"]!.GetValue<bool>());
			Assert.Equal("play.home.open-rom", ring["target"]!.GetValue<string>());
			Assert.Equal("play.home.open-rom", TestHookRing.Observed(true, "play.home.open-rom"));
		}

		//A focused control with no ring (the renderer panel takes the focus while a
		//game runs) is `none`, so GAME-02's "no focus ring" is one comparison.
		[Fact]
		public void No_ring_is_none()
		{
			Assert.Equal(TestHookRing.None, TestHookRing.Observed(false, "play.game"));
			Assert.Equal(TestHookRing.None, TestHookRing.Observed(true, null));
			Assert.False(TestHookRing.Of(false, null)["visible"]!.GetValue<bool>());
		}

		//The action bar's entries, in the order the surface declares them - ids off
		//PlayAction, never the localized label (ADR-0272 item 3).
		[Fact]
		public void The_footer_is_the_action_bar_entries_in_order()
		{
			Assert.Equal("confirm,settings", TestHookFooter.Observed(PlayBarDeclarations.Home));
			Assert.Equal("confirm,search,console-filter,back", TestHookFooter.Observed(PlayBarDeclarations.Library));
			Assert.Equal("move,confirm,back", TestHookFooter.Observed(PlayActionBar.KeyboardEntries));
		}

		[Fact]
		public void A_surface_that_declares_no_bar_has_an_empty_footer()
		{
			Assert.Equal("", TestHookFooter.Observed(PlayBarDeclarations.None));
			Assert.Equal("", TestHookFooter.Observed(null));
			Assert.Empty(TestHookFooter.Of(null));
		}

		//#1282: the port lamps, one per port, lit the way PadPortLamps reads them.
		[Fact]
		public void The_lamps_are_the_four_ports_lit_the_way_the_core_reports_them()
		{
			JsonObject lamps = TestHookLamps.Of(PadPortLamps.Build(1, _ => "Pad 1"), shown: true);
			Assert.True(lamps["shown"]!.GetValue<bool>());
			JsonArray ports = lamps["ports"]!.AsArray();
			Assert.Equal(4, ports.Count);
			Assert.True(ports[0]!["lit"]!.GetValue<bool>());
			Assert.Equal(1, ports[0]!["port"]!.GetValue<int>());
			Assert.False(ports[1]!["lit"]!.GetValue<bool>());
		}

		//LAMP-03: while the game runs unpaused the status line - and its lamps - are
		//hidden (ADR-0261 Consequences), and a lamp that is not drawn is never "dim".
		[Fact]
		public void A_hidden_status_line_shows_no_lamp()
		{
			JsonObject lamps = TestHookLamps.Of(PadPortLamps.Build(1, _ => "Pad 1"), shown: false);
			Assert.False(lamps["shown"]!.GetValue<bool>());
			Assert.Equal(TestHookLamps.Hidden, TestHookLamps.Observed(lamps, 1));
			Assert.Equal(TestHookLamps.Lit, TestHookLamps.Observed(TestHookLamps.Of(PadPortLamps.Build(1, _ => "Pad 1"), shown: true), 1));
			Assert.Equal(TestHookLamps.Dim, TestHookLamps.Observed(TestHookLamps.Of(PadPortLamps.Build(1, _ => "Pad 1"), shown: true), 2));
		}

		[Theory]
		[InlineData("1", 1)]
		[InlineData("4", 4)]
		public void A_port_a_lamp_names_is_a_whole_number(string text, int port)
		{
			Assert.True(TestHookLamps.TryPort(text, out int got));
			Assert.Equal(port, got);
		}

		[Theory]
		[InlineData("0")]
		[InlineData("5")]
		[InlineData("")]
		[InlineData("p1")]
		public void A_port_that_is_not_one_of_the_four_is_not_a_lamp(string text)
		{
			Assert.False(TestHookLamps.TryPort(text, out _));
		}

		//The focused list's entries, in the order they are drawn; a list whose
		//entries carry no id (ADR-0272 item 3) is empty, never invented.
		[Fact]
		public void The_items_are_the_focused_lists_entry_ids_in_order()
		{
			Assert.Equal("play.library.tile.a.nes,play.library.tile.b.nes",
				TestHookItems.Observed(new[] { "play.library.tile.a.nes", null, "play.library.tile.b.nes" }));
			Assert.Equal(TestHookItems.None, TestHookItems.Observed(new string?[] { null, "" }));
			Assert.Equal(new[] { "play.home.recent.a.nes" }, TestHookItems.Ids(new[] { "play.home.recent.a.nes", null }));
		}

		//#1282: the haptic tick requests, per pad, since the last step - recorded at
		//HapticTickOutput.Tick's seam so no physical motor is needed.
		[Fact]
		public void Haptic_ticks_are_counted_per_pad_and_restart_with_each_step()
		{
			TestHookHaptics haptics = new();
			haptics.Record(0);
			haptics.Record(0);
			haptics.Record(2);

			JsonArray since = haptics.Take();
			Assert.Equal(2, since.Count);
			Assert.Equal(1, since[0]!["pad"]!.GetValue<int>());
			Assert.Equal(2, since[0]!["count"]!.GetValue<int>());
			Assert.Equal(3, since[1]!["pad"]!.GetValue<int>());
			Assert.Equal(1, since[1]!["count"]!.GetValue<int>());
			Assert.Equal(2, haptics.Count(1));

			haptics.Restart();
			Assert.Empty(haptics.Take());
			Assert.Equal(0, haptics.Count(1));
		}

		//The surfaces a run can be standing on, and the one on top: the stack is the
		//arbiter's own order (ADR-0249's Esc order, topmost first).
		[Fact]
		public void The_topmost_open_surface_is_the_first_open_one_of_the_stack()
		{
			(string, bool)[] stack = {
				(TestHookSurfaces.PauseOverlay, true),
				(TestHookSurfaces.Settings, false),
				(TestHookSurfaces.Library, false)
			};
			Assert.Equal(TestHookSurfaces.PauseOverlay, TestHookSurfaces.Topmost(stack));
			Assert.Equal(TestHookSurfaces.Settings, TestHookSurfaces.Topmost(stack.Select(s => (s.Item1, s.Item1 == TestHookSurfaces.Settings)).ToArray()));
			Assert.Null(TestHookSurfaces.Topmost(stack.Select(s => (s.Item1, false)).ToArray()));
			Assert.Equal(TestHookSurfaces.None, TestHookSurfaces.Observed(null));
			Assert.Equal(TestHookSurfaces.Library, TestHookSurfaces.Observed(TestHookSurfaces.Library));
		}

		//Every surface id is a script-facing name (ADR-0272 item 3): one closed
		//shape, no duplicates, and none of them is a control id of another surface.
		[Fact]
		public void Every_surface_id_is_a_named_play_name()
		{
			Assert.Equal(TestHookSurfaces.All.Count, TestHookSurfaces.All.Distinct().Count());
			foreach(string id in TestHookSurfaces.All) {
				Assert.StartsWith("play.", id);
				Assert.Equal(id.ToLowerInvariant(), id);
				Assert.DoesNotContain(" ", id);
				Assert.DoesNotContain("..", id);
			}
		}
	}
}
