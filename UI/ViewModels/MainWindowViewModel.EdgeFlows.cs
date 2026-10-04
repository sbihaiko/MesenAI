using System;
using System.Collections.Generic;
using System.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P13–W-P16): the Play edge flows -
	//the BIOS sheet, the load-failure alert, the unknown-controller setup and
	//the pending pack file - and the shell status sentence they leave behind.
	//Player mode only; Advanced keeps the classic dialogs and OSD lines. Kept
	//in its own partial so MainWindowViewModel.cs does not grow.
	public partial class MainWindowViewModel
	{
		public PlayBiosSheetViewModel BiosSheet { get; } = new();
		public PlayPackDepSheetViewModel PackDepSheet { get; } = new();
		public PlayControllerSetupViewModel ControllerSetup { get; } = new();

		//ADR-0255 slice 5: repairs a pad that reconnects at a different device
		//index, moving its keys with it. Driven by the window's Play poll
		//(PlayEdgeFlowsWiring), so it is only looked at while Player mode has the
		//Play door up - home or game screen, because the keys have to be right by
		//the time a game reads them. Inert on macOS and Windows XInput, where every
		//pad is unidentified (see the class).
		public ControllerReconnectRepair ControllerReconnect { get; } = new();

		public bool IsPlayerMode => Config.Preferences.UiMode == UiMode.Player;

		//Any open (a recent card, Open a ROM…, a drop): the W-P14 alert and a
		//W-P13 "needs the BIOS" sentence belong to the previous attempt.
		public void OnOpenStarted()
		{
			Interlocked.Increment(ref _openGeneration);
			//#658: a BIOS sheet still up belongs to the previous attempt; its
			//load would hold the Core's load locks and queue this one behind it.
			BiosSheet.Dismiss();
			//#674: a cancel belongs to the open it ended. This open starts clean,
			//so its own failure shows the W-P14 alert even when the earlier load
			//never reported (a recent game, Remaster's workspace).
			BiosSheet.ClearCancelled();
			//An archive's game list still up belongs to the previous open.
			SelectRomSheet.Cancel();
			RecentGames.OnOpenStarted();
			Shell.SetPlayNotice("");
			PackDepSheet.Clear();
		}

		//W-P13: the Core's MissingFirmware, in Player mode. True when a file
		//was installed (the Core then retries the load).
		public async System.Threading.Tasks.Task<bool> RequestBios(FirmwareType type, string fileName, uint size, uint altSize, string gameName)
		{
			bool installed = await BiosSheet.Request(type, fileName, size, altSize, gameName);
			if(!installed && BiosSheet.LastRequestCancelled && BiosSheet.CancelNotice.Length > 0) {
				Shell.SetPlayNotice(BiosSheet.CancelNotice);
			}
			return installed;
		}

		//Bumped by every open: an install's late post for the previous game is
		//dropped (PlayPackDepPrompt.BelongsToCurrentLoad). Read off the UI thread.
		private int _openGeneration;
		public int OpenGeneration => Volatile.Read(ref _openGeneration);

		//The install's post: applied only when it still belongs to the loaded
		//game. True when it was applied.
		public bool SetPendingPackDeps(string packName, IReadOnlyList<CommunityPackDepPrompt> pending, int installOpenGeneration, string installRomSha1, string currentRomSha1)
		{
			if(!PlayPackDepPrompt.BelongsToCurrentLoad(installOpenGeneration, OpenGeneration, installRomSha1, currentRomSha1)) {
				return false;
			}
			SetPendingPackDeps(packName, pending);
			return true;
		}

		//W-P16: CommunityPackInstallService found a file only the user can add.
		//The status line carries it; the sheet opens with the pause overlay.
		public void SetPendingPackDeps(string packName, IReadOnlyList<CommunityPackDepPrompt> pending)
		{
			if(pending.Count == 0) {
				PackDepSheet.Clear();
				Shell.SetPlayNotice("");
				return;
			}
			PackDepSheet.SetPending(packName, pending);
			Shell.SetPlayNotice(ResourceHelper.GetMessage("PackDepWaitingStatus"));
		}

		//Called by OpenPauseOverlay: the first overlay after a notice opens
		//the W-P16 sheet on top of it, once per notice.
		private bool OpenPackDepSheetWithOverlay()
		{
			if(!PackDepSheet.Notice.ShouldOpenWithOverlay()) {
				return false;
			}
			IsPlayerOverlayVisible = false;
			PackDepSheet.Open();
			return PackDepSheet.IsVisible;
		}

		//The game is gone (Quit game, power off): its pack's pending file goes
		//with it. A W-P13 sentence stays - it belongs to the load that failed.
		private void ClearPackDepWithoutGame()
		{
			if(PackDepSheet.Notice.HasPending || PackDepSheet.IsVisible) {
				PackDepSheet.Clear();
				Shell.SetPlayNotice("");
			}
		}

		//The file landed in the drop folder: the status sentence is gone.
		public void OnPackDepFileAdded() => Shell.SetPlayNotice("");

		//#732: the way out of a forced pack patch - play this ROM without it
		//(this session; the setting is not changed) and power-cycle. Swapped by
		//headless tests that only check the banner.
		public Action ReloadWithoutForcedPatch { get; set; } = () => {
			if(EmuApi.SuppressForcedPackPatch()) {
				LoadRomHelper.PowerCycle();
			}
		};

		//#732: after every load, the patch the core forced on this game through
		//ApplyPatchOnHashMismatch (EmuApi.GetForcedPackPatch; empty for none).
		public void OnForcedPackPatch(string forcedPatch)
		{
			switch(PlayForcedPatch.AfterLoad(Config.Preferences.UiMode, forcedPatch, Interruption.Kind)) {
				case ForcedPatchBanner.Show:
					Interruption.Ask(InterruptionKind.ForcedPatch, PlayForcedPatch.PatchName(forcedPatch), 0, false, () => ReloadWithoutForcedPatch());
					break;
				case ForcedPatchBanner.Withdraw:
					Interruption.Keep();
					break;
			}
		}

		//The game is gone or replaced: its forced-patch banner goes with it.
		private void WithdrawForcedPatchWithoutGame()
		{
			if(PlayForcedPatch.WithdrawsWithoutGame(Interruption.Kind)) {
				Interruption.Keep();
			}
		}

		//Esc on a sheet that does not belong to the overlay: the BIOS sheet
		//cancels (the game does not load), the controller setup cancels and
		//resumes. True when Esc was taken.
		private bool HandleEdgeFlowEsc()
		{
			if(HandleInWindowSheetEsc()) {
				return true;
			}
			if(BiosSheet.IsVisible) {
				BiosSheet.Cancel();
				return true;
			}
			if(ControllerSetup.IsVisible) {
				ControllerSetup.Cancel();
				return true;
			}
			return false;
		}
	}
}
