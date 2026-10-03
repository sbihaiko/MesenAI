using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.7 (PRD Part B §13.5.3 W-R1 zone ②, W-R5-W-R7): the crossing into XAML of
//the tile browser, the provenance popover, the finished-pack import sheet and
//the composer hand-off. The rules (tile order, counts, the paint probe, the
//import argv and refusal parsing, compose readiness) are pinned host-free in
//UI.Tests/Remaster; this checks what reaches the screen. No core is loaded.
[Collection(NativeCoreCollection.Name)]
public class RemasterTileBrowserTests : IDisposable
{
	private static readonly byte[] Red = { 255, 0, 0, 255 };
	private static readonly byte[] Blue = { 0, 0, 255, 255 };
	private readonly List<string> _folders = new();

	public void Dispose()
	{
		foreach(string folder in _folders) {
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}
	}

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-g7-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
	}

	private sealed class FakeLauncher : IJobProcessLauncher
	{
		public sealed class Job : IJobProcess
		{
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };

			public void Kill() => OnExit(-9);
		}

		public Job? Last;
		public IReadOnlyList<string> Argv = Array.Empty<string>();

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			Argv = argv;
			Last = new Job { OnLine = onLine, OnExit = onExit };
			return Last;
		}
	}

	private static void WaitFor(Func<bool> condition, string failure, int timeoutMs = 30000)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > timeoutMs) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(Button button)
	{
		Assert.True(button.IsEffectivelyEnabled, $"{button.Name} is disabled");
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	//A tools folder that holds the two scripts G.7 runs, so the gate passes.
	private RemasterFeasibility Ready()
	{
		string tools = TempFolder();
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ImportScript), "");
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ComposeScript), "");
		return new RemasterFeasibility(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, tools);
	}

	private (Window Window, RemasterWorkspaceViewModel Model, FakeLauncher Launcher, List<string> Opened) Show(RemasterFeasibility feasibility, string project, bool gameLoaded)
	{
		FakeLauncher launcher = new();
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => feasibility, launcher, hasHeadlessRecorder: false);
		List<string> opened = new();
		model.OpenFile = path => {
			opened.Add(path);
			return true;
		};
		string root = Path.GetDirectoryName(project)!;
		model.UpdateGame(gameLoaded, ConsoleType.Nes, "Contra (USA)", gameLoaded ? Path.Combine(root, "Contra (USA).nes") : "", gameLoaded ? project : "", Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		model.TilesSettled.Wait(10000);
		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1100, Height = 900 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model, launcher, opened);
	}

	//A recorded project with one figure (and its composed view), one scenery
	//object and one pattern page, laid out as `mep_project.py kit` writes it.
	private string KitProject()
	{
		string project = Path.Combine(TempFolder(), "Contra (USA)");
		Write(project, "auto/rec-001/textures/hires.txt", "<ver>107\n");
		WritePair(project, "kit/rec-001/sheets/usr000.png", 4, 2, 4);
		WritePair(project, "kit/rec-001/figures/usr000-figure.png", 4, 2, 4);
		WritePair(project, "kit/rec-001/sheets/usr005.png", 2, 2, 4);
		WritePair(project, "kit/pages/chr/Chr_0.png", 2, 2, 1);
		Write(project, "kit/rec-001/kit.json", "{\"parts\": [" +
			"{\"part\": \"sprites\", \"files\": [{\"path\": \"sheets/usr000.png\", \"title\": \"run — a 6-phase loop\", \"unit\": \"grid\", \"cells\": 6, \"seen\": true, \"playsColumns\": [1,2,3,4,5,6], \"figure\": \"figures/usr000-figure.png\"}]}," +
			"{\"part\": \"background\", \"files\": [{\"path\": \"sheets/usr005.png\", \"title\": \"obj000 (10 cells)\", \"unit\": \"object\", \"cells\": 10, \"seen\": true}]}]}");
		Write(project, "kit/pages/kit.json", "{\"parts\": [{\"part\": \"chr\", \"files\": [" +
			"{\"path\": \"chr/Chr_0.png\", \"title\": \"Chr_0 — CHR bank 1\", \"unit\": \"page\", \"cells\": 64, \"evidence\": 50, \"fill\": 12, \"empty\": 2, \"seen\": false}]}]}");
		return project;
	}

	[AvaloniaFact]
	public void Zone_two_shows_the_kits_tiles_by_category_and_a_click_opens_the_figures_png()
	{
		string project = KitProject();
		//The artist painted the figure's composed view (ADR-0225).
		File.WriteAllBytes(Path.Combine(project, "kit/rec-001/figures/usr000-figure.png"), Png(16, 8, i => i == 0 ? Blue : Red));
		(Window window, RemasterWorkspaceViewModel model, _, List<string> opened) = Show(Ready(), project, gameLoaded: true);

		Assert.True(window.FindNamed<RemasterTileBrowserView>("RemasterTileBrowser").IsOnScreen());
		Assert.Equal(new[] { "Figures (1)", "Scenery (1)", "Pattern pages (1)" },
			window.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>().Select(b => b.Content as string).ToArray());
		Assert.Equal("Click a tile to open it in your paint program. Save it as the same PNG and come back.", window.FindNamed<TextBlock>("RemasterPaintText").Text);

		Button tile = Assert.Single(window.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>(), b => b.Classes.Contains("tile"));
		//This host is Advanced's (no `player` scope): the glyph badge stays.
		string[] texts = tile.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();
		Assert.Equal(new[] { "run", "6 phases", "✎" }, texts);
		Click(tile);
		Assert.Equal(new[] { Path.Combine(project, "kit", "rec-001", "figures", "usr000-figure.png") }, opened.ToArray());
	}

	[AvaloniaFact]
	public void The_popover_says_where_a_pages_pixels_came_from_and_that_paint_cannot_be_told()
	{
		string project = KitProject();
		(Window window, RemasterWorkspaceViewModel model, _, List<string> opened) = Show(Ready(), project, gameLoaded: true);
		ToggleButton pages = window.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>().Single(b => (b.Content as string)!.StartsWith("Pattern pages"));
		pages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		model.TilesSettled.Wait(10000);
		Dispatcher.UIThread.RunJobs();

		Button tile = Assert.Single(window.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>(), b => b.Classes.Contains("tile"));
		//Advanced keeps the ⚠ glyph badge; Player's drawn warning stays hidden.
		Assert.Equal("⚠", tile.FindAll<TextBlock>().Last(t => t.IsOnScreen()).Text);
		Assert.False(tile.FindAll<PathIcon>().Single(p => p.Classes.Contains("warning")).IsOnScreen());
		Assert.Contains("12 of 64 cells not seen in the game", ToolTip.GetTip(tile) as string);

		Button details = window.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().Single(b => b.Classes.Contains("details"));
		//What a click on ▸ does (Button.OnClick opens its Flyout).
		details.Flyout!.ShowAt(details);
		Dispatcher.UIThread.RunJobs();
		StackPanel popover = Assert.IsType<StackPanel>(((Flyout)details.Flyout!).Content);
		WaitFor(() => popover.FindAll<TextBlock>().Any(t => t.Classes.Contains("line-classic")), "the popover never showed its lines");
		Assert.Equal("\"Chr_0\" · 64 cells · from every recording", popover.FindAll<TextBlock>().Single(t => t.Classes.Contains("header-line")).Text);
		string[] lines = popover.FindAll<TextBlock>().Where(t => t.Classes.Contains("line-classic") && t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();
		Assert.Equal(4, lines.Length);
		Assert.StartsWith("✔ 50 of 64 cells seen", lines[0]);
		Assert.StartsWith("⚠ 12 of 64 cells not seen", lines[1]);
		Assert.StartsWith("? MesenAI cannot tell whether this was painted", lines[3]);
		Click(popover.FindAll<Button>().Single(b => b.Classes.Contains("open")));
		Assert.Equal(new[] { Path.Combine(project, "kit", "pages", "chr", "Chr_0.png") }, opened.ToArray());
	}

	[AvaloniaFact]
	public void A_project_that_paints_a_patched_game_carries_the_banner_across_zone_two()
	{
		string project = KitProject();
		Write(project, "auto/rec-001/textures/hires.txt", "<ver>107\n<patch>fix.ips,0123456789ABCDEF0123456789ABCDEF01234567\n");
		(Window window, _, _, _) = Show(Ready(), project, gameLoaded: true);
		Assert.True(window.FindNamed<Border>("RemasterPatchedBanner").IsOnScreen());
	}

	//W-R6: a finished pack picked from W-R0 asks once; a refusal lists its
	//sentence with Show line and no Make editable.
	[AvaloniaFact]
	public void A_finished_pack_asks_to_be_made_editable_and_a_refusal_cites_its_line()
	{
		string root = TempFolder();
		string pack = Path.Combine(root, "Contra80s");
		string hires = Write(pack, "hires.txt", "<ver>106\n<foo>bar\n");
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, _) = Show(Ready(), Path.Combine(root, "none"), gameLoaded: false);
		Assert.True(window.FindNamed<StackPanel>("RemasterNoProject").IsOnScreen());

		model.OpenProjectFolder(pack);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<RemasterImportSheet>("RemasterImportSheet").IsOnScreen());
		Assert.False(window.FindNamed<TextBlock>("RemasterImportPatchedWarning").IsOnScreen());
		Assert.False(window.FindNamed<TextBlock>("RemasterNotice").IsOnScreen());

		Click(window.FindNamed<Button>("RemasterMakeEditableButton"));
		Assert.Equal(new[] { "import", pack, "--out", Path.Combine(root, "Contra80s (editable)") }, launcher.Argv.Skip(3).ToArray());
		Assert.True(window.FindNamed<TextBlock>("RemasterImportRunningText").IsOnScreen());

		launcher.Last!.OnLine("error: " + hires + ":2: unknown tag <foo>; refusing it rather than dropping it", true);
		launcher.Last.OnExit(2);
		WaitFor(() => model.IsImportRefused, "the refusal never showed");

		Assert.False(window.FindNamed<Button>("RemasterMakeEditableButton").IsOnScreen());
		ItemsControl list = window.FindNamed<ItemsControl>("RemasterImportRefusalList");
		Assert.Equal("unknown tag <foo>; refusing it rather than dropping it", list.FindAll<TextBlock>().Single(t => t.Classes.Contains("refusal")).Text);
		TextBlock cited = list.FindAll<TextBlock>().Single(t => t.Classes.Contains("cited"));
		Assert.False(cited.IsOnScreen());
		ToggleButton showLine = list.FindNamed<ToggleButton>("ShowLine");
		showLine.IsChecked = true;
		Dispatcher.UIThread.RunJobs();
		Assert.True(cited.IsOnScreen());
		Assert.Equal("<foo>bar", cited.Text);

		Click(window.FindNamed<Button>("RemasterImportCloseButton"));
		Assert.False(window.FindNamed<RemasterImportSheet>("RemasterImportSheet").IsOnScreen());
	}

	[AvaloniaFact]
	public void A_successful_import_opens_the_new_project_and_esc_cancels_the_question()
	{
		string root = TempFolder();
		string pack = Path.Combine(root, "Contra80s");
		Write(pack, "textures/hires.txt", "<ver>106\n");
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, _) = Show(Ready(), Path.Combine(root, "none"), gameLoaded: false);

		model.OpenProjectFolder(pack);
		Dispatcher.UIThread.RunJobs();
		RemasterImportSheet sheet = window.FindNamed<RemasterImportSheet>("RemasterImportSheet");
		sheet.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
		Dispatcher.UIThread.RunJobs();
		Assert.False(sheet.IsOnScreen());

		model.OpenProjectFolder(pack);
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("RemasterMakeEditableButton"));
		string dest = Path.Combine(root, "Contra80s (editable)");
		Write(dest, "auto/textures/hires.txt", "<ver>106\n");
		Write(dest, "IMPORT.md", "# Imported\n");
		Directory.CreateDirectory(Path.Combine(dest, "textures", "sheets"));
		File.WriteAllBytes(Path.Combine(dest, "textures", "sheets", "Sprites.png"), Png(4, 4, _ => Red));
		launcher.Last!.OnExit(0);
		WaitFor(() => model.IsProject, "the imported project never opened");

		Assert.False(sheet.IsOnScreen());
		Assert.Equal("Contra80s (editable)", window.FindNamed<TextBlock>("RemasterProjectName").Text);
		Assert.Equal(new[] { "From the pack (1)" },
			window.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>().Select(b => b.Content as string).ToArray());
	}

	//W-R7: disabled with the hint until adjacency.json exists; then the
	//composer starts as its own process on the newest textured recording.
	[AvaloniaFact]
	public void Compose_a_scene_waits_for_layout_data_then_launches_the_composer_on_the_recording()
	{
		string project = KitProject();
		(Window window, RemasterWorkspaceViewModel model, FakeLauncher launcher, _) = Show(Ready(), project, gameLoaded: true);
		MenuItem compose = window.FindNamed<Button>("RemasterProjectMenu").Flyout is MenuFlyout menu
			? menu.Items.OfType<MenuItem>().Single(m => m.Name == "RemasterComposeMenuItem") : throw new XunitException("no project menu");
		compose.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<RemasterComposeSheet>("RemasterComposeSheet").IsOnScreen());
		Assert.Equal("Record again to get the layout data", window.FindNamed<TextBlock>("RemasterComposeHint").Text);
		Assert.False(window.FindNamed<Button>("RemasterOpenComposerButton").IsEffectivelyEnabled);
		Click(window.FindNamed<Button>("RemasterComposeCancelButton"));
		Assert.False(window.FindNamed<RemasterComposeSheet>("RemasterComposeSheet").IsOnScreen());

		Write(project, "auto/rec-001/textures/sheets/adjacency.json", "{}");
		compose.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		Assert.StartsWith("This project has the layout data", window.FindNamed<TextBlock>("RemasterComposeHint").Text);
		Click(window.FindNamed<Button>("RemasterOpenComposerButton"));

		Assert.Equal(new[] { RemasterHandOff.ComposeScript, Path.Combine(project, "auto", "rec-001"), "--rom" },
			new[] { Path.GetFileName(launcher.Argv[2]), launcher.Argv[3], launcher.Argv[4] });
		Assert.False(window.FindNamed<RemasterComposeSheet>("RemasterComposeSheet").IsOnScreen());

		//A composer that dies says so, in plain words, where notices go.
		launcher.Last!.OnLine("Traceback: tkinter missing", true);
		launcher.Last.OnExit(1);
		WaitFor(() => model.NoticeText.Length > 0, "the composer's failure never showed");
		Assert.Equal("The scene composer closed with an error: Traceback: tkinter missing", window.FindNamed<TextBlock>("RemasterNotice").Text);
	}

	private static string Write(string root, string relative, string text)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, text);
		return path;
	}

	private static void WritePair(string root, string relative, int width, int height, int scale)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, Png(width * scale, height * scale, _ => Red));
		File.WriteAllBytes(path.Substring(0, path.Length - 4) + ".orig.png", Png(width, height, _ => Red));
	}

	//8-bit RGBA, filter 0 (as UI.Tests/Remaster/KitFixture writes; this
	//project cannot reference that one).
	private static byte[] Png(int width, int height, Func<int, byte[]> pixel)
	{
		using MemoryStream raw = new();
		for(int y = 0; y < height; y++) {
			raw.WriteByte(0);
			for(int x = 0; x < width; x++) {
				raw.Write(pixel(y * width + x));
			}
		}
		using MemoryStream z = new();
		using(ZLibStream deflate = new(z, CompressionLevel.Optimal, true)) {
			deflate.Write(raw.ToArray());
		}
		byte[] ihdr = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
		ihdr[8] = 8;
		ihdr[9] = 6;
		using MemoryStream png = new();
		png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", z.ToArray());
		Chunk(png, "IEND", Array.Empty<byte>());
		return png.ToArray();
	}

	private static void Chunk(Stream s, string tag, byte[] body)
	{
		byte[] len = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
		s.Write(len);
		byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
		s.Write(tagBytes);
		s.Write(body);
		uint c = 0xFFFFFFFF;
		foreach(byte b in tagBytes.Concat(body)) {
			c ^= b;
			for(int k = 0; k < 8; k++) {
				c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			}
		}
		byte[] crc = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(crc, c ^ 0xFFFFFFFF);
		s.Write(crc);
	}
}
