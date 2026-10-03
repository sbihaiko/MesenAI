using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//G.1 (PRD Part B §8, ADR-0241, §13.2/§13.5.1): the workspaces ("doors") and
//the shell's host-free rules. Persisted as PreferencesConfig.Workspace.
//ADR-0250 Decision 2: Classic is the fourth door and the original Mesen GUI;
//it owns UiMode.Advanced (entering it sets Advanced, leaving it for a task
//door sets Player), so UiMode is now derived from the door.
public enum Workspace
{
	//Zero value is the default for a settings.json without the key.
	Play,
	Remaster,
	Share,
	Classic
}

public sealed record WorkspaceSwitcherRow(Workspace Workspace, bool IsActive);

public static class WorkspaceShell
{
	//W-S3: fixed order 1. Play, 2. Remaster, 3. Share (user's decision,
	//2026-10-02), 4. Classic (ADR-0250). It never changes with the active
	//profile, recent use or the loaded console; ⌘1-⌘4 (Ctrl elsewhere) follow
	//the same positions.
	public static IReadOnlyList<Workspace> Ordered { get; } = new[] { Workspace.Play, Workspace.Remaster, Workspace.Share, Workspace.Classic };

	//ADR-0250 Decision 2: Classic is the Advanced GUI; every task door is Player.
	public static UiMode UiModeFor(Workspace door)
	{
		return door == Workspace.Classic ? UiMode.Advanced : UiMode.Player;
	}

	//The door the window opens in. Play is the default (a fresh install, a
	//settings file without the key, an unknown value); an upgraded install
	//whose UiMode is Advanced opens in Classic, so nobody loses the GUI they
	//chose.
	public static Workspace InitialDoor(UiMode uiMode, Workspace persisted)
	{
		//UiMode decides between Classic and the task doors (a Player file that
		//names Classic was edited by hand: it opens in Play, as DoorForUiMode).
		return DoorForUiMode(uiMode, Enum.IsDefined(persisted) ? persisted : Workspace.Play);
	}

	//Every place that flips UiMode goes through the door: Advanced is Classic,
	//and Player leaves Classic for Play (a task door stays where it is).
	public static Workspace DoorForUiMode(UiMode uiMode, Workspace current)
	{
		if(uiMode == UiMode.Advanced) {
			return Workspace.Classic;
		}
		return current == Workspace.Classic ? Workspace.Play : current;
	}

	//The game's own screen (renderer, Play home or the classic game list,
	//music player): Play's, and Classic's plain game view.
	public static bool ShowsGameScreen(Workspace door)
	{
		return door == Workspace.Play || door == Workspace.Classic;
	}

	public static Workspace? FromShortcutDigit(int digit)
	{
		return digit >= 1 && digit <= Ordered.Count ? Ordered[digit - 1] : null;
	}

	public static int ShortcutDigit(Workspace workspace)
	{
		for(int i = 0; i < Ordered.Count; i++) {
			if(Ordered[i] == workspace) {
				return i + 1;
			}
		}
		return 0;
	}

	//W-S1: the bar (and the status line under the content) is hidden while a
	//game runs in Play and nothing is paused - the game fills the window. A
	//pause (Esc opens the Player overlay, which pauses; Advanced's Esc is
	//Pause) brings it back, and so does a Play sheet on screen over the game
	//(W-P5's first-start picker does not pause it; the render keeps the bar
	//and the status line around its scrim). In Remaster and Share it is
	//always visible. Classic (ADR-0250) never shows it: the original GUI has
	//its classic menu bar, whose Workspace menu is the switcher there.
	public static bool IsBarVisible(Workspace workspace, bool gameRunning, bool paused, bool sheetOpen = false)
	{
		if(workspace == Workspace.Classic) {
			return false;
		}
		return workspace != Workspace.Play || !gameRunning || paused || sheetOpen;
	}
}

//The workspace switch. Deliberately holds nothing but the active workspace:
//switching changes what the window shows and never stops the game, pauses it,
//rewrites settings, picks a pack or publishes anything (§13.2), so there is no
//emulator command for this type to emit.
public sealed class WorkspaceState
{
	public Workspace Active { get; private set; }

	public WorkspaceState(Workspace initial = Workspace.Play)
	{
		Active = Enum.IsDefined(initial) ? initial : Workspace.Play;
	}

	public bool IsPlay => Active == Workspace.Play;

