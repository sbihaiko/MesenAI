using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mesen.Logic.TestHook;

//#1282 (ADR-0272 item 3, ADR-0271): what a player judges by eye, as the hook's
//own state - the focus ring, the action bar, the port lamps, the focused list's
//entries and the haptic ticks the menu asked for. Every value here comes from
//the application's UI state (a control's focus, the surface's declared bar, the
//shell's lamps, a control's own AutomationId, the tick seam), never from a
//pixel: nothing in this file can see what was drawn.

//The focus ring: PlayerTheme paints it from :focus-visible, which is the focus
//PlayFocusOnOpen.Enter takes with NavigationMethod.Directional. A control that
//is focused without it (the renderer panel, a host's own Focus()) is focused
//with no ring - the state a pad player must never be left in (#824, #1232).
public static class TestHookRing
{
	public const string None = "none";

	public static JsonObject Of(bool visible, string? target) => new() {
		["visible"] = visible,
		["target"] = target
	};

	//What a check reads: the id the ring is on, or `none`. `ui.ring == <id>` is
	//"the ring is visible on that control" in one comparison, and
	//`ui.ring == none` is the ring being nowhere (GAME-02: the pad is the
	//console's while a game runs, so nothing in the GUI reacts).
	public static string Observed(bool visible, string? target)
		=> visible && target is { Length: > 0 } ? target : None;
}

//The action bar: what the surface holding the focus declared (PlayFocusOnOpen.
//Declared), in the order it declared it. The id is the PlayAction - the label is
//localized text and never a check (ADR-0272 item 3).
public static class TestHookFooter
{
	public static string Id(PlayAction action) => action switch {
		PlayAction.Move => "move",
		PlayAction.Confirm => "confirm",
		PlayAction.Favorite => "favorite",
		PlayAction.Search => "search",
		PlayAction.Settings => "settings",
		PlayAction.ConsoleFilter => "console-filter",
		PlayAction.Back => "back",
		_ => throw new ArgumentOutOfRangeException(nameof(action), action, "no footer id for that action")
	};

	//The bar as one string: `confirm,settings`. A surface that declared nothing
	//(the home's first run has no Back, a sheet under construction) is empty,
	//which is what `ui.footer ==` with nothing after it reads.
	public static string Observed(IEnumerable<PlayBarEntry>? entries)
		=> string.Join(",", (entries ?? Array.Empty<PlayBarEntry>()).Select(e => Id(e.Action)));

	public static JsonArray Of(IEnumerable<PlayBarEntry>? entries)
		=> new(Observed(entries).Length == 0
			? Array.Empty<JsonNode?>()
			: Observed(entries).Split(',').Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
}

//The focused list's entries: the ids of the entries the list has realized, in
//the order it draws them. An entry with no AutomationId is invisible to the hook
//(ADR-0272 item 3), exactly as a control without one is - which is why the
//library grid, the Favorites and the Recent shelves name their entries.
public static class TestHookItems
{
	public const string None = "none";

	public static string[] Ids(IEnumerable<string?> ids)
		=> ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToArray();

	public static string Observed(IEnumerable<string?> ids)
	{
		string[] named = Ids(ids);
		return named.Length == 0 ? None : string.Join(",", named);
	}

	public static JsonArray Of(IEnumerable<string?> ids)
		=> new(Ids(ids).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
}

//The port lamps: the shell's own strip (PadPortLamps, ADR-0249/ADR-0261), one
//lamp per port. `shown` is the status line's own visibility - while the game
//runs unpaused the bar and its lamps are hidden (ADR-0261 Consequences), and a
//lamp that is not drawn is never read as "dim".
public static class TestHookLamps
{
	public const string Lit = "lit";
	public const string Dim = "dim";
	public const string Hidden = "hidden";

	public static JsonObject Of(PadPortStrip strip, bool shown) => new() {
		["shown"] = shown,
		["ports"] = new JsonArray(strip.Lamps.Select(lamp => (JsonNode?)new JsonObject {
			["port"] = lamp.Port,
			["lit"] = lamp.IsLit,
			//For a person reading the state; a check matches the port and the
			//lamp, never the pad's name (ADR-0272 item 3).
			["name"] = lamp.Name
		}).ToArray())
	};

	//The port a check names: one of the four, 1-based as the lamps' own labels
	//are ("P1"). Anything else is not a port, and the adapter answers a check
	//that names it with `unknown id` rather than a false lamp.
	public static bool TryPort(string? text, out int port)
	{
		port = 0;
		return int.TryParse(text, out port) && port >= 1 && port <= PadPortLamps.Ports;
	}

	//What a check reads for one port: `lit`, `dim`, or `hidden` while the status
	//line is not drawn at all.
	public static string Observed(JsonObject lamps, int port)
	{
		if(!lamps["shown"]!.GetValue<bool>()) {
			return Hidden;
		}
		foreach(JsonNode? node in lamps["ports"]!.AsArray()) {
			if(node is JsonObject lamp && lamp["port"]!.GetValue<int>() == port) {
				return lamp["lit"]!.GetValue<bool>() ? Lit : Dim;
			}
		}
		return Hidden;
	}
}

//#1282 (owner's added scope, 2026-10-10): the haptic tick requests the menu
//made, per pad, since the last step. Recorded at HapticTickOutput.Tick - the
//seam #1106 left - so a run proves the menu asked for a tick on the pad in hand
//with no physical motor anywhere near it. The pad is named the way the lamps
//name it: port 1 is the first pad.
//
//"Since the last step" is the protocol's own step boundary: a step's `inject`
//restarts the counts, so the ticks a step's press caused are exactly what its
//check reads - state reads in between (a wait polls state, ADR-0272 item 4) do
//not consume anything.
public sealed class TestHookHaptics
{
	private readonly Dictionary<int, int> _counts = new();

	public void Record(uint padIndex)
	{
		int port = (int)padIndex + 1;
		_counts[port] = Count(port) + 1;
	}

	//A step begins: the ticks from here on belong to it.
	public void Restart() => _counts.Clear();

	public int Count(int port) => _counts.TryGetValue(port, out int count) ? count : 0;

	public JsonArray Take() => new(_counts.OrderBy(pair => pair.Key)
		.Select(pair => (JsonNode?)new JsonObject { ["pad"] = pair.Key, ["count"] = pair.Value })
		.ToArray());
}
