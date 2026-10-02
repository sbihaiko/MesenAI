using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//R.2 (ADR-0205 §7): W-P4 › Save states › Shared replays. W-P4 is at its
	//seven-control limit (PlayPauseOverlay), so the list opens from the Save
	//states sheet rather than from a new overlay row. It replaces that sheet
	//(replace-not-stack), and closing it - Done or Esc - brings the overlay
	//back (rule 8). Kept in its own partial so the overlay glue stays small.
	public partial class MainWindowViewModel
	{
		private PlayerReplaysSheetViewModel? _replaysSheet;

		public PlayerReplaysSheetViewModel ReplaysSheet
		{
			get {
				if(_replaysSheet == null) {
					_replaysSheet = new PlayerReplaysSheetViewModel();
					_replaysSheet.Closed += OpenPauseOverlay;
					_replaysSheet.Watching += () => IsPlayerOverlayVisible = false;
				}
				return _replaysSheet;
			}
		}

		//Where the catalog comes from - the project's own
		//docs/community-replays.json, a plain GET with no confirmation (MEI §3
		//item 4). Headless tests swap these for fixed data.
		public Func<Task<IReadOnlyList<CommunityReplayGame>?>> CommunityReplaysSource { get; set; } = CommunityReplayCatalogFetcher.FetchAsync;
		public Func<IReadOnlyList<CommunityReplayGame>> CommunityReplaysLastKnown { get; set; } = () => CommunityReplayCatalogFetcher.LastKnown;

		//The hash a replay must carry: the movie's own GameSettings.txt SHA1 is
		//Emulator::GetHash(Sha1) of the ROM as loaded, before a patch applies.
		//The core dereferences the running console, so no game = no hash.
		public Func<string> ReplayRomSha1 { get; set; } = () => EmuApi.IsRunning() ? EmuApi.GetRomHash(HashType.Sha1) : "";
		public Action<string> ReplayOpenUrl { get; set; } = ApplicationHelper.OpenBrowser;
		public Func<CommunityReplay, Task<ReplayFetchResult>> ReplayDownload { get; set; } = CommunityReplayCatalogFetcher.DownloadAsync;
		//MesenMovie::Play power-cycles and plays; the overlay had paused.
		public Action<string> ReplayPlay { get; set; } = path => {
			RecordApi.MoviePlay(path);
			EmuApi.Resume();
		};
		public Func<ReplayWatchReason> ReplayWatchReasonSource { get; set; } = () => ReplayWatch.Reason(
			EmuApi.IsRunning(), RecordApi.MovieRecording() || RecordApi.MoviePlaying(), NetplayApi.IsConnected());

		//The Save states sheet's *Shared replays…* button.
		public void OpenReplaysSheet()
		{
			IsSaveStatesSheetVisible = false;
			IsPlayerOverlayVisible = false;
			string romSha1 = ReplayRomSha1();
			ReplaysSheet.Open(romSha1, RomInfo.ConsoleType, CommunityReplaysLastKnown(), ReplayWatchReasonSource(),
				ReplayOpenUrl, ReplayDownload, ReplayPlay);
			_ = RefreshCommunityReplaysAsync(romSha1);
		}

		//The sheet opens on the last known catalog; the fetched one replaces it
		//when it returns, if the sheet is still up for the same copy.
		private async Task RefreshCommunityReplaysAsync(string romSha1)
		{
			IReadOnlyList<CommunityReplayGame>? catalog = await CommunityReplaysSource();
			if(catalog == null) {
				return;
			}
			Dispatcher.UIThread.Post(() => {
				if(ReplaysSheet.IsVisible && ReplaysSheet.RomSha1 == romSha1) {
					ReplaysSheet.SetCatalog(catalog);
				}
			});
		}

		//Hides the sheet without raising Closed (see HideCheatsSheet).
		private void HideReplaysSheet()
		{
			if(_replaysSheet != null) {
				_replaysSheet.IsVisible = false;
			}
		}
	}
}
