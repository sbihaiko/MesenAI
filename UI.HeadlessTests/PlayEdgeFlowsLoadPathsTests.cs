using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#674, #676, #681 (1): every way a game opens in Play reaches W-P14's alert
//when it fails - after a cancelled BIOS sheet, from a recent game, and from an
//OS file open that arrives before the window's startup initialized the core.
public partial class PlayEdgeFlowsTests
{
	//#674: a BIOS sheet cancelled for a load that never reported its failure
	//(a recent game, #676) must not hide the next open's alert.
	[AvaloniaFact]
	public void A_cancelled_bios_sheet_does_not_hide_the_next_load_failure_alert()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow _, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);

		Task<bool> request = model.RequestBios(FirmwareType.FDS, "disksys.rom", 8192, 0, "Zelda no Densetsu");
		Dispatcher.UIThread.RunJobs();
		model.BiosSheet.Cancel();
		WaitFor(() => request.IsCompleted, "Cancel never answered the Core's request");

		string notAGame = Path.Combine(_folder, "Contra.txt");
		File.WriteAllText(notAGame, "not a game");
		LoadRomHelper.LoadFile(notAGame);
		WaitFor(() => model.RecentGames.IsLoadAlertVisible, "the load failure after a cancelled BIOS sheet never reached the home (#674)");
	}

	//#676: Continue on a recent game whose ROM was moved or deleted leaves the
	//home on screen with the alert naming the missing file.
	[AvaloniaFact]
	public void Continue_on_a_recent_game_whose_rom_is_gone_shows_the_load_failure_alert()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);

		string recent = WriteRecentGame("Gone Game", Path.Combine(_folder, "Gone Game.nes"));
		LoadRomHelper.LoadRecentGame(recent, false);
		WaitFor(() => model.RecentGames.IsLoadAlertVisible, "a recent game whose ROM is gone never reached the home's alert (#676)");

		Assert.True(model.RecentGames.Visible);
		Assert.False(EmuApi.IsRunning());
		Assert.True(window.FindNamed<Border>("PlayHomeLoadAlert").IsOnScreen());
		Assert.Equal("\u201cGone Game.nes\u201d is no longer where it was.", window.FindNamed<TextBlock>("PlayHomeLoadAlertTitle").Text);
	}

	//#681 (1): on a cold launch the OS's open-documents event can arrive before
	//MainWindow.Startup has run EmuApi.InitializeEmu. The open waits for it.
	[AvaloniaFact]
	public void A_file_opened_by_the_OS_before_startup_finishes_waits_for_the_core()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		PrepareShowPlay();
		MainWindow window = new();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		Task notStarted = window.Startup;
		int generation = model.OpenGeneration;

		string notAGame = Path.Combine(_folder, "Contra.txt");
		File.WriteAllText(notAGame, "not a game");
		App.OpenFromOs(window, notAGame);
		window.ShowUnstarted();

		bool openedBeforeStartup = false;
		while(window.Startup == notStarted || !window.Startup.IsCompleted) {
			Dispatcher.UIThread.RunJobs();
			if(model.OpenGeneration != generation && (window.Startup == notStarted || !window.Startup.IsCompleted)) {
				openedBeforeStartup = true;
			}
			Thread.Sleep(1);
		}
		Assert.False(openedBeforeStartup, "the OS file open ran before MainWindow.Startup initialized the core (#681)");
		WaitFor(() => model.RecentGames.IsLoadAlertVisible, "the OS file open never ran once the startup finished");
	}

	//A recent-game file as SaveStateManager::SaveRecentGame writes it:
	//RomInfo.txt (name, ROM path, patch path) and the state.
	private string WriteRecentGame(string name, string romPath)
	{
		string recent = Path.Combine(_folder, name + ".rgd");
		using ZipArchive zip = ZipFile.Open(recent, ZipArchiveMode.Create);
		using(StreamWriter info = new(zip.CreateEntry("RomInfo.txt").Open())) {
			info.Write(name + "\n" + romPath + "\n\n");
		}
		zip.CreateEntry("Savestate.mss");
		return recent;
	}
}
