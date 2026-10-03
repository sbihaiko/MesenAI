using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//ADR-0250: every menu entry has one place per door. The doors' menus are
//selected here, host-free (UI.Tests/Shell/WorkspaceMenuTests): the task
//doors' short Tools ⋯ (Decision 3), the shared tail, the Classic menu bar
//without its duplicates (Decision 4), and what the doors' own screens already
//hold - the pause overlay, the Remaster project chip and the homes - so the
//two guards (Decision 5) can see every surface. Hiding is presentation
//(Decision 6): MainMenuViewModel builds the actions and tags each with its
//MenuEntry; this only decides which entries a door shows.

//One action, wherever it is shown. A value that no door places fails the
//"belongs to no door" guard, so every new menu action must be assigned here.
public enum MenuEntry
{
	//Classic File
	OpenRom,
	SaveState,
	LoadState,
	LoadLastSession,
	RecentFiles,
	Exit,

	//Classic Game
	PauseResume,
	Reset,
	PowerCycle,
	ReloadRom,
	PowerOff,
	GameConfig,
	FdsSelectDisk,
	FdsEjectDisk,
	InsertCoin1,
	InsertCoin2,
	InsertCoin3,
	InsertCoin4,
	InputBarcode,
	TapeRecorder,

	//Classic Options
	Speed,
	VideoScale,
	VideoFilter,
	Shaders,
	AspectRatio,
	Region,
	GameboyModel,
	AudioSettings,
	EmulationSettings,
	InputSettings,
	VideoSettings,
	NesSettings,
	GameboySettings,
	GbaSettings,
	SmsSettings,
	Preferences,

	//Classic Tools (Movies ▸ and HD Packs ▸ list their children: Decision 4
	//touches them)
	Cheats,
	HistoryViewer,
	Movies,
	MoviePlay,
	MovieRecord,
	RecordAndShare,
	MovieStop,
	NetPlay,
	SoundRecorder,
	VideoRecorder,
	MusicRecorder,
	LiveRecorder,
	HdPacks,
	HdPackBuilder,
	ReloadPackImages,
	EnhancementPacks,
	LogWindow,
	Screenshot,

	//Classic Debug
	Debugger,
	SpcDebugger,
	Cx4Debugger,
	NecDspDebugger,
	GsuDebugger,
	Sa1Debugger,
	St018Debugger,
	GameboyDebugger,
	EventViewer,
	MemoryTools,
	RegisterViewer,
	TraceLogger,
	TilemapViewer,
	TileViewer,
	SpriteViewer,
	PaletteViewer,
	Assembler,
	DebugLog,
	MemorySearch,
	Profiler,
	ScriptWindow,
	WatchWindow,
	GameboyTilemapViewer,
	GameboyTileViewer,
	GameboySpriteViewer,
	GameboyPaletteViewer,
	GameboyEventViewer,
	GameboyAssembler,
	NesHeaderEditor,
	DebugSettings,

	//Classic Help
	CommandLineHelp,
	CheckForUpdates,
	About,

	//Classic Workspace ▸ (the switcher inside Classic, which has no shell bar)
	SwitchToPlay,
	SwitchToRemaster,
	SwitchToShare,

	//Task doors' Tools ⋯ only
	Fullscreen,
	Record,
	Help,
	Settings,

	//The doors' own screens (not menu items, listed for the duplicate guard)
	SaveStates,
	Pack,
	Enhancements,
	QuitGame,
	ShowProjectFolder,
	SwitchProject,
	ComposeScene,
	RemasterRecord,
	RemasterBuild,
	SharePack
}

//Where an entry is shown within a door. AppMenu is the shared tail (About,
//Settings…, Quit): the system app menu on macOS, the end of Tools ⋯
//elsewhere. It is not one of Decision 1's surfaces.
public enum MenuSurface
{
	ToolsMenu,
	AppMenu,
	ClassicMenuBar,
	PauseOverlay,
	ProjectChip,
	Home
}

public enum ClassicMenu
{
	File,
	Game,
	Options,
	Tools,
	Debug,
	Help,
	Workspace
}

public sealed record MenuPlacement(MenuSurface Surface, MenuEntry Entry);

