using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//#1282 (ADR-0272 item 3): the Play sheets and overlays a run can be standing on.
//
//`ui.screen` names the surface the player is looking at (TestHookScreens); this
//is the other half of the same question - WHICH sheet or overlay is open over
//it - and a script that judges "the pause overlay is up" by eye reads it here.
//The names are the ones the pilot script's families already use
//(docs/validation/process/play-pad-only*), so a surface and the controls on it
//read as one family: `play.settings` and `play.settings.rumble`.
//
//A surface that gains a claim in the arbiter (PlayFocusOnOpen) without gaining
//a name here has no `ui.surface`: the id is named at the claim, never derived
//from the controls that happen to be on screen - two sheets share the same
//control kinds, and that is exactly the mistake `ui.screen` made in #1228.
public static class TestHookSurfaces
{
	public const string QuitGame = "play.quit-game";
	public const string SelectRom = "play.select-rom";
	public const string Shader = "play.shader";
	public const string Tool = "play.tool";
	public const string Bios = "play.bios";
	public const string ControllerSetup = "play.controller-setup";
	//The Settings sheet's System tab is a surface of its own (ADR-0256 Decision
	//8), claimed over the sheet that holds it.
	public const string SettingsSystemTab = "play.settings.system";
	public const string Settings = "play.settings";
	public const string ControllerSheet = "play.controller-sheet";
	public const string PackDep = "play.pack-dep";
	public const string PackPicker = "play.pack-picker";
	public const string Enhancements = "play.enhancements";
	public const string PackDetail = "play.pack-detail";
	public const string Cheats = "play.cheats";
	public const string Replays = "play.replays";
	public const string SaveStates = "play.save-states";
	public const string PauseOverlay = "play.pause";
	//The ROM picker sheet's three surfaces (#1032/#1036, ADR-0264): the library,
	//the folder browser and the library-folders list.
	public const string Library = "play.library";
	public const string LibraryBrowse = "play.library-browse";
	public const string LibraryFolders = "play.library-folders";

	//What `ui.surface` reads when nothing is open over the screen.
	public const string None = "none";

	public static readonly IReadOnlyList<string> All = new[] {
		QuitGame, SelectRom, Shader, Tool, Bios, ControllerSetup, SettingsSystemTab,
		Settings, ControllerSheet, PackDep, PackPicker, Enhancements, PackDetail,
		Cheats, Replays, SaveStates, PauseOverlay, Library, LibraryBrowse, LibraryFolders
	};

	//The topmost open surface of a stack given in the arbiter's own order
	//(topmost first - ADR-0249's Esc order, which PlayFocusOnOpen registers in),
	//or null when nothing is open.
	public static string? Topmost(IEnumerable<(string Id, bool IsOpen)> stack)
	{
		foreach((string id, bool isOpen) in stack) {
			if(isOpen) {
				return id;
			}
		}
		return null;
	}

	//What a check reads: the surface's id, or `none` - so `ui.surface == none` is
	//one comparison, like every other check (ADR-0272 item 3).
	public static string Observed(string? topmost) => topmost ?? None;
}
