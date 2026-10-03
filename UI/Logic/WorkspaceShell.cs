using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//G.1 (PRD Part B §8, ADR-0241, §13.2/§13.5.1): the three task workspaces and
//the shell's host-free rules. Persisted as PreferencesConfig.Workspace, a key
//separate from UiMode - the UiMode values are never reinterpreted as
//workspaces (ADR-0241, "Advanced tools are an escape hatch").
public enum Workspace
{
	//Zero value is the default for a settings.json without the key.
	Play,
	Remaster,
	Share
}

public sealed record WorkspaceSwitcherRow(Workspace Workspace, bool IsActive);

public static class WorkspaceShell
{
	//W-S3: fixed order 1. Play, 2. Remaster, 3. Share (user's decision,
	//2026-10-02). It never changes with the active profile, recent use or the
	//loaded console; ⌘1/⌘2/⌘3 (Ctrl elsewhere) follow the same positions.
	public static IReadOnlyList<Workspace> Ordered { get; } = new[] { Workspace.Play, Workspace.Remaster, Workspace.Share };

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
	//always visible.
	public static bool IsBarVisible(Workspace workspace, bool gameRunning, bool paused, bool sheetOpen = false)
	{
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

public enum ShellStatusKind
{
	NoGame,
	Playing,
	PlayingWithPack,
	Paused,
	PausedWithPack
}

//W-S1: the status line is one read-only sentence. The owning ViewModel maps
//the kind to its localized sentence (game and pack names as arguments).
public static class ShellStatusLine
{
	public static ShellStatusKind Classify(bool gameLoaded, bool paused, string packName)
	{
		if(!gameLoaded) {
			return ShellStatusKind.NoGame;
		}
		bool hasPack = !string.IsNullOrWhiteSpace(packName);
		if(paused) {
			return hasPack ? ShellStatusKind.PausedWithPack : ShellStatusKind.Paused;
		}
		return hasPack ? ShellStatusKind.PlayingWithPack : ShellStatusKind.Playing;
	}
}

//PRD Part B §13.2 and §13.8 Q4 (user's decision, 2026-10-02):
//ShowClassicMenuBar is false everywhere, upgrades included, and an upgraded
//install gets a one-time "your menus are under Tools ⋯" toast instead of
//keeping the bar. This replaces UiMode as §6's "upgrade keeps my menus" rule.
public static class ClassicMenuNotice
{
	public const bool DefaultShowClassicMenuBar = false;

	//The PreferencesConfig.ClassicMenuNoticeShown value while its key is
	//absent: a fresh install (no settings.json, Configuration.CreateConfig)
	//never had a menu bar, so there is nothing to tell it; an existing
	//settings.json without the key is an upgrade and still owes the toast.
	public static bool ShownForMissingKey(bool settingsFileExists)
	{
		return !settingsFileExists;
	}

	public static bool ShouldShow(bool alreadyShown, bool showClassicMenuBar)
	{
		return !alreadyShown && !showClassicMenuBar;
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

	public static bool ExtendsIntoTitleBar(bool isMacOS)
	{
		return isMacOS;
	}

	//macOS fullscreen has no traffic lights at rest, so the bar starts at the
	//edge; a plain in-window strip never needs the inset.
	public static double LeadingInset(bool extended, bool fullScreen)
	{
		return extended && !fullScreen ? MacTrafficLightInset : 0;
	}
}
