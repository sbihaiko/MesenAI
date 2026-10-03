using Avalonia.Controls;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Utilities
{
	public static class LoadRomHelper
	{
		public static async void LoadRom(ResourcePath romPath, ResourcePath? patchPath = null)
		{
			if(FolderHelper.IsArchiveFile(romPath)) {
				ResourcePath? selectedRom = await SelectRomWindow.Show(romPath);
				if(selectedRom == null) {
					return;
				}
				romPath = selectedRom.Value;
			}

			if(patchPath == null && ConfigManager.Config.Preferences.AutoLoadPatches) {
				string[] extensions = new string[3] { ".ips", ".ups", ".bps" };
				foreach(string ext in extensions) {
					string file = Path.Combine(romPath.Folder, Path.GetFileNameWithoutExtension(romPath.FileName)) + ext;
					if(File.Exists(file)) {
						patchPath = file;
						break;
					}
				}
			}

			InternalLoadRom(romPath, patchPath);
		}

		//G.5 (W-P13): the game a BIOS sheet names when it is cancelled.
		public static string RequestedGameName { get; private set; } = "";

		//G.5 (PRD Part B §13.5.2 W-P14): in Player mode's Play workspace the home
		//stays while a file opens (rule 5) and a failure is an inline alert on
		//it, so the home is not hidden to make room for the on-screen error message.
		private static bool KeepsHomeDuringLoad => PlayLoadFailure.KeepsHomeDuringLoad(
			ConfigManager.Config.Preferences.UiMode == UiMode.Player, MainWindowViewModel.Instance.IsPlayWorkspace);

		//Decided once, on the UI thread, for the whole load.
		private static bool BeginLoad(string gameName)
		{
			RequestedGameName = gameName;
			MainWindowViewModel.Instance.OnOpenStarted();
			bool keepsHome = KeepsHomeDuringLoad;
			if(!keepsHome) {
				//Temporarily hide selection screen to allow displaying error messages
				MainWindowViewModel.Instance.RecentGames.Visible = false;
			}
			return keepsHome;
		}

		private static void InternalLoadRom(ResourcePath romPath, ResourcePath? patchPath)
		{
			//G.6 (W-X3): a running recording asks before another game opens.
			if(!MainWindowViewModel.Instance.ConfirmOpen(romPath.FileName, () => InternalLoadRom(romPath, patchPath))) {
				return;
			}
			bool keepsHome = BeginLoad(Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(romPath.InnerFile) ? romPath.FileName : romPath.InnerFile));
			int openGeneration = MainWindowViewModel.Instance.OpenGeneration;

			Task.Run(() => {
				//Run in another thread to prevent deadlocks etc. when emulator notifications are processed UI-side
				if(EmuApi.LoadRom(romPath, patchPath)) {
					ConfigManager.Config.RecentFiles.AddRecentFile(romPath, patchPath);
					ConfigManager.Config.Save();
				} else if(keepsHome) {
					ReportLoadFailure(romPath, openGeneration);
					return;
				}
				ShowSelectionOnScreenAfterError();
			});
		}

		private static void ReportLoadFailure(ResourcePath romPath, int openGeneration)
		{
			bool isArchive = FolderHelper.IsArchiveFile(romPath.Path);
			string shownName = isArchive && !string.IsNullOrEmpty(romPath.InnerFile) ? Path.GetFileName(romPath.InnerFile) : Path.GetFileName(romPath.Path);
			LoadFailureCause cause = PlayLoadFailure.Classify(shownName, FolderHelper.IsRomFile(romPath.Path), isArchive, !string.IsNullOrEmpty(romPath.InnerFile));
			ReportLoadFailure(cause, shownName, openGeneration);
		}

		//W-P14: one sentence per cause. A load the user stopped by cancelling the
		//BIOS sheet is not a broken file, and a failure while another game keeps
		//running stays today's on-screen message (the home is not on screen).
		//#674: an open that another open has replaced reports nothing.
		private static void ReportLoadFailure(LoadFailureCause cause, string shownName, int openGeneration)
		{
			Dispatcher.UIThread.Post(() => {
				MainWindowViewModel model = MainWindowViewModel.Instance;
				if(!PlayLoadFailure.IsCurrentOpen(openGeneration, model.OpenGeneration)) {
					return;
				}
				bool cancelled = model.BiosSheet.ConsumeCancelled();
				if(EmuApi.IsRunning() || !PlayLoadFailure.ShowsAlert(cancelled)) {
					return;
				}
				model.RecentGames.Visible = true;
				model.RecentGames.ShowLoadFailure(cause, shownName);
			});
		}

		public static void LoadRecentGame(string filename, bool forceLoadState)
		{
			if(!MainWindowViewModel.Instance.ConfirmOpen(filename, () => LoadRecentGame(filename, forceLoadState))) {
				return;
			}
			bool keepsHome = BeginLoad(Path.GetFileNameWithoutExtension(filename));
			int openGeneration = MainWindowViewModel.Instance.OpenGeneration;

			Task.Run(() => {
				//Run in another thread to prevent deadlocks etc. when emulator notifications are processed UI-side
				bool recentFileExists = File.Exists(filename);
				if(recentFileExists) {
					EmuApi.LoadRecentGame(filename, !forceLoadState && ConfigManager.Config.Preferences.GameSelectionScreenMode == GameSelectionMode.PowerOn);
				}
				//#676: the core's LoadRecentGame answers nothing - no game running
				//now means the recent game did not open (W-P14, like any open).
				if(keepsHome && !EmuApi.IsRunning()) {
					ReportRecentGameFailure(filename, recentFileExists, openGeneration);
					return;
				}
				ShowSelectionOnScreenAfterError();
			});
		}

		private static void ReportRecentGameFailure(string recentFile, bool recentFileExists, int openGeneration)
		{
			RecentGameRom? rom = recentFileExists ? ReadRecentGameRom(recentFile) : null;
			bool romFileExists = rom != null && File.Exists(rom.Path);
			(LoadFailureCause cause, string shownName) = PlayRecentGameFailure.Classify(recentFile, recentFileExists, rom,
				romFileExists, rom != null && FolderHelper.IsRomFile(rom.Path), rom != null && FolderHelper.IsArchiveFile(rom.Path));
			ReportLoadFailure(cause, shownName, openGeneration);
		}

		//The ROM a .rgd names (RomInfo.txt), or null when it cannot be read.
		private static RecentGameRom? ReadRecentGameRom(string recentFile)
		{
			try {
				using ZipArchive zip = ZipFile.OpenRead(recentFile);
				ZipArchiveEntry? entry = zip.GetEntry("RomInfo.txt");
				if(entry == null) {
					return null;
				}
				using StreamReader reader = new(entry.Open());
				return PlayRecentGameFailure.ParseRomInfo(reader.ReadToEnd());
			} catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) {
				return null;
			}
		}

		private static void ShowSelectionOnScreenAfterError()
		{
			if(ConfigManager.Config.Preferences.GameSelectionScreenMode != GameSelectionMode.Disabled) {
				Thread.Sleep(3100);
				if(!EmuApi.IsRunning()) {
					//No game was loaded, show game selection screen again after ~3 seconds
					//This allows error messages to be visible to the user
					Dispatcher.UIThread.Post(() => {
						MainWindowViewModel.Instance.RecentGames.Visible = true;
					});
				}
			}
		}

		public static async void LoadPatchFile(string patchFile)
		{
			string? patchFolder = Path.GetDirectoryName(patchFile);
			if(patchFolder == null) {
				return;
			}

			List<string> romsInFolder = new List<string>();
			foreach(string filepath in Directory.EnumerateFiles(patchFolder)) {
				if(FolderHelper.IsRomFile(filepath)) {
					romsInFolder.Add(filepath);
				}
			}

			if(romsInFolder.Count == 1) {
				//There is a single rom in the same folder as the IPS/BPS patch, use it automatically
				LoadRom(romsInFolder[0], patchFile);
			} else {
				Window? wnd = ApplicationHelper.GetMainWindow();
				if(!EmuApi.IsRunning()) {
					//Prompt the user for a rom to load
					if(await MesenMsgBox.Show(wnd, "SelectRomIps", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) {
						string? filename = await FileDialogHelper.OpenFile(null, wnd, FileDialogHelper.RomExt);
						if(filename != null) {
							LoadRom(filename, patchFile);
						}
					}
				} else if(await MesenMsgBox.Show(wnd, "PatchAndReset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) {
					//Confirm that the user wants to patch the current rom and reset
					LoadRom(EmuApi.GetRomInfo().RomPath, patchFile);
				}
			}
		}

		private static bool IsPatchFile(string filename)
		{
			using(FileStream? stream = FileHelper.OpenRead(filename)) {
				if(stream != null) {
					byte[] header = new byte[5];
					stream.ReadExactly(header, 0, 5);
					if(header[0] == 'P' && header[1] == 'A' && header[2] == 'T' && header[3] == 'C' && header[4] == 'H') {
						return true;
					} else if((header[0] == 'U' || header[0] == 'B') && header[1] == 'P' && header[2] == 'S' && header[3] == '1') {
						return true;
					}
				}
			}
			return false;
		}

		public static void LoadFile(string filename)
		{
			if(File.Exists(filename)) {
				string ext = Path.GetExtension(filename).ToLowerInvariant();
				if(IsPatchFile(filename)) {
					LoadPatchFile(filename);
				} else if(ext == "." + FileDialogHelper.MesenSaveStateExt) {
					EmuApi.LoadStateFile(filename);
				} else if(EmuApi.IsRunning() && (ext == "." + FileDialogHelper.MesenMovieExt || ext == "." + FileDialogHelper.BizHawkMovieExt || ext == "." + FileDialogHelper.GbaHawkMovieExt)) {
					RecordApi.MoviePlay(filename);
				} else {
					LoadRom(filename);
				}
			} else {
				DisplayMessageHelper.DisplayMessage("Error", ResourceHelper.GetMessage("FileNotFound", filename));
			}
		}

		private static int _reloadRequestCounter = 0;
		public static void ResetReloadCounter()
		{
			//Reload/etc. operation is done, allow other calls
			Interlocked.Exchange(ref _reloadRequestCounter, 0);
		}

		private static void RunReloadShortcut(EmulatorShortcut shortcut)
		{
			//Block power cycle/power off/reload rom operations until the previous operation is done
			//This helps prevent a lot of edge cases that could happen in the UI when e.g spamming reload rom
			if(Interlocked.Increment(ref _reloadRequestCounter) == 1) {
				Task.Run(() => EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = shortcut }));
			}
		}

		//ADR-0244 (P.9): a pack change - Textures/Audio/Border, or a pack picked -
		//that keeps the player's place wherever PackChangePolicy allows it, and
		//says on the HUD what happened. `restart` is the pre-ADR-0244 path
		//(ReloadRom, or PowerCycle for the picker), taken for everything the
		//policy keeps on a restart and when the core refuses after all.
		private static readonly object _packChangeLock = new();
		//What ApplyPackChange would do right now: W-P7 names its button from it.
		public static PackChangePlan PlanPackChange(ConsoleType console)
		{
			bool movieActive = RecordApi.MoviePlaying() || RecordApi.MovieRecording();
			bool netplayActive = NetplayApi.IsConnected() || NetplayApi.IsServerRunning();
			return PackChangePolicy.Plan(console, movieActive, netplayActive);
		}

		//Returns the in-place swap's background work (completed for a restart),
		//so a headless test can wait for it before its dispatcher goes away.
		public static Task ApplyPackChange(ConsoleType console, Action restart)
		{
			PackChangePlan plan = PlanPackChange(console);
			if(plan.Route == PackChangeRoute.Restart) {
				if(plan.NoticeKey != null) {
					EmuApi.DisplayMessage("MEP", plan.NoticeKey);
				}
				restart();
				return Task.CompletedTask;
			}

			//#655: the load this change is for - a fallback restart posted after
			//another game opened is dropped (PackChangePolicy.RestartsLoadedGame).
			int openGeneration = MainWindowViewModel.Instance.OpenGeneration;
			string romSha1 = EmuApi.GetMepRomSha1();
			return Task.Run(() => {
				//One swap at a time. Each reloads with the switches as they are
				//when it runs, so a second toggle flipped during the first one is
				//applied by its own swap and never lost.
				PackChangeOutcome outcome;
				lock(_packChangeLock) {
					outcome = PackChangePolicy.Outcome((InPlaceReloadResult)EmuApi.ReloadRomKeepingState());
				}
				if(outcome.NoticeKey != null) {
					EmuApi.DisplayMessage("MEP", outcome.NoticeKey);
				}
				if(outcome.RestartNeeded) {
					Dispatcher.UIThread.Post(() => {
						if(PackChangePolicy.RestartsLoadedGame(outcome, openGeneration, MainWindowViewModel.Instance.OpenGeneration, romSha1, EmuApi.GetMepRomSha1())) {
							restart();
						} else {
							EmuApi.WriteLogEntry("[MEP] Pack change refused after the game changed; the loaded game is not restarted");
						}
					});
				}
			});
		}

		public static void Reset() { Task.Run(() => EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = EmulatorShortcut.ExecReset })); }
		public static void PowerCycle() { RunReloadShortcut(EmulatorShortcut.ExecPowerCycle); }
		public static void PowerOff()
		{
			//#658: a load waiting on the BIOS sheet holds the Core's locks, so
			//the power off would wait behind the sheet; the request is dropped.
			MainWindowViewModel.Instance.BiosSheet.Dismiss();
			RunReloadShortcut(EmulatorShortcut.ExecPowerOff);
		}
		public static void ReloadRom() { RunReloadShortcut(EmulatorShortcut.ExecReloadRom); }
	}
}
