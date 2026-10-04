using System.IO;
using Avalonia.Headless.XUnit;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//A game opened while the emulator is paused starts running: the pause flag
//belongs to the game that was on screen, and a parked freshly loaded game draws
//exactly one frame and then nothing, with no overlay up to explain it (the
//Player overlay is an Esc menu, not a reaction to the flag).
//
//The Core half of the contract is pinned at the Core boundary, with no UI and no
//dylib resolution in the way: scripts/test_core_pause_carry_over.py asserts that
//an open runs and keeps running past ten frames, and that a reload keeps the
//pause. These cases cover the three UI entry points that reach it - the raw
//EmuApi.LoadRom the file-open path uses, the Play home's Continue card, and
//LoadRomHelper.LoadFile - and read the flag last, because no UI observable
//separates a game that is about to park from one that is not (IsGamePaused is
//still false at that moment either way; it turns true only when the parked
//thread posts GamePaused).
public partial class PlayEdgeFlowsTests
{
	[AvaloniaFact]
	public void Opening_a_game_while_the_emulator_is_paused_starts_it_running()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow _, MainWindowViewModel model) = ShowPlay();
		RunSyntheticGame(model);

		//What the player leaves behind: the game parked on the pause flag.
		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the emulator never reported as paused");

		//Open a game while that flag is still set.
		string rom = Path.Combine(_folder, "synthetic-nrom-b.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");

		WaitFor(() => model.RomInfo.GetRomName() == "synthetic-nrom-b", "the new game never started");
		Assert.False(EmuApi.IsPaused(), "the new game inherited the previous game's pause flag");
	}

	//The same thing down the card's own path (RecentGameInfo.Load), which is
	//what the Play home's Continue runs.
	[AvaloniaFact]
	public void Continuing_a_recent_game_starts_it_running()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow _, MainWindowViewModel model) = ShowPlay();
		RunSyntheticGame(model);

		//Power off writes the .rgd the home's card opens; do it paused, the way
		//a player who alt-tabs away or parks on the overlay leaves the game.
		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the emulator never reported as paused");
		EmuApi.Stop();
		WaitFor(() => !EmuApi.IsRunning(), "the game never stopped");

		string[] recents = Directory.GetFiles(Mesen.Config.ConfigManager.RecentGamesFolder, "*.rgd");
		Assert.True(recents.Length > 0, "the power off wrote no recent game");

		model.RecentGames.Init(GameScreenMode.RecentGames);
		model.RecentGames.GameEntries[0].Load();

		WaitFor(() => EmuApi.IsRunning(), "the recent game never started");
		Assert.False(EmuApi.IsPaused(), "Continue left the emulator paused with no overlay");
	}

	//File > Open ROM, and the Finder's Open With: LoadRomHelper.LoadRom resumes
	//nothing, so this is the same carry-over down the other open path.
	[AvaloniaFact]
	public void Opening_a_rom_from_a_file_path_starts_it_running()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow _, MainWindowViewModel model) = ShowPlay();
		RunSyntheticGame(model);

		EmuApi.Pause();
		WaitFor(() => EmuApi.IsPaused(), "the emulator never reported as paused");

		string rom = Path.Combine(_folder, "synthetic-nrom-c.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		LoadRomHelper.LoadFile(rom);

		//IsRunning() is _console != nullptr and a paused game keeps its console, so
		//waiting on it alone is satisfied by the game already on screen and would
		//read the flag before the load (a Task.Run) had even started. Wait for the
		//open itself, then read the flag.
		WaitFor(() => model.RomInfo.GetRomName() == "synthetic-nrom-c", "the opened game never started");
		Assert.False(EmuApi.IsPaused(), "opening a ROM from a file path inherited the previous pause flag");
	}
}
