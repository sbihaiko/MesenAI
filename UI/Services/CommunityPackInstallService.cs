using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mesen.Logic;

namespace Mesen.Services
{
	//ROM-load entry point of F6.4b-2 (ADR-0138 §4/§37/§38): the thin orchestrator that ties
	//CommunityPackCatalogFetcher (network) and CommunityPackInstallCoordinator (gates + interop)
	//together once per loaded ROM, and surfaces the outcome through the emulator's own message
	//API. Runs off the UI thread (ADR-0146: no first-run consent dialog).
	//Fail-soft by contract: an auto-install must never break a game load, so every failure path
	//degrades to a one-line message (or silence for the routine "nothing to do" cases).
	public static class CommunityPackInstallService
	{
		private const string MessageTitle = "Enhancement Packs";
		//Exclusion between the auto-install (OnGameLoaded/RunAsync) and the user's
		//Restore so the two never rewrite the same mep/ at once (host-free
		//CommunityPackInstallGate, UI.Tests). At most one holder at a time; a
		//refused caller backs off without releasing the holder's token.
		private static readonly CommunityPackInstallGate _installGate = new();

		//True while an auto-install or Restore holds the gate. Read-only; lets
		//UI.HeadlessTests/CommunityPackApplyTests wait out an install a game load
		//in an earlier test started, before it drives the gate itself.
		public static bool InstallInFlight => _installGate.IsHeld;
		//§51 per-session idempotency key: one successful-or-in-flight attempt
		//per ROM sha1 per process, decided before any network call. A failed
		//fetch/install or a thrown extract is removed so the next load retries.
		//The .mep-install.json stamp (§43) remains the cross-session gate.
		private static readonly HashSet<string> _attemptedRomSha1 = new(StringComparer.OrdinalIgnoreCase);
		//P.6: community 👍 counts from the last catalog fetch (MEI `votes`), keyed
		//by pack_id - the Player picker sorts by them (§5). Read-only for the
		//UI; local-only packs have no entry and sort by name (votes 0). Written
		//from the background install task, read on the UI thread - hence concurrent.
		private static readonly ConcurrentDictionary<string, int> _catalogVotesByPackId = new(StringComparer.OrdinalIgnoreCase);

		//ADR-0152: known-missing declarations from the last catalog fetch, keyed
		//by pack_id. Display-only, like the votes above - a `miss` changes nothing
		//in the installed tree, it only makes a reviewed gap legible in the picker.
		private static readonly ConcurrentDictionary<string, CommunityPackErrata> _catalogErrataByPackId = new(StringComparer.OrdinalIgnoreCase);

		//G.4 (W-P9): the HUD pill of an auto-install. Started(name) when a
		//catalog row matches and its artifact is not the one already installed
		//(PackInstallPill.ShowsFor); Finished(installed, silent) when the run
		//ends. Raised on the UI thread. Restore raises them too (rule 6: a job
		//is a pill, never a window).
		public static event Action<string>? InstallStarted;
		public static event Action<bool, bool>? InstallFinished;

		private static void RaiseStarted(string name) => Dispatcher.UIThread.Post(() => InstallStarted?.Invoke(name));
		private static void RaiseFinished(bool installed, bool silent) => Dispatcher.UIThread.Post(() => InstallFinished?.Invoke(installed, silent));

		public static int GetVotes(string packId)
		{
			return _catalogVotesByPackId.TryGetValue(packId, out int votes) ? votes : 0;
		}

		public static CommunityPackErrata? GetErrata(string packId)
		{
			return _catalogErrataByPackId.TryGetValue(packId, out CommunityPackErrata? errata) ? errata : null;
		}