	public IReadOnlyList<WorkspaceSwitcherRow> Rows
	{
		get
		{
			List<WorkspaceSwitcherRow> rows = new();
			foreach(Workspace w in WorkspaceShell.Ordered) {
				rows.Add(new WorkspaceSwitcherRow(w, w == Active));
			}
			return rows;
		}
	}

	//Returns true when the active workspace changed.
	public bool Select(Workspace target)
	{
		if(!Enum.IsDefined(target) || target == Active) {
			return false;
		}
		Active = target;
		return true;
	}
}

//W-S1: the status line reads like the renders, "Contra (USA) · pack Contra 80s
//1.2". A pack named after the ROM is the player's own project or the automatic
//upscale, never repeated as if it were a pack's name.
public enum ShellPackKind
{
	None,
	Named,
	Project,
	AutoUpscale
}

public static class ShellStatusLine
{
	public static ShellPackKind PackKind(string gameName, string packName, bool autoOnly)
	{
		if(string.IsNullOrWhiteSpace(packName)) {
			return ShellPackKind.None;
		}
		if(autoOnly) {
			return ShellPackKind.AutoUpscale;
		}
		return string.Equals(packName.Trim(), (gameName ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ? ShellPackKind.Project : ShellPackKind.Named;
	}

	//"Contra 80s 1.2"; a pack.json without a version reads as 0.0.0, never shown.
	public static string PackLabel(string name, string version)
	{
		string v = (version ?? "").Trim();
		return v.Length == 0 || v == "0.0.0" ? name : name + " " + v;
	}

	public static string Compose(string gameName, string packPart)
	{
		return string.IsNullOrWhiteSpace(packPart) ? gameName : gameName + " · " + packPart;
	}

	//W-R1/W-R5: in Remaster at rest the line follows the project's progress,
	//"Contra (USA) · playing your project · 412 cells painted". An unknown
	//count (ADR-0252: no file can say) adds no words.
	public static string ComposeRemaster(string gameName, string packPart, string paintedCells)
	{
		string line = Compose(gameName, packPart);
		return string.IsNullOrWhiteSpace(paintedCells) ? line : line + " · " + paintedCells.Trim();
	}
}

//G.1 (PRD Part B §13.5.1 W-S1; user's choice 2026-10-02, "Integrar agora"):
//on macOS the shell bar is drawn in the window's title bar (Avalonia's
//client-area extension) with the traffic lights at its leading edge. Windows
//and Linux keep the in-window strip under the system title bar: drawing
//caption buttons there would be new chrome to maintain per OS and window
//manager, for no W-S1 requirement.
public static class ShellTitleBar
{
	//W-S1's bar height, also the extended title bar's height hint.
	public const double Height = 52;

	//Room the macOS traffic lights take at the bar's leading edge (three
	//buttons plus their margins), so the profile button never sits under them.
	public const double MacTrafficLightInset = 78;

	//Height of the strip kept above the game while the bar is hidden. The
	//native game view covers the title-bar zone and swallows the mouse-down
	//there (airspace: an Avalonia control cannot sit over it), so the window
	//could not be dragged; reserving a strip the view does not cover keeps a
	//normal draggable top edge. About a macOS title bar (the traffic lights
	//sit in it); the game loses these pixels to its letterbox.
	public const double DragStripSize = 28;

	public static bool ExtendsIntoTitleBar(bool isMacOS)
	{
		return isMacOS;
	}

	//ADR-0250: Classic has no shell bar; it keeps the plain title bar with
	//its classic menu bar under it, as the original GUI had it.
	public static bool ExtendsIntoTitleBar(bool isMacOS, Workspace door)
	{
		return ExtendsIntoTitleBar(isMacOS) && door != Workspace.Classic;
	}

	//macOS fullscreen has no traffic lights at rest, so the bar starts at the
	//edge; a plain in-window strip never needs the inset.
	public static double LeadingInset(bool extended, bool fullScreen)
	{
		return extended && !fullScreen ? MacTrafficLightInset : 0;
	}

	//The reserved drag strip: only on an extended (macOS) window, windowed,
	//and only while the bar is hidden - a visible bar is the drag area itself,
	//and fullscreen has no title bar to drag.
	public static double DragStripHeight(bool extended, bool fullScreen, bool barVisible)
	{
		return extended && !fullScreen && !barVisible ? DragStripSize : 0;
	}
}
