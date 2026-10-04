using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Mesen.ViewModels
{
	public partial class RecentGamesViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool Visible { get; set; }
		[ObservableProperty] public partial bool NeedResume { get; private set; }
		[ObservableProperty] public partial string Title { get; private set; } = "";
		[ObservableProperty] public partial GameScreenMode Mode { get; private set; }
		[ObservableProperty] public partial List<RecentGameInfo> GameEntries { get; private set; } = new List<RecentGameInfo>();

		//G.2 (PRD Part B §13.5.2): the Play home, Player mode only - never the
		//Advanced game-selection screen, nor the Save/Load state screens that
		//reuse this same ViewModel (both gated on Mode == RecentGames below).
		//W-P1 (no recents) replaces the P.7 Welcome card; W-P2 is Continue
		//playing + Open a ROM… + the other recent games (PlayHome).
		[ObservableProperty] public partial bool ShowFirstRunHome { get; private set; }
		[ObservableProperty] public partial bool ShowRecentsHome { get; private set; }
		//The classic grid of every entry: Advanced's game selection and the
		//Save/Load state screens, exactly as before G.2.
		[ObservableProperty] public partial bool ShowPlainGrid { get; private set; }
		//ADR-0249: in Player mode the Save/Load state screens are a light sheet
		//of slot tiles (StateGrid `tiles slots`), not the classic dark grid.
		[ObservableProperty] public partial bool ShowSlotTiles { get; private set; }
		[ObservableProperty] public partial string FirstRunOrientation { get; private set; } = "";
		[ObservableProperty] public partial string ContinueTitle { get; private set; } = "";
		[ObservableProperty] public partial string ContinueSubtitle { get; private set; } = "";
		//W-P2: the Continue card's picture, the recent entry's own screenshot
		//(null keeps the placeholder art).
		[ObservableProperty, NotifyPropertyChangedFor(nameof(HasContinuePreview))] public partial Bitmap? ContinuePreview { get; private set; }
		public bool HasContinuePreview => ContinuePreview != null;
		private int _previewGeneration;
		[ObservableProperty] public partial List<RecentGameInfo> HomeGridEntries { get; private set; } = new List<RecentGameInfo>();
		[ObservableProperty] public partial bool ShowHomeGrid { get; private set; }

		public RecentGamesViewModel()
		{
			//P.4 (PRD Part B §6): in Player mode the recent-games grid is the
			//home screen and is always shown when no ROM runs - GameSelectionScreenMode
			//keeps its current meaning (ResumeState/PowerOn/Disabled) only in Advanced.
			Visible = ConfigManager.Config.Preferences.UiMode == UiMode.Player || ConfigManager.Config.Preferences.GameSelectionScreenMode != GameSelectionMode.Disabled;
		}

		public void Init(GameScreenMode mode)
		{
			if(mode == GameScreenMode.RecentGames && ConfigManager.Config.Preferences.UiMode != UiMode.Player && ConfigManager.Config.Preferences.GameSelectionScreenMode == GameSelectionMode.Disabled) {
				Visible = false;
				GameEntries = new List<RecentGameInfo>();
				SetPlayHome(false, entries: new List<RecentGameInfo>());
				return;
			} else if(mode != GameScreenMode.RecentGames && Mode == mode && Visible) {
				Visible = false;
				if(NeedResume) {
					EmuApi.Resume();
				}
				return;
			}

			if(Mode == mode && Visible && GameEntries.Count > 0) {
				//Prevent flickering when closing the config window while no game is running
				//No need to update anything if the game selection screen is already visible
				return;
			}

			Mode = mode;

			List<RecentGameInfo> entries = new();

			//#153: the Play home lives in the same DataTemplate as the recent-games
			//grid and shares its host ContentControl's IsVisible. On a genuine first
			//boot the recents list is empty and `Visible = entries.Count > 0` below
			//would collapse the whole template - taking W-P1 (formerly the Welcome
			//card) down with it, for exactly the user it exists for. The Player home
			//must stay up with zero entries; Save/Load/game-selection keep the old
			//empty->hidden behaviour.
			bool keepPlayerHomeHostVisible = false;

			if(mode == GameScreenMode.RecentGames) {
				NeedResume = false;
				Title = string.Empty;
				//W-P2: re-read the packs folder and catalog for this home's badges.
				RecentPackLookup.Invalidate();

				List<string> files = Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd").OrderByDescending((file) => new FileInfo(file).LastWriteTime).ToList();
				for(int i = 0; i < files.Count && entries.Count < 72; i++) {
					entries.Add(new RecentGameInfo() { FileName = files[i], Name = Path.GetFileNameWithoutExtension(files[i]) });
				}

				//G.2: Player-home only - Advanced's own game-selection screen
				//(GameSelectionScreenMode) reuses this same ViewModel/mode but is
				//not the Play home.
				bool isPlayerHome = ConfigManager.Config.Preferences.UiMode == UiMode.Player;
				keepPlayerHomeHostVisible = isPlayerHome;
				SetPlayHome(isPlayerHome, entries);
			} else {
				SetPlayHome(false, entries);
				if(!Visible) {
					NeedResume = Pause();
				}

				//ADR-0249: Player mode speaks the overlay's language (PlaySlotGrid).
				bool player = ConfigManager.Config.Preferences.UiMode == UiMode.Player;
				Title = ResourceHelper.GetMessage(PlaySlotGrid.TitleKey(mode == GameScreenMode.LoadState, player));

				string romName = EmuApi.GetRomInfo().GetRomName();
				for(int i = 0; i < (mode == GameScreenMode.LoadState ? 11 : 10); i++) {
					entries.Add(new RecentGameInfo() {
						FileName = Path.Combine(ConfigManager.SaveStateFolder, romName + "_" + (i + 1) + "." + FileDialogHelper.MesenSaveStateExt),
						StateIndex = i + 1,
						Name = i == 10 ? ResourceHelper.GetMessage("AutoSave") : ResourceHelper.GetMessage(PlaySlotGrid.SlotKey(player), i + 1),
						SaveMode = mode == GameScreenMode.SaveState
					});
				}
				if(mode == GameScreenMode.LoadState) {
					entries.Add(new RecentGameInfo() {
						FileName = Path.Combine(ConfigManager.RecentGamesFolder, romName + ".rgd"),
						Name = ResourceHelper.GetMessage("LastSession")
					});
				}
			}

			Visible = keepPlayerHomeHostVisible || entries.Count > 0;
			GameEntries = entries;
		}

		private void SetPlayHome(bool isPlayerHome, List<RecentGameInfo> entries)
		{
			PlayHomeKind kind = PlayHome.Classify(entries.Count);
			ShowFirstRunHome = isPlayerHome && kind == PlayHomeKind.FirstRun;
			ShowRecentsHome = isPlayerHome && kind == PlayHomeKind.WithRecents;
			ShowPlainGrid = !isPlayerHome;
			ShowSlotTiles = !isPlayerHome && Mode != GameScreenMode.RecentGames && ConfigManager.Config.Preferences.UiMode == UiMode.Player;
			HomeGridEntries = ShowRecentsHome ? PlayHome.RecentGrid(entries) : new List<RecentGameInfo>();
			ShowHomeGrid = HomeGridEntries.Count > 0;

			FirstRunOrientation = ShowFirstRunHome ? OrientationText() : "";
			if(ShowRecentsHome) {
				ContinueTitle = entries[0].Name;
				ContinueSubtitle = LastPlayedText(entries[0].FileName);
			} else {
				ContinueTitle = "";
				ContinueSubtitle = "";
			}
			LoadContinuePreview(ShowRecentsHome ? entries[0].FileName : null);
		}

		//Read off the UI thread (the recent file is a zip); a newer home wins.
		//The pack's name and version (RecentPackLookup, the same lookup as the
		//tile badges) are read in the same task and join the subtitle when known.
		private void LoadContinuePreview(string? recentFile)
		{
			int generation = ++_previewGeneration;
			ContinuePreview = null;
			if(recentFile == null) {
				return;
			}
			string recentName = Path.GetFileNameWithoutExtension(recentFile);
			string lastPlayed = ContinueSubtitle;
			RecentGameHash? hash = RecentGameHashes.Find(ConfigManager.Config.RecentFiles.GameHashes, recentName);
			bool namedHdPack = PlayHome.HasHdPack(ConfigManager.HdPackFolder, recentName);
			bool autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
			Task.Run(() => {
				RecentPackInfo pack = default;
				try {
					pack = RecentPackLookup.Lookup(recentName, hash, namedHdPack, autoInstall);
				} catch(Exception ex) {
					EmuApi.WriteLogEntry("[PlayHome] continue pack lookup failed: " + ex.Message);
				}
				if(pack.Name.Length > 0) {
					Dispatcher.UIThread.Post(() => {
						if(generation == _previewGeneration) {
							ContinueSubtitle = PlayHome.ContinueSubtitle(lastPlayed, pack.Name, pack.Version);
						}
					});
				}
			});
			Task.Run(() => {
				byte[]? png = PlayHome.ReadScreenshot(recentFile);
				if(png == null) {
					return;
				}
				Dispatcher.UIThread.Post(() => {
					if(generation != _previewGeneration) {
						return;
					}
					try {
						using MemoryStream stream = new(png);
						ContinuePreview = new Bitmap(stream);
					} catch(Exception) {
						ContinuePreview = null;
					}
				});
			});
		}

		private static string OrientationText()
		{
			EnhancementPackConfig packs = ConfigManager.Config.EnhancementPacks;
			return PlayHome.Orientation(packs.EnableAudio, packs.AutoInstallCommunityPacks) switch {
				PlayHomeOrientation.AudioAndPacks => ResourceHelper.GetMessage("PlayHomeOrientationAudioAndPacks"),
				PlayHomeOrientation.AudioOnly => ResourceHelper.GetMessage("PlayHomeOrientationAudioOnly"),
				PlayHomeOrientation.PacksOnly => ResourceHelper.GetMessage("PlayHomeOrientationPacksOnly"),
				_ => ""
			};
		}

		//W-P2's "last played today"; the pack half of the wireframe's subtitle
		//("· Contra 80s 1.2") is added by LoadContinuePreview once looked up.
		private static string LastPlayedText(string recentFile)
		{
			if(!File.Exists(recentFile)) {
				return "";
			}
			DateTime played = new FileInfo(recentFile).LastWriteTime;
			(LastPlayedKind kind, int days) = PlayHome.LastPlayed(played, DateTime.Now);
			return kind switch {
				LastPlayedKind.Today => ResourceHelper.GetMessage("PlayHomeLastPlayedToday"),
				LastPlayedKind.Yesterday => ResourceHelper.GetMessage("PlayHomeLastPlayedYesterday"),
				LastPlayedKind.DaysAgo => ResourceHelper.GetMessage("PlayHomeLastPlayedDaysAgo", days),
				_ => ResourceHelper.GetMessage("PlayHomeLastPlayedOn", played.ToShortDateString())
			};
		}

		private bool Pause()
		{
			if(!EmuApi.IsPaused()) {
				EmuApi.Pause();
				return true;
			}
			return false;
		}
	}

	public enum GameScreenMode
	{
		RecentGames,
		LoadState,
		SaveState
	}

	public class RecentGameInfo
	{
		public string FileName { get; set; } = "";
		public int StateIndex { get; set; } = -1;
		public string Name { get; set; } = "";
		public bool SaveMode { get; set; } = false;

		public bool IsEnabled()
		{
			return SaveMode || File.Exists(FileName);
		}

		public void Load()
		{
			if(StateIndex > 0) {
				Task.Run(() => {
					//Run in another thread to prevent deadlocks etc. when emulator notifications are processed UI-side
					if(SaveMode) {
						EmuApi.SaveState((uint)StateIndex);
					} else {
						EmuApi.LoadState((uint)StateIndex);
					}
					EmuApi.Resume();
				});
			} else {
				//#783: no Resume() here. It ran before the load even started, and a
				//game that opens now starts running in the core, which is where the
				//pause flag lives - the reload branch above resumes because its load
				//is a state restore onto the game already on screen.
				LoadRomHelper.LoadRecentGame(FileName, false);
			}
		}
	}
}