		//ADR-0147: explicit user action - discard local edits to the installed
		//pack and re-materialize it from the catalog original (Restore). Uses the
		//install registry (outside mep/) to confirm a catalog pack is installed
		//for the loaded ROM, then re-fetches the artifact and rewrites mep/. A
		//pack that left the catalog cannot be restored (nothing to fetch from).
		//Shares the CommunityPackInstallGate with the auto-install so the two
		//never rewrite the same mep/ at once.
		public static async Task<(bool Ok, string Error)> RestoreInstalledPack()
		{
			if(!_installGate.TryEnter()) {
				return (false, "an install is already in progress");
			}
			try {
				//#657: the game this Restore is for, captured before the download -
				//the coordinator writes its folder and registry key from this, and
				//drops the Restore if another game was opened meanwhile.
				CommunityPackLoadTarget load = CommunityPackInstallCoordinator.CaptureLoad();
				if(!load.HasRom) {
					return (false, "no loaded ROM to restore a pack for");
				}
				CommunityPackInstallRecord? record = CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, load.RomSha1);
				if(record == null || string.IsNullOrWhiteSpace(record.SourceSha256)) {
					return (false, "there is no catalog-installed pack to restore for this ROM");
				}
				//The catalog fetch re-verifies an up-to-300MB artifact (SHA-256); start it
				//on the thread pool so that CPU work and its continuations stay off the UI
				//thread (this Restore is user-triggered from the Enhancement Packs window).
				CommunityPackFetchResult? fetched = await Task.Run(() => CommunityPackCatalogFetcher.FetchMatchingPackAsync(entry => RaiseStarted(entry.Name)));
				if(fetched == null) {
					RaiseFinished(false, false);
					return (false, "the pack is no longer in the catalog (nothing to restore from)");
				}
				//Restore() is synchronous file/interop work - keep it off the UI thread.
				//A throw here would skip RaiseFinished and leave the pill installing.
				(bool ok, string error) = await Task.Run(() => {
					try {
						bool restored = CommunityPackInstallCoordinator.Restore(fetched.Entry, fetched.PrimaryPackPath, load, out string restoreError);
						return (restored, restoreError);
					} catch(Exception ex) {
						return (false, ex.Message);
					}
				});
				RaiseFinished(ok, false);
				if(!ok) {
					return (false, error);
				}
				EmuApi.WriteLogEntry("[CommunityPack] RestoreInstalledPack: restored " + fetched.Entry.PackId + " to mep/");
				return (true, "");
			} finally {
				ExitGate();
			}
		}