public sealed record ClassicMenuContent(ClassicMenu Menu, IReadOnlyList<MenuEntry> Entries);

//What the loaded game uses, for Play's console items (Decision 3).
public sealed record GameCapabilities(bool FdsDisk = false, bool VsSystem = false, bool VsDualSystem = false, bool Barcode = false, bool Tape = false)
{
	public static GameCapabilities None { get; } = new();
	public static GameCapabilities All { get; } = new(true, true, true, true, true);
}

public static class WorkspaceMenu
{
	//Decision 4: the Super Game Boy viewers keep both entries, the second set
	//labelled "(Game Boy)" so the same name never appears twice.
	public const string SuperGameBoyViewerHint = "Game Boy";

	private static readonly MenuEntry[] ClassicFile = {
		MenuEntry.OpenRom, MenuEntry.SaveState, MenuEntry.LoadState, MenuEntry.LoadLastSession, MenuEntry.RecentFiles, MenuEntry.Exit
	};

	private static readonly MenuEntry[] ClassicGame = {
		MenuEntry.PauseResume, MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.ReloadRom, MenuEntry.PowerOff, MenuEntry.GameConfig,
		MenuEntry.FdsSelectDisk, MenuEntry.FdsEjectDisk, MenuEntry.InsertCoin1, MenuEntry.InsertCoin2, MenuEntry.InsertCoin3, MenuEntry.InsertCoin4,
		MenuEntry.InputBarcode, MenuEntry.TapeRecorder
	};

	private static readonly MenuEntry[] ClassicOptions = {
		MenuEntry.Speed, MenuEntry.VideoScale, MenuEntry.VideoFilter, MenuEntry.Shaders, MenuEntry.AspectRatio, MenuEntry.Region, MenuEntry.GameboyModel,
		MenuEntry.AudioSettings, MenuEntry.EmulationSettings, MenuEntry.InputSettings, MenuEntry.VideoSettings,
		MenuEntry.NesSettings, MenuEntry.GameboySettings, MenuEntry.GbaSettings, MenuEntry.SmsSettings, MenuEntry.Preferences
	};

	//Movies ▸ keeps Play, Record and Stop; Record and Share is Share's.
	//HD Packs ▸ loses Install HD Pack, merged into Enhancement Packs.
	private static readonly MenuEntry[] ClassicTools = {
		MenuEntry.Cheats, MenuEntry.HistoryViewer, MenuEntry.Movies, MenuEntry.MoviePlay, MenuEntry.MovieRecord, MenuEntry.MovieStop, MenuEntry.NetPlay,
		MenuEntry.SoundRecorder, MenuEntry.VideoRecorder, MenuEntry.MusicRecorder, MenuEntry.LiveRecorder,
		MenuEntry.HdPacks, MenuEntry.HdPackBuilder, MenuEntry.ReloadPackImages, MenuEntry.EnhancementPacks, MenuEntry.LogWindow, MenuEntry.Screenshot
	};

	private static readonly MenuEntry[] ClassicDebug = {
		MenuEntry.Debugger, MenuEntry.SpcDebugger, MenuEntry.Cx4Debugger, MenuEntry.NecDspDebugger, MenuEntry.GsuDebugger, MenuEntry.Sa1Debugger,
		MenuEntry.St018Debugger, MenuEntry.GameboyDebugger, MenuEntry.EventViewer, MenuEntry.MemoryTools, MenuEntry.RegisterViewer, MenuEntry.TraceLogger,
		MenuEntry.TilemapViewer, MenuEntry.TileViewer, MenuEntry.SpriteViewer, MenuEntry.PaletteViewer, MenuEntry.Assembler, MenuEntry.DebugLog,
		MenuEntry.MemorySearch, MenuEntry.Profiler, MenuEntry.ScriptWindow, MenuEntry.WatchWindow,
		MenuEntry.GameboyTilemapViewer, MenuEntry.GameboyTileViewer, MenuEntry.GameboySpriteViewer, MenuEntry.GameboyPaletteViewer,
		MenuEntry.GameboyEventViewer, MenuEntry.GameboyAssembler, MenuEntry.NesHeaderEditor, MenuEntry.DebugSettings
	};

