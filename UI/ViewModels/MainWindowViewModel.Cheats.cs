using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//P.10 (ADR-0245 §1, PRD Part B §13 W-P4/W-P11): the pause overlay's Cheats
	//row and the sheet it opens. Kept in its own partial file so the overlay
	//glue stays small in MainWindowViewModel.cs. The sheet replaces the overlay
	//(same replace-not-stack shape as the Enhancements panel), and closing it -
	//Done or Esc - brings the overlay back (rule 8).
	public partial class MainWindowViewModel
	{
		private PlayerCheatsSheetViewModel? _cheatsSheet;

		public PlayerCheatsSheetViewModel CheatsSheet
		{
			get {
				if(_cheatsSheet == null) {
					_cheatsSheet = new PlayerCheatsSheetViewModel();
					//G.2: back to W-P4 with its row values refreshed.
					_cheatsSheet.Closed += OpenPauseOverlay;
					//P.11 (#922): the search by intent runs scripts/cheat_intent.py
					//under the python3 and tools Remaster already located.
					IByokKeyStore keyStore = ByokKeyStores.ForCurrentOS();
					_cheatsSheet.ConfigureIntentSearch(keyStore, async () => {
						await Remaster.EnsureFeasibilityMeasured();
						RemasterFeasibility? found = Remaster.Feasibility;
						return found is { CanRunJobs: true }
							? new CheatIntentScriptRunner(keyStore, found.PythonExecutable, found.PythonPrefixArgs, found.ToolsFolder)
							: null;
					});
				}
				return _cheatsSheet;
			}
		}

		//The row's value: "none", "N on", or "off" (CheatSheet.OverlaySummary).
		[ObservableProperty] public partial string CheatsSummary { get; private set; } = "none";

		public void RefreshCheatsSummary()
		{
			CheatsSummary = CheatSheet.OverlaySummary(CheatSheet.CountOn(PlayerCheatsStore.LoadStored()), Config.Cheats.DisableAllCheats);
		}

		//R.4 (ADR-0248 §5): where the sheet's community catalog comes from - the
		//project's own docs/community-cheats.json, a plain GET with no
		//confirmation (ADR-0146). Headless tests swap it for a fixed catalog.
		public Func<Task<IReadOnlyList<CommunityCheatGame>?>> CommunityCheatsSource { get; set; } = CommunityCheatCatalogFetcher.FetchAsync;
		public Func<IReadOnlyList<CommunityCheatGame>> CommunityCheatsLastKnown { get; set; } = () => CommunityCheatCatalogFetcher.LastKnown;

		//The cheat hash of the running copy. The core dereferences the running
		//console for the hash, so with no game loaded there is no copy to match
		//(the sheet says "not in the list"). Headless tests swap it.
		public Func<string> CheatRomSha1 { get; set; } = () => EmuApi.IsRunning() ? EmuApi.GetRomHash(HashType.Sha1Cheat) : "";

		//Play is unrestricted (ADR-0245 §3), so recordingArt is false in Play,
		//also over the passive automatic recording; it is true in Remaster and
		//while a Remaster recording runs, which switching to Play does not stop
		//(§13.6): Game Genie rows are disabled with their reason and Add a Code
		//refuses them.
		public void OpenCheatsSheet()
		{
			IsPlayerOverlayVisible = false;
			ConsoleType console = RomInfo.ConsoleType;
			string cheatSha1 = CheatRomSha1();
			CheatsSheet.Open(
				console,
				cheatSha1,
				PlayerCheatsStore.LoadDatabase(console),
				PlayerCheatsStore.LoadStored(),
				//Only a Remaster context locks: the passive automatic recording does not
				//(it stops when a code changes the game; ADR-0245 amendment 2026-10-03).
				recordingArt: CheatRecordingRule.IsRecordingArtContext(Shell.Active == Workspace.Remaster, Remaster.IsRecording),
				Config.Cheats.DisableAllCheats,
				PlayerCheatsStore.SaveAndApply,
				gameName: EmuApi.IsRunning() ? EmuApi.GetRomInfo().GetRomName() : "",
				//#662: the file name tells a GBC game from a GB one (an archive's inner file).
				romFile: ((ResourcePath)RomInfo.RomPath).FileName,
				openUrl: ApplicationHelper.OpenBrowser,
				community: CommunityCheatsLastKnown(),
				//#639: CheatCodes saves to the running game's file; the sheet
				//checks it is still the copy it opened for.
				runningCheatSha1: CheatRomSha1
			);
			_ = RefreshCommunityCheatsAsync(cheatSha1, CheatsSheet.BeginCommunityLoading());
		}

		//The sheet opens on the last known catalog; the fetched one replaces it
		//when it returns, if the sheet is still up for the same copy. Whatever
		//the fetch answers - nothing included - ends the sheet's wait.
		private async Task RefreshCommunityCheatsAsync(string cheatSha1, int loading)
		{
			IReadOnlyList<CommunityCheatGame>? catalog;
			try {
				catalog = await CommunityCheatsSource();
			} catch(Exception) {
				catalog = null;
			}
			Dispatcher.UIThread.Post(() => {
				if(catalog != null && CheatsSheet.IsVisible && CheatsSheet.CheatSha1 == cheatSha1) {
					CheatsSheet.SetCommunityCatalog(catalog);
				}
				CheatsSheet.FinishCommunityLoading(loading);
			});
		}

		//Hides the sheet without raising Closed: the caller decides what shows
		//next (the Esc router opens the overlay once, #641; a game change or
		//leaving Player shows nothing, #639/#642).
		private void HideCheatsSheet()
		{
			if(_cheatsSheet != null) {
				_cheatsSheet.IsVisible = false;
			}
		}
	}
}
