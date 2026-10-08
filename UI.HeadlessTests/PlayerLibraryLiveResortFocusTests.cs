using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#1065 (ADR-0264 Decision 7), review finding 1: RestoreTiles reorders with
//Tiles.Move, and a panel generator is free to treat a Move as Remove+Add - which
//drops the container the ring is on. The tile the player is on while a query is
//active must still be the focused element after a canonical title moves THAT tile.
[Collection(NativeCoreCollection.Name)]
public class PlayerLibraryLiveResortFocusTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly string? _gameFolder = ConfigManager.Config.Preferences.GameFolder;
	private readonly bool _overrideGameFolder = ConfigManager.Config.Preferences.OverrideGameFolder;

	private readonly List<MainWindow> _windows = new();
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-1065-focus-" + Guid.NewGuid().ToString("N"));

	private const string UnknownSha1 = "0000000000000000000000000000000000000000";

	private static NoIntroNameTable TableOf(params string[] rows)
	{
		string header =
			"#mesen-no-intro-sha1-table\t1\n" +
			"#source\tfixture\n" +
			"#licence\tfixture\n" +
			"#hash\tfixture\n" +
			"#console\tnes\tFixture - NES\t2020.01.02\t" + new string('a', 64) + "\n";
		using MemoryStream output = new MemoryStream();
		using(GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
			byte[] bytes = Encoding.UTF8.GetBytes(header + string.Join("\n", rows) + "\n");
			gzip.Write(bytes, 0, bytes.Length);
		}
		return NoIntroNameTable.Load(new MemoryStream(output.ToArray()));
	}

	public PlayerLibraryLiveResortFocusTests()
	{
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			Stopwatch clock = Stopwatch.StartNew();
			while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
				Thread.Sleep(10);
			}
			Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
		}
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.GameFolder = _gameFolder ?? "";
		prefs.OverrideGameFolder = _overrideGameFolder;
		ConfigManager.Config.Save();

		try {
			Directory.Delete(_folder, true);
		} catch {
			//A case that failed before it built its tree leaves nothing to remove.
		}
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Settle(int milliseconds)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < milliseconds) {
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static string Titles(PlayerRomPickerViewModel picker) => string.Join(", ", picker.Tiles.Select(t => t.Title));

	[AvaloniaFact]
	public void The_ring_stays_on_the_tile_a_canonical_title_moves_while_a_query_is_active()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		const string ZeldaSha1 = "4444444444444444444444444444444444444444";
		string contra = Path.Combine(_folder, "Contra (U) [!].nes");
		string metroid = Path.Combine(_folder, "Metroid (USA).nes");
		TaskCompletionSource<bool> held = new();

		LibraryScanResult Scan(IReadOnlyList<string> folders, FolderLister list, Action<IReadOnlyList<LibraryEntry>> onBatch)
		{
			LibraryEntry[] entries = {
				new(contra, RomConsole.Nes, "Contra"),
				new(metroid, RomConsole.Nes, "Metroid")
			};
			onBatch(entries);
			return new LibraryScanResult(entries, 1, false);
		}

		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");

		PlayerRomPickerViewModel picker = model.RomPicker;
		picker.LibraryFolderSource = () => new[] { _folder };
		picker.LibraryScanStreamSource = Scan;
		picker.RunLibraryScanInline = true;
		picker.NoIntroTable = TableOf(ZeldaSha1 + "\tnes\tZelda II - The Adventure of Link (USA)");
		picker.RomHashSource = async (path, _, _) => {
			await held.Task;
			return Path.GetFileName(path).StartsWith("Contra", StringComparison.Ordinal) ? ZeldaSha1 : UnknownSha1;
		};

		model.RecentGames.Init(GameScreenMode.RecentGames);
		Pump();
		model.OpenRomPicker();
		Assert.True(picker.IsVisible, "the sheet never opened");

		WaitFor(() => picker.Tiles.Count == 2, $"the grid never filled ({picker.Tiles.Count} tiles)");

		//"t" matches both games, and the canonical title of the first one too.
		picker.SearchQuery = "t";
		Pump();
		Assert.Equal(2, picker.Tiles.Count);

		PlayerLibraryTile contraTile = picker.Tiles[0];
		Button button = window.FindNamed<ItemsControl>("RomPickerGrid")
			.GetVisualDescendants()
			.OfType<Button>()
			.First(b => ReferenceEquals(b.DataContext, contraTile));
		button.Focus();
		Pump();
		Assert.True(button.IsFocused, "the case never got the ring onto a tile");

		held.SetResult(true);

		//"Zelda II - ..." sorts after "Metroid": the focused tile is the one that moves.
		WaitFor(() => Titles(picker) == "Metroid, Zelda II - The Adventure of Link",
			$"the grid never took the canonical order (titles=[{Titles(picker)}])");
		Settle(400);

		Assert.Same(contraTile, picker.Tiles[1]);
		Assert.Same(contraTile, (window.FocusManager?.GetFocusedElement() as Control)?.DataContext);
	}
}
