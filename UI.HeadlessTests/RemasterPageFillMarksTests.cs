using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0219 (G.7 follow-up): the crossing into XAML of a page thumbnail's marks.
//Which cells are marked, and where, is pinned host-free in
//UI.Tests/Remaster/RemasterPageMarksTests; this checks that a page's fill cells
//really reach the screen, dimmed and at the pixels the rule gave them, that an
//empty cell looks like neither and a seen cell carries nothing. No core is
//loaded.
[Collection(NativeCoreCollection.Name)]
public class RemasterPageFillMarksTests : IDisposable
{
	private static readonly byte[] Red = { 255, 0, 0, 255 };

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

	private readonly List<string> _folders = new();

	private string TempFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-911-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		_folders.Add(folder);
		return folder;
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

	private sealed class FakeLauncher : IJobProcessLauncher
	{
		public sealed class Job : IJobProcess
		{
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };

			public void Kill() => OnExit(-9);
		}

		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			return new Job { OnLine = onLine, OnExit = onExit };
		}
	}

	private RemasterFeasibility Ready()
	{
		string tools = TempFolder();
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ImportScript), "");
		File.WriteAllText(Path.Combine(tools, RemasterHandOff.ComposeScript), "");
		return new RemasterFeasibility(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, tools);
	}

	private (Window Window, RemasterWorkspaceViewModel Model) Show(string project)
	{
		RemasterWorkspaceViewModel model = new(new RemasterConfig(), _ => Ready(), new FakeLauncher(), hasHeadlessRecorder: false);
		string root = Path.GetDirectoryName(project)!;
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(root, "Contra (USA).nes"), project, Path.Combine(root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		model.TilesSettled.Wait(10000);
		Window window = new() { Content = new RemasterWorkspaceView { DataContext = model }, Width = 1100, Height = 900 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static string Write(string root, string relative, string text)
	{
		string path = Path.Combine(root, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, text);
		return path;
	}

	//A page laid out as `artist_chr_kit.py` writes it: 16 cells a row at 8 px,
	//its sidecar saying which cells came from the ROM (`fill`), which are
	//`empty`, and which a recording saw (`evidence`).
	private string KitProject()
	{
		string project = Path.Combine(TempFolder(), "Contra (USA)");
		Write(project, "auto/rec-001/textures/hires.txt", "<ver>107\n");
		string png = Path.Combine(project, "kit/pages/chr/Chr_0.png");
		Directory.CreateDirectory(Path.GetDirectoryName(png)!);
		File.WriteAllBytes(png, Png(128, 128, _ => Red));
		List<string> cells = new();
		for(int slot = 0; slot < 256; slot++) {
			string state = slot is 0 or 1 or 200 ? "fill" : slot == 255 ? "empty" : "evidence";
			cells.Add($"{{\"slot\": {slot}, \"index\": {slot}, \"x\": {slot % 16 * 8}, \"y\": {slot / 16 * 8}, \"state\": \"{state}\"}}");
		}
		Write(project, "kit/pages/chr/Chr_0.json",
			"{\"version\": 1, \"kind\": \"chr\", \"gridUnit\": 8, \"scale\": 1, \"columns\": 16, \"rows\": 16, \"cells\": [" + string.Join(",", cells) + "]}");
		Write(project, "kit/pages/kit.json", "{\"parts\": [{\"part\": \"chr\", \"files\": [" +
			"{\"path\": \"chr/Chr_0.png\", \"title\": \"Chr_0 — CHR bank 1\", \"unit\": \"page\", \"cells\": 256, \"evidence\": 252, \"fill\": 3, \"empty\": 1, \"seen\": false}]}]}");
		return project;
	}

	private static IReadOnlyList<ContentPresenter> Marks(Window window)
	{
		return window.FindNamed<ItemsControl>("RemasterThumbMarks").FindAll<ContentPresenter>();
	}

	[AvaloniaFact]
	public void A_pages_fill_cells_are_dimmed_on_its_thumbnail_and_empty_ones_outlined()
	{
		(Window window, RemasterWorkspaceViewModel model) = Show(KitProject());
		ToggleButton pages = window.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>()
			.Single(b => (b.Content as string)!.StartsWith("Pattern pages"));
		pages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		model.TilesSettled.Wait(10000);
		Dispatcher.UIThread.RunJobs();

		//The thumbnail's canvas is the picture's own aspect at 64 px, so the
		//marks' pixels are the page's own halved - the rule's arithmetic.
		Canvas canvas = window.FindNamed<Canvas>("RemasterThumbCanvas");
		Assert.Equal(64d, canvas.Width);
		Assert.Equal(64d, canvas.Height);

		IReadOnlyList<ContentPresenter> marks = Marks(window);
		//One mark per cell the sidecar did not see in play: three fills and the
		//one empty - the 252 `evidence` cells carry nothing at all.
		Assert.Equal(4, marks.Count);

		string[] drawn = marks.Select(MarkClassOf).ToArray();
		Assert.Equal(new[] { "thumb-fill", "thumb-fill", "thumb-fill", "thumb-empty" }, drawn);

		//A page cell 8 px wide is 4 px in a 64 px thumbnail of a 128 px page:
		//slots 0 and 1 (fills) sit at the top-left, slot 200 (a fill) at
		//x = 64, y = 96 and slot 255 (the empty) at the bottom-right corner.
		(int Left, int Top)[] places = marks.Select(m => ((int)Canvas.GetLeft(m), (int)Canvas.GetTop(m))).ToArray();
		Assert.Equal(new[] { (0, 0), (4, 0), (32, 48), (60, 60) }, places);

		//Both rectangles of a mark are in the template; exactly one draws.
		foreach(ContentPresenter mark in marks) {
			Assert.Single(mark.GetVisualDescendants().OfType<Rectangle>(), r => r.IsOnScreen());
		}
	}

	[AvaloniaFact]
	public void A_page_whose_sidecar_is_missing_leaves_its_thumbnail_alone()
	{
		string project = KitProject();
		File.Delete(Path.Combine(project, "kit/pages/chr/Chr_0.json"));
		(Window window, RemasterWorkspaceViewModel model) = Show(project);
		ToggleButton pages = window.FindNamed<ItemsControl>("RemasterTileCategories").FindAll<ToggleButton>()
			.Single(b => (b.Content as string)!.StartsWith("Pattern pages"));
		pages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		model.TilesSettled.Wait(10000);
		Dispatcher.UIThread.RunJobs();

		//The kit fragment still says "3 ROM fill": a count is no provenance, so
		//the thumbnail keeps every cell as the picture draws it.
		Assert.Empty(Marks(window));
	}

	//The class the mark's visible rectangle carries - the art the eye sees.
	private static string MarkClassOf(ContentPresenter mark)
	{
		Rectangle drawn = mark.GetVisualDescendants().OfType<Rectangle>().Single(r => r.IsOnScreen());
		return drawn.Classes.Contains("thumb-fill") ? "thumb-fill" : "thumb-empty";
	}

	//8-bit RGBA, filter 0 (as UI.Tests/Remaster/KitFixture writes; this project
	//cannot reference that one).
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
