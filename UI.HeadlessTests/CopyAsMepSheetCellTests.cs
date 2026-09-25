using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Debugger.Controls;
using Mesen.Debugger.Utilities;
using Mesen.Debugger.ViewModels;
using Mesen.Debugger.Windows;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//PRD Phase 12 F12.2, dispatcher steps P5-P8. ADR-0214: a fresh Opus session
//is the remaining evaluator; this test is not that session. What IS mechanical
//is the claim P8 makes about the clipboard, and ADR-0214 §3: the menu's
//identity is the visible label, not ActionType. This drives the real Tilemap
//Viewer ViewModel to check:
//
//  - the panel is null until SelectionRect is set (P6's own correction of
//    2026-09-18: the first evaluator hovered instead of clicking, and the
//    right-hand side stayed on the whole-screen Tilemap block);
//  - "Copy as MEP sheet cell" is a real, visible, enabled entry found by that
//    Name (ResourceHelper.GetEnumText), then checked to be wired to
//    ActionType.CopyToMepSheetCell — looking it up by enum first is the
//    opposite of a cold read;
//  - invoking it puts ONE line on the clipboard, and that line is a whole
//    *unplaced* cell: `count` + `tiles[]`, with no `index`, `x` or `y` of its
//    own (ADR-0216 OPEN 1(b) - the slot is `scripts/mep_add_cell.py`'s to
//    choose, and the emulator never opens the artist's sheets to guess it).
//    Inside `tiles[]`, the one entry is `tile` + `palette` on a CHR RAM game
//    and carries a third `index` field on a CHR ROM one. The two `index`
//    fields are the trap ADR-0216 names: the absent one is the cell's ordinal
//    in its sheet, the present one is the tile's absolute CHR index
//    (ADR-0172 §2). This case asserts exactly that shape, so the pre-ADR-0216
//    payload - a bare `tiles[]` entry with `tile`/`palette`/`index` at the
//    root - fails it.
//
//Set MESEN_F122_LABEL_DUMP to a path to write the visible, enabled labels
//(the dump the Fable briefing treats as the right-click menu).
//
//MepSheetCell.Format's own rules stay covered host-free in
//UI.Tests/Mep/MepSheetCellTests.cs (ADR-0123 firewall); what is asserted here
//is the crossing from the viewer's click into that text.
//
//Two environment gates, both explicit skips rather than muted failures:
//NativeCore (ADR-0150 §3, no core built = no ROM to read tiles out of) and the
//ROM library, which is never in the repo. Point MESEN_NES_ROMS at a folder
//holding the two ROMs below to run this locally.
[Collection(NativeCoreCollection.Name)]
public class CopyAsMepSheetCellTests
{
	private const string RomFolderVariable = "MESEN_NES_ROMS";
	private const string LabelDumpVariable = "MESEN_F122_LABEL_DUMP";
	private const string CopyScanVariable = "MESEN_F122_COPY_SCAN";
	private const string CopyOutVariable = "MESEN_F122_COPY_OUT";
	private const string CopyStateVariable = "MESEN_F122_COPY_STATE";
	private const string SheetCellLabel = "Copy as MEP sheet cell";
	private const string HdPackLabel = "Copy tile (HD pack format)";

	//Zelda is mapper 1 with 8 KB of CHR RAM, Super Mario Bros. is NROM with
	//8 KB of CHR ROM - the two sides of the `index` field ADR-0172 §2 defines.
	private const string ChrRamRom = "The Legend of Zelda (1987) (Nintendo).nes";
	private const string ChrRomRom = "Super Mario Bros. (1985) (Nintendo).nes";
	//#421: a title screen drawn from two nametables at once (0 and 2).
	private const string MultiNametableRom = "Zelda II - The Adventure of Link (1988) (Nintendo).nes";

	[AvaloniaFact]
	public void Copying_a_tilemap_tile_emits_the_one_line_cell_the_panel_promises()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string folder = Environment.GetEnvironmentVariable(RomFolderVariable) ?? "";
		Assert.SkipWhen(folder.Length == 0 || !Directory.Exists(folder),
			$"{RomFolderVariable} is not set to a folder of NES ROMs. The library is never in the " +
			"repo, so this test runs where the ROMs are and skips everywhere else.");