	//The two dead entries (Online Help, Report Bug) are gone.
	private static readonly MenuEntry[] ClassicHelp = { MenuEntry.CommandLineHelp, MenuEntry.CheckForUpdates, MenuEntry.About };

	private static readonly MenuEntry[] ClassicWorkspace = { MenuEntry.SwitchToPlay, MenuEntry.SwitchToRemaster, MenuEntry.SwitchToShare };

	//On macOS these live in the system app menu instead (Decision 3/4).
	private static readonly MenuEntry[] MovedToMacAppMenu = { MenuEntry.About, MenuEntry.Preferences, MenuEntry.Exit };

	//Decision 3's "lives elsewhere in the door" column.
	private static readonly MenuPlacement[] PlayScreens = {
		new(MenuSurface.Home, MenuEntry.OpenRom),
		new(MenuSurface.Home, MenuEntry.RecentFiles),
		new(MenuSurface.PauseOverlay, MenuEntry.PauseResume),
		new(MenuSurface.PauseOverlay, MenuEntry.SaveStates),
		new(MenuSurface.PauseOverlay, MenuEntry.Pack),
		new(MenuSurface.PauseOverlay, MenuEntry.Enhancements),
		new(MenuSurface.PauseOverlay, MenuEntry.Cheats),
		new(MenuSurface.PauseOverlay, MenuEntry.Settings),
		new(MenuSurface.PauseOverlay, MenuEntry.QuitGame)
	};

	private static readonly MenuPlacement[] RemasterScreens = {
		new(MenuSurface.ProjectChip, MenuEntry.ShowProjectFolder),
		new(MenuSurface.ProjectChip, MenuEntry.SwitchProject),
		new(MenuSurface.ProjectChip, MenuEntry.ComposeScene),
		new(MenuSurface.Home, MenuEntry.RemasterRecord),
		new(MenuSurface.Home, MenuEntry.RemasterBuild)
	};

	private static readonly MenuPlacement[] ShareScreens = {
		new(MenuSurface.Home, MenuEntry.RecordAndShare),
		new(MenuSurface.Home, MenuEntry.SharePack)
	};

	//The task door's Tools ⋯, as groups (a separator between two groups).
	//Classic has none: its menus are its menu bar. The last group is the
	//shared tail - Help ▸ only on macOS, where About, Settings… and Quit are
	//in the app menu; Settings…, Help ▸, About, Quit elsewhere.
	public static IReadOnlyList<IReadOnlyList<MenuEntry>> ToolsMenu(Workspace door, bool isMacOS, GameCapabilities game)
	{
		List<IReadOnlyList<MenuEntry>> groups = new();
		switch(door) {
			case Workspace.Play:
				groups.Add(PlayConsoleGroup(game));
				groups.Add(new[] { MenuEntry.Screenshot, MenuEntry.Fullscreen });
				break;
			case Workspace.Remaster:
				groups.Add(new[] { MenuEntry.ReloadPackImages, MenuEntry.MusicRecorder });
				groups.Add(new[] { MenuEntry.EnhancementPacks, MenuEntry.LogWindow });
				break;
			case Workspace.Share:
				groups.Add(new[] { MenuEntry.MoviePlay, MenuEntry.Record, MenuEntry.NetPlay });
				groups.Add(new[] { MenuEntry.Screenshot });
				break;
			default:
				return groups;
		}
		groups.Add(isMacOS ? new[] { MenuEntry.Help } : new[] { MenuEntry.Settings, MenuEntry.Help, MenuEntry.About, MenuEntry.Exit });
		return groups;
	}

	//Reset · Power Cycle, then the console items the loaded game uses.
	private static IReadOnlyList<MenuEntry> PlayConsoleGroup(GameCapabilities game)
	{
		List<MenuEntry> group = new() { MenuEntry.Reset, MenuEntry.PowerCycle };
		if(game.FdsDisk) {
			group.Add(MenuEntry.FdsSelectDisk);
			group.Add(MenuEntry.FdsEjectDisk);
		}
		if(game.VsSystem || game.VsDualSystem) {
			group.Add(MenuEntry.InsertCoin1);
			group.Add(MenuEntry.InsertCoin2);
		}
		if(game.VsDualSystem) {
			group.Add(MenuEntry.InsertCoin3);
			group.Add(MenuEntry.InsertCoin4);
		}
		if(game.Barcode) {
			group.Add(MenuEntry.InputBarcode);
		}
		if(game.Tape) {
			group.Add(MenuEntry.TapeRecorder);
		}
		return group;
	}

