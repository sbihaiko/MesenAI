using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//What a task door's Tools ⋯ entry opens when clicked in Player mode.
//ADR-0250 Decision 3: "No task-door entry opens a classic window: each one
//opens its Player sheet, or is not offered in that door."
public enum EntryOpens
{
	//Acts at once (Reset, Screenshot, Fullscreen…) or answers in place (Quit
	//asks with the InterruptionBar).
	Nothing,
	//The operating system's own file picker (open a replay, save a recording).
	SystemPicker,
	//A sheet inside the main window (Settings, About, Command Line, Check for
	//Updates, the video recorder's settings, a barcode).
	PlayerSheet,
	//A tool window too big for a sheet that opens in the Player look (the
	//`player` class, Player buttons and fields): Enhancement Packs, the log
	//window and the Netplay forms.
	PlayerLookWindow,
	//A classic Mesen window. No task-door entry may open one (guarded by
	//UI.Tests/Shell/DoorEntryOpensTests).
	ClassicWindow
}

//Player-mode sheets that MainWindow hosts for the task doors' Tools ⋯ (and
//the macOS app menu's About), in PlayerToolSheetView.
public enum PlayerToolSheet
{
	None,
	About,
	CommandLine,
	CheckForUpdates,
	VideoRecord,
	Barcode
}

//The host-free table behind the guard: every entry a task door can show (its
//Tools ⋯, with every console item, and the shared tail) and what each of its
//leaves opens. A submenu lists every child it holds. MainMenuViewModel's
//CreateDoorItem builds the actions; UI.HeadlessTests/DoorMenuNoClassicWindowTests
//clicks them and checks the result against this table.
public static class DoorEntryOpensTable
{
	private static readonly Dictionary<MenuEntry, EntryOpens[]> Table = new() {
		[MenuEntry.Reset] = new[] { EntryOpens.Nothing },
		[MenuEntry.PowerCycle] = new[] { EntryOpens.Nothing },
		[MenuEntry.FdsSelectDisk] = new[] { EntryOpens.Nothing },
		[MenuEntry.FdsEjectDisk] = new[] { EntryOpens.Nothing },
		[MenuEntry.InsertCoin1] = new[] { EntryOpens.Nothing },
		[MenuEntry.InsertCoin2] = new[] { EntryOpens.Nothing },
		[MenuEntry.InsertCoin3] = new[] { EntryOpens.Nothing },
		[MenuEntry.InsertCoin4] = new[] { EntryOpens.Nothing },
		//The barcode sheet (PlayerToolSheet.Barcode).
		[MenuEntry.InputBarcode] = new[] { EntryOpens.PlayerSheet },
		//Tape ▸ Play / Record pick a file; Stop acts.
		[MenuEntry.TapeRecorder] = new[] { EntryOpens.SystemPicker, EntryOpens.Nothing },
		[MenuEntry.Screenshot] = new[] { EntryOpens.Nothing },
		[MenuEntry.Fullscreen] = new[] { EntryOpens.Nothing },
		[MenuEntry.ReloadPackImages] = new[] { EntryOpens.Nothing },
		//Record Music ▸ Record picks the file; Stop acts.
		[MenuEntry.MusicRecorder] = new[] { EntryOpens.SystemPicker, EntryOpens.Nothing },
		[MenuEntry.EnhancementPacks] = new[] { EntryOpens.PlayerLookWindow },
		[MenuEntry.LogWindow] = new[] { EntryOpens.PlayerLookWindow },
		//Play a Replay… picks the movie file.
		[MenuEntry.MoviePlay] = new[] { EntryOpens.SystemPicker },
		//Record ▸ Video ▸ Record: the video sheet; Sound ▸ Record: the save
		//picker; both Stops act.
		[MenuEntry.Record] = new[] { EntryOpens.PlayerSheet, EntryOpens.SystemPicker, EntryOpens.Nothing },
		//Connect… and Start Server… are Player-look forms; the rest acts.
		[MenuEntry.NetPlay] = new[] { EntryOpens.PlayerLookWindow, EntryOpens.Nothing },
		[MenuEntry.Settings] = new[] { EntryOpens.PlayerSheet },
		//Check for Updates and Command Line, both sheets.
		[MenuEntry.Help] = new[] { EntryOpens.PlayerSheet },
		[MenuEntry.About] = new[] { EntryOpens.PlayerSheet },
		[MenuEntry.Exit] = new[] { EntryOpens.Nothing },
	};

	//What the entry's leaves open; throws for an entry no task door shows, so
	//a new Tools ⋯ entry fails the guard until it is placed here.
	public static IReadOnlyList<EntryOpens> Of(MenuEntry entry)
	{
		if(!Table.TryGetValue(entry, out EntryOpens[]? opens)) {
			throw new ArgumentOutOfRangeException(nameof(entry), entry, "not a task-door entry: add it to DoorEntryOpensTable");
		}
		return opens;
	}

	//Every entry a task door shows, on either platform, with every console item.
	public static IReadOnlyList<MenuEntry> TaskDoorEntries()
	{
		HashSet<MenuEntry> entries = new();
		foreach(Workspace door in new[] { Workspace.Play, Workspace.Remaster, Workspace.Share }) {
			foreach(bool isMacOS in new[] { true, false }) {
				foreach(IReadOnlyList<MenuEntry> group in WorkspaceMenu.ToolsMenu(door, isMacOS, GameCapabilities.All)) {
					entries.UnionWith(group);
				}
				entries.UnionWith(WorkspaceMenu.AppMenu(door, isMacOS));
			}
		}
		return entries.OrderBy(e => e).ToArray();
	}

	public static bool OpensClassicWindow(MenuEntry entry) => Of(entry).Contains(EntryOpens.ClassicWindow);
}

//The barcode sheet's rules, as InputBarcodeWindow has them: digits only, at
//most 13; more than 8 digits is an EAN-13, otherwise an EAN-8.
public static class BarcodeEntry
{
	public const int MaxLength = 13;

	public static string Digits(string? text)
	{
		return new string((text ?? "").Where(c => c >= '0' && c <= '9').Take(MaxLength).ToArray());
	}

	public static bool CanSubmit(string? text) => Digits(text).Length > 0;

	public static uint Kind(string digits) => digits.Length > 8 ? 13u : 8u;
}