		string chrRam = Path.Combine(folder, ChrRamRom);
		string chrRom = Path.Combine(folder, ChrRomRom);
		Assert.SkipWhen(!File.Exists(chrRam) || !File.Exists(chrRom),
			$"{RomFolderVariable} does not hold both '{ChrRamRom}' (CHR RAM) and '{ChrRomRom}' (CHR ROM).");

		ClassicDesktopStyleApplicationLifetime lifetime = new();
		Assert.SkipUnless(TryInstallDesktopLifetime(lifetime),
			"this Avalonia build does not let a test install a desktop application lifetime, so " +
			"ApplicationHelper.GetMainWindow() answers null and the clipboard write is unreachable " +
			"- the copy would be a silent no-op indistinguishable from a refusal.");

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			AssertCopiedCell(lifetime, chrRam, expectIndex: false);
			AssertCopiedCell(lifetime, chrRom, expectIndex: true);
		} finally {
			//Stop unloads the ROM; Release is deliberately NOT called. Releasing
			//the global core here makes the MainWindow-backed cases in this
			//project crash the whole run with SIGSEGV - MainWindow's OnOpened
			//calls EmuApi.InitializeEmu from a background Task, and it lands on
			//a core this test had already torn down. Leaving the core
			//initialized and idle is the state every other case here expects.
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, false);
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
			TryInstallDesktopLifetime(null);
		}
	}

	//ADR-0214 §3's other half. The evaluator cannot open the flyout, and the
	//dispatcher cannot know which tile a cold read will pick, so setup drives
	//the action for every tile of the paused frame and hands over the result.
	//This is a fact about the emulator, never the answer to criterion 1: the
	//file says what a tile copies to, and nothing about which menu entry did it.
	//
	//MESEN_F122_COPY_STATE points at a .mss the dispatcher minted with
	//`headless_record ... save-state=` at the same frame the sandbox screenshot
	//shows. Without it the scan reads whatever power-on leaves after 2 emulated
	//seconds, which is the title screen - a frame the recording captured as a
	//whole-screen <background>, where no painted cell could ever show.
	//Skips unless the ROM and output variables are set, so an ordinary run - and
	//CI, which has no ROMs - is unchanged.
	[AvaloniaFact]
	public void The_dispatcher_can_read_every_tile_of_a_paused_frame_as_a_cell()
	{
		string rom = Environment.GetEnvironmentVariable(CopyScanVariable) ?? "";
		string output = Environment.GetEnvironmentVariable(CopyOutVariable) ?? "";
		string state = Environment.GetEnvironmentVariable(CopyStateVariable) ?? "";
		Assert.SkipWhen(rom.Length == 0 || output.Length == 0,
			$"set {CopyScanVariable} to a ROM and {CopyOutVariable} to a file to scan a frame.");
		Assert.SkipWhen(!File.Exists(rom), $"{rom} does not exist");
		Assert.SkipWhen(state.Length > 0 && !File.Exists(state), $"{state} does not exist");
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		ClassicDesktopStyleApplicationLifetime lifetime = new();
		Assert.SkipUnless(TryInstallDesktopLifetime(lifetime),
			"this Avalonia build does not let a test install a desktop application lifetime, so " +
			"ApplicationHelper.GetMainWindow() answers null and the clipboard write is unreachable " +
			"- the copy would be a silent no-op indistinguishable from a refusal.");

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			if(state.Length > 0) {
				//The dispatcher's own reference frame, restored rather than
				//replayed: a .mss carries the PPU memory the scan reads. One frame
				//is drawn after the load (issue #419, see
				//StepUntilTheTraceDescribesADrawnFrame), so the scan describes the
				//frame after the sandbox screenshot's, not that exact frame.
				EmuApi.Resume();
				Thread.Sleep(500);
				EmuApi.Pause();
				EmuApi.LoadStateFile(state);
				//A state carries the frame the core was on when it was written,
				//and a load can leave the console running. Without this the walk
				//below reads a screen that scrolls underneath it, and every tile
				//after the first few describes a different frame.
				EmuApi.Pause();
				Thread.Sleep(200);
				EmuApi.Pause();
				StepUntilTheTraceDescribesADrawnFrame();
			} else {
				//The same few emulated seconds the assertion above uses, so the
				//scan describes a real screen and not whatever power-on left in VRAM.
				ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, true);
				EmuApi.Resume();
				Thread.Sleep(2000);
				EmuApi.Pause();
			}
			Dispatcher.UIThread.RunJobs();

			TilemapViewerWindow window = new(CpuType.Nes);
			lifetime.MainWindow = window;
			try {
				window.Show();
				Dispatcher.UIThread.RunJobs();
				TilemapViewerViewModel model = Assert.IsType<TilemapViewerViewModel>(window.DataContext);
				model.RefreshData();
				Dispatcher.UIThread.RunJobs();
				model.SelectionRect = new Rect(0, 0, 8, 8);
				Dispatcher.UIThread.RunJobs();
				Assert.NotNull(model.PreviewPanel);

				ContextMenuAction copy = FindCopyActionByLabel(window, SheetCellLabel);
				List<string> lines = new();
				foreach((int x, int y, string text) in WalkTilemap(model, copy, window)) {
					//Tile units, not the selection's pixel origin: the tilemap
					//image the evaluator reads is labelled in columns and rows,
					//and that is what a person counts off a picture. Handing over
					//`96,64` for the tile labelled `12,8` cost both Fable runs a
					//stop on 2026-09-19 - the dispatcher's defect, not the
					//feature's.
					lines.Add($"{x / 8},{y / 8}\t{text}");
				}
				Assert.False(lines.Count == 0,
					"no tile of the paused frame produced a cell - the scan would hand the " +
					"evaluator nothing to paste");

				string? folder = Path.GetDirectoryName(Path.GetFullPath(output));
				if(!string.IsNullOrEmpty(folder)) {
					Directory.CreateDirectory(folder);
				}
				File.WriteAllLines(output, lines);
			} finally {
				lifetime.MainWindow = null;
				window.Close();
				Dispatcher.UIThread.RunJobs();
			}
		} finally {
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, false);
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
			TryInstallDesktopLifetime(null);
		}
	}

	//#421: the scan above used to walk x < 256, y < 240 - nametable $2000 only -
	//while the Tilemap Viewer shows all four nametables (512x480) and a frame is a
	//window into whichever of them the scroll points at. F14.2 measured the cost:
	//Zelda II's title draws nametable 0 rows 12-29 and nametable 2 below scanline
	//~144, and its copy table held no non-blank shape that was on screen.
	//
	//"Drawn" is the copy's own rule (ADR-0215): a cell is on the frame when
	//NesDrawnTileResolver.ScanlinesThatDrew finds a scanline whose loopy `v`
	//fetched it, which is the same inversion HdPackCopyHelper runs before it
	//answers, so the mirror tab of a mirrored nametable is not drawn and is not
	//expected. Zelda II from power-on shows nametable 2 for the whole of its first
	//six seconds (416 cells, measured), so two seconds here is not a race.
	//
	//The ROM is copied to a folder of its own first: with no pack beside it the
	//palette check answers Unchecked (#342) and every drawn cell copies, so what
	//this asserts is the walk, not which rules some pack happens to hold (the
	//pack the scan checks against is #420's, in test_f122_prepare_evaluator.py).
	[AvaloniaFact]
	public void The_scan_offers_a_line_for_every_cell_the_frame_drew_from_any_nametable()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string folder = Environment.GetEnvironmentVariable(RomFolderVariable) ?? "";
		Assert.SkipWhen(folder.Length == 0 || !Directory.Exists(folder),
			$"{RomFolderVariable} is not set to a folder of NES ROMs. The library is never in the " +
			"repo, so this test runs where the ROMs are and skips everywhere else.");
		string library = Path.Combine(folder, MultiNametableRom);
		Assert.SkipWhen(!File.Exists(library), $"{RomFolderVariable} does not hold '{MultiNametableRom}'.");

		ClassicDesktopStyleApplicationLifetime lifetime = new();
		Assert.SkipUnless(TryInstallDesktopLifetime(lifetime),
			"this Avalonia build does not let a test install a desktop application lifetime, so " +
			"ApplicationHelper.GetMainWindow() answers null and the clipboard write is unreachable.");

		string bare = Path.Combine(Path.GetTempPath(), "mesen-421-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(bare);
		string rom = Path.Combine(bare, MultiNametableRom);
		File.Copy(library, rom);

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, true);
			EmuApi.Resume();
			Thread.Sleep(2000);
			EmuApi.Pause();
			Dispatcher.UIThread.RunJobs();

			Assert.Equal(NesScanlineTraceStatus.Current, DebugApi.GetNesScanlineTrace(out UInt32[] scroll, out UInt32[] _));
			HashSet<(int Col, int Row)> drawn = DrawnCells(scroll);
			Assert.True(drawn.Any(cell => cell.Col >= 32 || cell.Row >= 30),
				$"precondition: {MultiNametableRom} at 2 s should draw cells outside nametable 0, and the " +
				$"trace names {drawn.Count} drawn cell(s), all in nametable 0");

			TilemapViewerWindow window = new(CpuType.Nes);
			lifetime.MainWindow = window;
			try {
				window.Show();
				Dispatcher.UIThread.RunJobs();
				TilemapViewerViewModel model = Assert.IsType<TilemapViewerViewModel>(window.DataContext);
				model.RefreshData();
				Dispatcher.UIThread.RunJobs();
				model.SelectionRect = new Rect(0, 0, 8, 8);
				Dispatcher.UIThread.RunJobs();
				ContextMenuAction copy = FindCopyActionByLabel(window, SheetCellLabel);

				HashSet<(int Col, int Row)> copied = WalkTilemap(model, copy, window)
					.Select(line => (line.X / 8, line.Y / 8)).ToHashSet();

				List<(int Col, int Row)> missing = drawn.Where(cell => !copied.Contains(cell))
					.OrderBy(cell => cell.Row).ThenBy(cell => cell.Col).ToList();
				Assert.True(missing.Count == 0,
					$"{missing.Count} of the {drawn.Count} cells the frame drew have no copy line " +
					$"(first: {string.Join(" ", missing.Take(8).Select(c => $"{c.Col},{c.Row}"))}) - " +
					$"the walk visited {WalkPositions(model).Count()} position(s)");
				List<(int Col, int Row)> undrawn = copied.Where(cell => !drawn.Contains(cell)).ToList();
				Assert.True(undrawn.Count == 0,
					$"{undrawn.Count} copy line(s) name a cell the frame did not draw " +
					$"(first: {string.Join(" ", undrawn.Take(8).Select(c => $"{c.Col},{c.Row}"))})");
			} finally {
				lifetime.MainWindow = null;
				window.Close();
				Dispatcher.UIThread.RunJobs();
			}
		} finally {
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, false);
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
			TryInstallDesktopLifetime(null);
			try {
				Directory.Delete(bare, true);
			} catch(IOException) {
			}
		}
	}

	//Every nametable cell a visible scanline fetched, in the Tilemap Viewer's own
	//tile coordinates: nametable n sits at column (n & 1) * 32, row (n >> 1) * 30.
	private static HashSet<(int Col, int Row)> DrawnCells(UInt32[] scroll)
	{
		HashSet<(int Col, int Row)> drawn = new();
		for(int nametable = 0; nametable < 4; nametable++) {
			for(int row = 0; row < 30; row++) {
				for(int column = 0; column < 32; column++) {
					if(NesDrawnTileResolver.ScanlinesThatDrew(scroll, nametable, row, column).Count > 0) {
						drawn.Add(((nametable & 0x01) * 32 + column, (nametable >> 1) * 30 + row));
					}
				}
			}
		}
		return drawn;
	}

	//Issue #419: a save state does not carry the per-scanline trace the copy
	//resolves keys through (ADR-0215), so right after the load the copy refuses
	//until a whole frame has been drawn - before the fix it resolved through the
	//trace left from before the load and handed F14.2 wrong-bank keys and empty
	//tables. So the scan draws the frame after the state's, one deterministic
	//frame period at a time (a state saved mid-frame needs a second), and reads
	//that frame: it is the one the trace and the screen then both describe.
	private static void StepUntilTheTraceDescribesADrawnFrame()
	{
		DebugApi.InitializeDebugger();
		for(int step = 0; step < 3 && DebugApi.GetNesScanlineTrace(out _, out _) != NesScanlineTraceStatus.Current; step++) {
			DebugApi.Step(CpuType.Nes, 1, StepType.PpuFrame);
			Thread.Sleep(300);
			EmuApi.Pause();
			Thread.Sleep(100);
		}
		Assert.Equal(NesScanlineTraceStatus.Current, DebugApi.GetNesScanlineTrace(out _, out _));
	}

	//Avalonia refuses `Application.ApplicationLifetime = ...` once the app is
	//initialized ("It's not possible to change ApplicationLifetime after
	//Application was initialized"), and Avalonia.Headless installs none at all.
	//Without one, the shipped ApplicationHelper.GetMainWindow() - the only place
	//HdPackCopyHelper can reach a clipboard from - always answers null, the copy
	//becomes a silent no-op, and the test could not tell that apart from a
	//refusal. Writing the backing field is the single reflection hack in this
	//file; it buys the assertion that matters, which is the text the *shipped*
	//code really puts on the clipboard rather than a re-implementation of it.
	private static bool TryInstallDesktopLifetime(IApplicationLifetime? lifetime)
	{
		FieldInfo? field = typeof(Application)
			.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
			.FirstOrDefault(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
		if(field == null || Application.Current == null) {
			return false;
		}
		field.SetValue(Application.Current, lifetime);
		return ReferenceEquals(Application.Current.ApplicationLifetime, lifetime);
	}

	private static void AssertCopiedCell(ClassicDesktopStyleApplicationLifetime lifetime, string rom, bool expectIndex)
	{
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		//A few emulated seconds, so the nametable holds a real screen rather
		//than whatever power-on left in VRAM.
		ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, true);
		EmuApi.Resume();
		Thread.Sleep(2000);
		EmuApi.Pause();

		TilemapViewerWindow window = new(CpuType.Nes);
		lifetime.MainWindow = window;
		try {
			window.Show();
			Dispatcher.UIThread.RunJobs();
			TilemapViewerViewModel model = Assert.IsType<TilemapViewerViewModel>(window.DataContext);
			model.RefreshData();
			Dispatcher.UIThread.RunJobs();

			//P6: hovering is not enough. Until a click fills SelectionRect,
			//UpdatePreviewPanel() keeps PreviewPanel null.
			Assert.Null(model.PreviewPanel);
			model.SelectionRect = new Rect(0, 0, 8, 8);
			Dispatcher.UIThread.RunJobs();
			Assert.NotNull(model.PreviewPanel);

			List<string> labels = VisibleEnabledLabels(window);
			MaybeWriteLabelDump(labels);
			Assert.Contains(SheetCellLabel, labels);
			Assert.Contains(HdPackLabel, labels);

			//ADR-0214 §3: find by the string a user reads, then check the wiring.
			ContextMenuAction copy = FindCopyActionByLabel(window, SheetCellLabel);
			Assert.Equal(ActionType.CopyToMepSheetCell, copy.ActionType);

			string text = CopyFirstUsableTile(model, copy, window);
			Assert.False(text.Length == 0, "no tile in the visible tilemap produced a sheet cell");

			//P8: one line, and exactly the fields the panel shows the evaluator.
			Assert.DoesNotContain("\n", text);
			Assert.DoesNotContain("\r", text);

			//JsonDocument, not JsonSerializer: this project builds with
			//reflection-based serialization disabled.
			using JsonDocument parsed = JsonDocument.Parse(text);
			Dictionary<string, JsonElement> cell = parsed.RootElement.EnumerateObject()
				.ToDictionary(property => property.Name, property => property.Value.Clone());

			//ADR-0216 OPEN 1(b): a whole cell, unplaced. `count` and `tiles[]` and
			//nothing else - an `index`, `x` or `y` at this level would be the viewer
			//claiming a slot it cannot know is free.
			Assert.Equal(new[] { "count", "tiles" }, cell.Keys.ToArray());
			Assert.Equal(1, cell["count"].GetInt32());
			JsonElement[] tiles = cell["tiles"].EnumerateArray().ToArray();
			Assert.Single(tiles);

			Dictionary<string, JsonElement> fields = tiles[0].EnumerateObject()
				.ToDictionary(property => property.Name, property => property.Value.Clone());
			string[] expected = expectIndex
				? new[] { "tile", "palette", "index" }
				: new[] { "tile", "palette" };
			Assert.Equal(expected, fields.Keys.ToArray());

			string tile = fields["tile"].GetString()!;
			string palette = fields["palette"].GetString()!;
			Assert.Equal(MepSheetCell.TileDataLength, tile.Length);
			Assert.Equal(MepSheetCell.PaletteLength, palette.Length);
			Assert.True(IsUpperHex(tile), $"tile '{tile}' is not 32 uppercase hex characters");
			Assert.True(IsUpperHex(palette), $"palette '{palette}' is not 8 uppercase hex characters");

			int index = expectIndex ? fields["index"].GetInt32() : MepSheetCell.UnknownIndex;
			if(expectIndex) {
				Assert.InRange(index, 0, 0x7FFFFFFF);
			}
			//The text is the sidecar's own form, character for character - the
			//panel's criterion 2 is that it pastes into `cells[]` unedited, and
			//`mep_add_cell.py` reads exactly this shape off the clipboard.
			Assert.Equal(MepSheetCell.FormatCell(tile, palette, index), text);
			//...and the entry inside it is still the `tiles[]` object it always was,
			//so a hand paste into an existing cell keeps working (ADR-0216's
			//"the action does not stop being a copy").
			Assert.Contains(MepSheetCell.Format(tile, palette, index), text);
		} finally {
			lifetime.MainWindow = null;
			window.Close();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//Known trap 4 in the panel script: IsEnabled checks the memory type, not
	//whether the selected tile has a key, so a click can be a silent no-op that
	//leaves the previous clipboard in place. Walking the tilemap until one tile
	//answers is what an evaluator does by eye; the assertions above then run on
	//a text this run really produced, never on a stale clipboard. Only the tiles
	//that answered are yielded, so a text read back is never a stale one.
	private static IEnumerable<(int X, int Y, string Text)> WalkTilemap(
		TilemapViewerViewModel model, ContextMenuAction copy, Window window)
	{
		IClipboard? clipboard = window.Clipboard;
		Assert.NotNull(clipboard);
		foreach((int x, int y) in WalkPositions(model)) {
			clipboard.SetTextAsync("").GetAwaiter().GetResult();
			model.SelectionRect = new Rect(x, y, 8, 8);
			Dispatcher.UIThread.RunJobs();
			Assert.NotNull(model.PreviewPanel);

			copy.OnClick();
			Dispatcher.UIThread.RunJobs();
			string text = clipboard.TryGetTextAsync().GetAwaiter().GetResult() ?? "";
			if(text.Length > 0) {
				yield return (x, y, text);
			}
		}
	}

	//#421: every tile of the picture the viewer shows - on the NES "Nametables"
	//tab that is all four nametables, 512x480 - not just nametable $2000. Which of
	//them the frame actually drew is the copy's to decide: a cell no visible
	//scanline fetched is refused (NotDrawnThisFrame) and yields no line, exactly
	//as it does for a person clicking it.
	private static IEnumerable<(int X, int Y)> WalkPositions(TilemapViewerViewModel model)
	{
		PixelSize size = model.ViewerBitmap.PixelSize;
		int stepX = Math.Max(model.GridSizeX, 1);
		int stepY = Math.Max(model.GridSizeY, 1);
		for(int y = 0; y < size.Height; y += stepY) {
			for(int x = 0; x < size.Width; x += stepX) {
				yield return (x, y);
			}
		}
	}

	private static string CopyFirstUsableTile(TilemapViewerViewModel model, ContextMenuAction copy, Window window)
	{
		foreach((int _, int _, string text) in WalkTilemap(model, copy, window)) {
			return text;
		}
		return "";
	}

	private static IEnumerable MenuActions(TilemapViewerWindow window)
	{
		PictureViewer viewer = window.FindNamed<ScrollPictureViewer>("picViewer").InnerViewer;
		IEnumerable? items = viewer.ContextMenu?.ItemsSource;
		Assert.NotNull(items);
		return items;
	}

	private static List<string> VisibleEnabledLabels(TilemapViewerWindow window)
	{
		List<string> labels = new();
		foreach(ContextMenuAction action in MenuActions(window).OfType<ContextMenuAction>()) {
			if(action is ContextMenuSeparator) {
				continue;
			}
			if(!(action.IsVisible?.Invoke() ?? true) || !(action.IsEnabled?.Invoke() ?? true)) {
				continue;
			}
			labels.Add(action.Name);
		}
		return labels;
	}

	private static ContextMenuAction FindCopyActionByLabel(TilemapViewerWindow window, string label)
	{
		return MenuActions(window).OfType<ContextMenuAction>()
			.Where(action => action is not ContextMenuSeparator)
			.Where(action => action.IsVisible?.Invoke() ?? true)
			.Single(action => action.Name == label);
	}

	private static void MaybeWriteLabelDump(List<string> labels)
	{
		string path = Environment.GetEnvironmentVariable(LabelDumpVariable) ?? "";
		if(path.Length == 0) {
			return;
		}
		string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
		if(!string.IsNullOrEmpty(folder)) {
			Directory.CreateDirectory(folder);
		}
		File.WriteAllLines(path, labels);
	}

	private static bool IsUpperHex(string text)
	{
		return text.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'));
	}
}