	//The shared tail's app-menu part. A task door's Settings… is the Player
	//sheet (W-P8); Classic's is the Options window's Preferences, and Classic
	//keeps About, Preferences and Exit in its menu bar off macOS.
	public static IReadOnlyList<MenuEntry> AppMenu(Workspace door, bool isMacOS)
	{
		if(door == Workspace.Classic) {
			return isMacOS ? MovedToMacAppMenu : Array.Empty<MenuEntry>();
		}
		return new[] { MenuEntry.About, MenuEntry.Settings, MenuEntry.Exit };
	}

	//Classic's in-window menu bar, in order. Submenus whose children
	//Decision 4 touches (Movies ▸, HD Packs ▸) list those children after
	//them; the entries are flat, in display order.
	public static IReadOnlyList<ClassicMenuContent> ClassicMenuBar(bool isMacOS)
	{
		return new[] {
			Menu(ClassicMenu.File, ClassicFile, isMacOS),
			Menu(ClassicMenu.Game, ClassicGame, isMacOS),
			Menu(ClassicMenu.Options, ClassicOptions, isMacOS),
			Menu(ClassicMenu.Tools, ClassicTools, isMacOS),
			Menu(ClassicMenu.Debug, ClassicDebug, isMacOS),
			Menu(ClassicMenu.Help, ClassicHelp, isMacOS),
			Menu(ClassicMenu.Workspace, ClassicWorkspace, isMacOS)
		};
	}

	private static ClassicMenuContent Menu(ClassicMenu menu, MenuEntry[] entries, bool isMacOS)
	{
		return new ClassicMenuContent(menu, entries.Where(e => !isMacOS || !MovedToMacAppMenu.Contains(e)).ToArray());
	}

	//The filter MainMenuViewModel applies to the actions it built for the
	//classic bar: an entry not listed in ClassicMenuBar is hidden there.
	public static bool ShowsInClassic(MenuEntry entry, bool isMacOS)
	{
		return ClassicMenuBar(isMacOS).Any(m => m.Entries.Contains(entry));
	}

	//Every entry the door shows, on every surface, in surface order.
	public static IReadOnlyList<MenuPlacement> Placements(Workspace door, bool isMacOS, GameCapabilities game)
	{
		List<MenuPlacement> placements = new();
		HashSet<MenuEntry> tail = new(AppMenu(door, isMacOS));
		if(door == Workspace.Classic) {
			foreach(ClassicMenuContent menu in ClassicMenuBar(isMacOS)) {
				placements.AddRange(menu.Entries.Select(e => new MenuPlacement(MenuSurface.ClassicMenuBar, e)));
			}
		} else {
			foreach(IReadOnlyList<MenuEntry> group in ToolsMenu(door, isMacOS, game)) {
				placements.AddRange(group.Where(e => !tail.Contains(e)).Select(e => new MenuPlacement(MenuSurface.ToolsMenu, e)));
			}
		}
		placements.AddRange(tail.Select(e => new MenuPlacement(MenuSurface.AppMenu, e)));
		placements.AddRange(door switch {
			Workspace.Play => PlayScreens,
			Workspace.Remaster => RemasterScreens,
			Workspace.Share => ShareScreens,
			_ => Array.Empty<MenuPlacement>()
		});
		return placements;
	}
}

//Decision 4: Classic's four Pause/Resume variants are one entry whose label
//follows the state. With "pause when in menus" on, opening the menu already
//paused the game (AutoPaused): Pause keeps it paused once the menu closes,
//Resume (the user had paused before) resumes it then - the click only flips
//AutoPaused. Otherwise the click is the Pause shortcut.
public static class PauseMenuEntry
{
	public static bool ShowsResume(bool pauseWhenInMenus, bool autoPaused, bool paused)
	{
		return pauseWhenInMenus ? !autoPaused : paused;
	}

	public static bool TogglesAutoPause(bool pauseWhenInMenus)
	{
		return pauseWhenInMenus;
	}
}