		//Called from MainWindow.OnNotification(GameLoaded); power cycles are not new loads
		//(a pack we just installed is applied through exactly such a power cycle).
		public static void OnGameLoaded(bool isPowerCycle)
		{
			//Every early return is traced so mesen.log always shows why an
			//auto-install did or did not proceed (the flow is otherwise silent
			//by design - §41/§42 - which made #142 hard to diagnose from a log).
			EmuApi.WriteLogEntry("[CommunityPack] OnGameLoaded: isPowerCycle=" + isPowerCycle +
				" AutoInstallCommunityPacks=" + ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks);
			if(isPowerCycle || !ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks) {
				EmuApi.WriteLogEntry("[CommunityPack] skipped: isPowerCycle or AutoInstallCommunityPacks off");
				return;
			}
			if(!_installGate.TryEnterOrDefer()) {
				//#657: deferred, not dropped - the holder's ExitGate runs the
				//auto-install for the game loaded then (ADR-0146).
				EmuApi.WriteLogEntry("[CommunityPack] deferred: a previous install or Restore is still in flight");
				return;
			}
			//#657 / G.5 W-P16: the load this install belongs to (open generation,
			//SHA-1, sibling folder, ROM name), captured before the download. The
			//coordinator installs for it or drops the install if another open
			//started meanwhile; a later open also drops the pending-file post.
			//#681: RunAsync's finally releases the gate; a throw before RunAsync
			//starts releases it here, or every later load would be deferred forever.
			try {
				CommunityPackLoadTarget load = CommunityPackInstallCoordinator.CaptureLoad();
				_ = Task.Run(() => RunAsync(load, userRequested: false));
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityPack] skipped: reading the loaded game threw: " + ex);
				ExitGate();
			}
		}

		//#736: Play's community-pack offer (CommunityPackOfferRule) for the
		//loaded ROM - the catalog row matched like the auto-install matches
		//(No-Intro SHA-1, then the ADR-0145/0146 same-game fallback), read from
		//the catalog copy on disk, plus the install registry's container.
		public static CommunityPackOfferContext ReadOfferContext(string romSha1, string romName)
		{
			if(string.IsNullOrWhiteSpace(romSha1)) {
				return new CommunityPackOfferContext(null, null, InstallInFlight);
			}
			CommunityPackCatalog? catalog = CommunityPackCatalogFetcher.ReadCachedCatalog();
			CommunityPackCatalogEntry? entry = catalog == null ? null : CommunityPackCatalogMatcher.FindMatchingEntry(catalog, romSha1, romName);
			string? registryContainer = CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, romSha1)?.Container;
			return new CommunityPackOfferContext(entry == null ? null : CommunityPackOfferRule.Pick(entry), registryContainer, InstallInFlight);
		}

		//#736: W-P6's Use Community Pack when the pack is not installed. The
		//player asked for this pack, for this game: it installs even with
		//AutoInstallCommunityPacks off (the switch is left as it is) or its
		//container in DisabledPacks, with the W-P9 pill, and the game restarts
		//to load it (ApplyInstalledPack). False when an install or Restore is
		//already running.
		public static bool InstallOnRequest()
		{
			if(!_installGate.TryEnter()) {
				return false;
			}
			try {
				CommunityPackLoadTarget load = CommunityPackInstallCoordinator.CaptureLoad();
				ClearAttempt(load.RomSha1);
				EmuApi.WriteLogEntry("[CommunityPack] install requested by the player for " + load.RomSha1);
				_ = Task.Run(() => RunAsync(load, userRequested: true));
				return true;
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityPack] requested install not started: reading the loaded game threw: " + ex);
				ExitGate();
				return false;
			}
		}

		//Releases the install gate; when a ROM load's auto-install was refused
		//while it was held (#657), runs it now for the game loaded at this point.
		private static void ExitGate()
		{
			if(_installGate.Exit()) {
				EmuApi.WriteLogEntry("[CommunityPack] running the auto-install deferred while the gate was held");
				OnGameLoaded(false);
			}
		}

		private static async Task RunAsync(CommunityPackLoadTarget load, bool userRequested)
		{
			string romSha1 = "";
			try {
				//ADR-0146: no first-run consent prompt - AutoInstallCommunityPacks (checked above)
				//is the single master switch before the catalog is contacted.
				romSha1 = load.RomSha1;
				EmuApi.WriteLogEntry("[CommunityPack] romSha1=" + romSha1);
				lock(_attemptedRomSha1) {
					if(string.IsNullOrWhiteSpace(romSha1)) {
						EmuApi.WriteLogEntry("[CommunityPack] skipped: no ROM sha1");
						return;
					}
					if(!_attemptedRomSha1.Add(romSha1)) {
						EmuApi.WriteLogEntry("[CommunityPack] skipped: already attempted this session for " + romSha1);
						return;
					}
				}

				string installedSha256 = CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, romSha1)?.SourceSha256 ?? "";
				bool pillShown = false;
				CommunityPackFetchResult? fetched = await CommunityPackCatalogFetcher.FetchMatchingPackAsync(entry => {
					//#736: a requested install always says it is running.
					if(userRequested || PackInstallPill.ShowsFor(installedSha256, entry.Sha256)) {
						pillShown = true;
						RaiseStarted(entry.Name);
					}
				});
				if(fetched == null) {
					if(pillShown) {
						//W-P9: a matched pack whose download failed.
						RaiseFinished(false, false);
					}
					//No catalog row for this dump (or a failed download): allow a
					//later load this session to retry, so a catalog update is
					//picked up without restarting the process.
					ClearAttempt(romSha1);
					EmuApi.WriteLogEntry("[CommunityPack] no match/fetch result (see [CommunityPackFetch] lines above for the stage that returned null)");
					return; //no catalog, no match, host not allowed or hash mismatch - all silent (§41/§42)
				}
				EmuApi.WriteLogEntry("[CommunityPack] fetched entry: pack_id=" + fetched.Entry.PackId + " primaryPath=" + fetched.PrimaryPackPath);

				//P.6 §5: remember the matched entry's community 👍 count so the
				//Player picker sorts its competing packs by it (votes 0 when the
				//entry carries none).
				if(!string.IsNullOrWhiteSpace(fetched.Entry.PackId) && fetched.Entry.Votes is int votes) {
					_catalogVotesByPackId[fetched.Entry.PackId] = votes;
				}

				//ADR-0152: same for the entry's known-missing declarations, so the
				//picker can name the gap and who declared it.
				if(!string.IsNullOrWhiteSpace(fetched.Entry.PackId) && fetched.Entry.Errata is CommunityPackErrata errata && errata.Count > 0) {
					_catalogErrataByPackId[fetched.Entry.PackId] = errata;
				}

				CommunityPackInstallOutcome outcome = CommunityPackInstallCoordinator.Install(
					fetched.Entry, fetched.PrimaryPackPath, fetched.ResolvedDepPaths, load, userRequested);
				EmuApi.WriteLogEntry("[CommunityPack] Install() outcome: Status=" + outcome.Status +
					" ContainerName=" + outcome.ContainerName + " Message=" + outcome.Message);
				//An install that withheld dep-backed ops is incomplete, so it belongs
				//with the other retryable outcomes: without this, _attemptedRomSha1
				//latches the ROM for the rest of the process and the user can never
				//complete a user_supplied dep - the power-cycle path is closed by the
				//isPowerCycle guard in OnGameLoaded, and reloading hits this set (#156).
				//#657: a Stale install (another game opened during the download)
				//is retried on this game's next load.
				if(outcome.Status == CommunityPackInstallStatus.Failed || outcome.Status == CommunityPackInstallStatus.Stale || outcome.PendingDeps.Count > 0) {
					ClearAttempt(romSha1);
				}
				if(pillShown) {
					RaiseFinished(outcome.Status == CommunityPackInstallStatus.Installed, silent: outcome.Status == CommunityPackInstallStatus.Skipped || outcome.Status == CommunityPackInstallStatus.UpdateAvailable || outcome.Status == CommunityPackInstallStatus.Stale);
				}
				if(userRequested && outcome.Status == CommunityPackInstallStatus.Installed) {
					ChooseRequestedPack(fetched.Entry, outcome.ContainerName, load.RomSha1);
				}
				Surface(outcome, load);
			} catch(Exception ex) {
				RaiseFinished(false, false);
				ClearAttempt(romSha1);
				EmuApi.WriteLogEntry("[CommunityPack] RunAsync threw: " + ex);
				Notify("Community pack auto-install failed: " + ex.Message);
			} finally {
				ExitGate();
			}
		}

		//#736: the player chose this pack for this game - it is turned on and
		//stored as the ROM's choice (P.3), so it renders over a local pack once
		//the game restarts. Posted ahead of ApplyInstalledPack's restart.
		private static void ChooseRequestedPack(CommunityPackCatalogEntry entry, string containerName, string romSha1)
		{
			Dispatcher.UIThread.Post(() => {
				EnhancementPackConfig config = ConfigManager.Config.EnhancementPacks;
				config.SetPackEnabled(containerName, true);
				if(!string.IsNullOrWhiteSpace(entry.PackId) && !string.IsNullOrWhiteSpace(romSha1)) {
					config.SetRomPackPreference(romSha1, entry.PackId);
				}
				config.ApplyConfig();
				ConfigManager.Config.Save();
			});
		}

		private static void ClearAttempt(string romSha1)
		{
			if(string.IsNullOrWhiteSpace(romSha1)) {
				return;
			}
			lock(_attemptedRomSha1) {
				_attemptedRomSha1.Remove(romSha1);
			}
		}

		private static void Surface(CommunityPackInstallOutcome outcome, CommunityPackLoadTarget load)
		{
			switch(outcome.Status) {
				case CommunityPackInstallStatus.Installed:
					Notify("Community pack '" + outcome.ContainerName + "' installed");
					foreach(string withheld in outcome.Withheld) {
						//§6: a patch whose dependency is missing is withheld, never applied blindly.
						Notify("Patch withheld (missing dependency): " + withheld);
					}
					foreach(string notice in outcome.Notices) {
						Notify(notice);
					}
					NotifyPendingDeps(outcome.ContainerName, outcome.PendingDeps, load.RomSha1, load.OpenGeneration);
					ApplyInstalledPack(load);
					break;

				case CommunityPackInstallStatus.Failed:
					Notify("Community pack install failed: " + outcome.Message);
					break;

				case CommunityPackInstallStatus.UpdateAvailable:
					//ADR-0147: a catalog update is withheld because the installed mep/
					//was edited locally - surface it so the user chooses Restore.
					Notify(outcome.Message);
					break;

				case CommunityPackInstallStatus.Skipped:
					//Routine: up to date, disabled by user - nothing to say.
				case CommunityPackInstallStatus.Stale:
					//#657: the player opened another game during the download; the
					//coordinator logged it and touched nothing. Not that game's news.
					break;
			}
		}

		//Test seam (UI.HeadlessTests/CommunityPackApplyTests): what applies a
		//freshly installed pack to the running game.
		public static Action PowerCycleGame { get; set; } = LoadRomHelper.PowerCycle;

		//HD/MEP packs are applied at console init, not live. After a background
		//auto-install, power-cycle the same ROM so the new HdPacks/<rom>/ (or MEP
		//container) is picked up without a second manual load. OnGameLoaded skips
		//power cycles, so this does not re-fetch. #675: only while the load the
		//install was for is still the one running (the W-P16 rule, open
		//generation + SHA-1) - a game the player switched to, or reopened (e.g.
		//Continue to resume a save), is left alone.
		//Public for UI.HeadlessTests/CommunityPackApplyTests; the production
		//caller is Surface, after an Installed outcome.
		public static void ApplyInstalledPack(CommunityPackLoadTarget installedFor)
		{
			Dispatcher.UIThread.Post(() => {
				CommunityPackLoadTarget current = CommunityPackInstallCoordinator.ReadCurrentLoad();
				if(!installedFor.IsStillLoaded(current)) {
					EmuApi.WriteLogEntry("[CommunityPack] not power-cycling: the game was opened again or changed since the install started (was " +
						installedFor.RomSha1 + " open #" + installedFor.OpenGeneration + ", now " + current.RomSha1 + " open #" + current.OpenGeneration + ")");
					Notify("Community pack installed - reload the game to apply");
					return;
				}
				EmuApi.WriteLogEntry("[CommunityPack] power-cycling to apply newly installed pack");
				PowerCycleGame();
			});
		}

		//MEI-v1.md §2.3 user_supplied deps: tell the user what to drop where, with the
		//declared licence (or "not declared") so they can judge the source themselves.
		private static void NotifyPendingDeps(string packName, IReadOnlyList<CommunityPackDepPrompt> pending, string installedRomSha1, int openGeneration)
		{
			//G.5 W-P16: Player mode gets the sheet (with the pause overlay) and a
			//status sentence instead of one OSD line per file - unless another
			//open started since the install began (the post belongs to that game).
			if(ConfigManager.Config.Preferences.UiMode == UiMode.Player) {
				Dispatcher.UIThread.Post(() => {
					bool applied = MainWindowViewModel.Instance.SetPendingPackDeps(packName, pending, openGeneration, installedRomSha1, EmuApi.GetMepRomSha1());
					if(!applied) {
						EmuApi.WriteLogEntry("[CommunityPack] pending files not shown: another game was opened since the install started");
					} else if(pending.Count > 0) {
						DisplayMessageHelper.DisplayMessage(MessageTitle, ResourceHelper.GetMessage("PackDepPill", packName));
					}
				});
				return;
			}
			foreach(CommunityPackDepPrompt dep in pending) {
				string license = string.IsNullOrWhiteSpace(dep.License) ? "not declared" : dep.License;
				string hints = string.IsNullOrWhiteSpace(dep.Hints) ? dep.DepId : dep.Hints;
				//"reload the ROM", not "power cycle": OnGameLoaded returns early on
				//isPowerCycle, so the power-cycle path can never re-resolve a dep (#156).
				Notify("Missing file '" + hints + "' (licence: " + license + ") - drop it into " + dep.DropFolder + " and reload the ROM");
			}
		}

		private static void Notify(string message)
		{
			Dispatcher.UIThread.Post(() => DisplayMessageHelper.DisplayMessage(MessageTitle, message));
		}
	}
}
