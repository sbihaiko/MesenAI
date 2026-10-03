using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//R.2 (ADR-0205 §7): W-P4 › Save states › Shared replays. The catalog parse,
//the exact-hash match and the Watch rules are pinned host-free in
//UI.Tests/Share; this checks the crossing into XAML: the Save states sheet's
//button opens the list for the loaded copy (most-👍-first, a cheats badge),
//another copy lists nothing, a regenerated catalog drops a closed issue, the
//vote count opens the issue, Watch asks once then downloads and plays, a
//download that does not verify never plays, and Esc closes back to the
//overlay (rule 8).
//
//No test reaches the network or the core's movie player: the catalog, the
//ROM hash, the download and the play call are injected on the window's model.
[Collection(NativeCoreCollection.Name)]
public class PlayerReplaysSheetTests
{
	private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string OtherSha1 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

	private static CommunityReplay Replay(int issue, int votes, string author, string subtitle, int cheats = 0)
	{
		CommunityReplayCheat[] list = Enumerable.Range(0, cheats).Select(i => new CommunityReplayCheat("NesCustom", "0032:0" + i)).ToArray();
		return new CommunityReplay(issue, "https://github.com/user-attachments/files/" + issue + "/run.mmo", new string('c', 64), 4096, "nes", "Contra (USA)", author, subtitle, 3600, list, votes);
	}

	private static CommunityReplayGame[] Catalog(params CommunityReplay[] rows) => new[] { new CommunityReplayGame(ContraSha1, "Contra (USA)", rows) };

	private static readonly CommunityReplayGame[] Full = Catalog(
		Replay(301, 2, "alice", "stage skip", cheats: 2),
		Replay(302, 9, "bob", "no death"));

	private sealed class Harness
	{
		public MainWindow Window = null!;
		public MainWindowViewModel Model = null!;
		public List<string> Opened = new();
		public List<int> Downloaded = new();
		public List<string> Played = new();
		public ReplayFetchFailure Failure = ReplayFetchFailure.None;
		//When set, the download stays pending until the test completes it.
		public TaskCompletionSource<ReplayFetchResult>? Pending;
		public string RomSha1 = "";
	}

	private static Harness ShowPlayer(string romSha1, IReadOnlyList<CommunityReplayGame> catalog, IReadOnlyList<CommunityReplayGame>? fetched = null)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		Harness h = new();
		h.Window = new MainWindow();
		h.Window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		h.Model = Assert.IsType<MainWindowViewModel>(h.Window.DataContext);
		h.Model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		h.Model.CommunityReplaysLastKnown = () => catalog;
		h.Model.CommunityReplaysSource = () => Task.FromResult(fetched);
		h.RomSha1 = romSha1;
		h.Model.ReplayRomSha1 = () => h.RomSha1;
		h.Model.ReplayWatchReasonSource = () => ReplayWatchReason.None;
		h.Model.ReplayOpenUrl = h.Opened.Add;
		h.Model.ReplayDownload = row => {
			h.Downloaded.Add(row.Issue);
			if(h.Pending != null) {
				return h.Pending.Task;
			}
			return Task.FromResult(h.Failure == ReplayFetchFailure.None ? new ReplayFetchResult("/cache/" + row.Issue + ".mmo", ReplayFetchFailure.None) : new ReplayFetchResult(null, h.Failure));
		};
		h.Model.ReplayPlay = h.Played.Add;
		return h;
	}

	private static void OpenFromSaveStates(Harness h)
	{
		h.Model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		h.Model.OpenSaveStatesSheet();
		Dispatcher.UIThread.RunJobs();
		Click(h.Window.FindNamed<Button>("SaveStatesReplaysButton"));
	}

	private static void Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static List<Button> Named(Control root, string name) => root.FindAll<Button>().Where(b => b.Name == name && b.IsOnScreen()).ToList();

	private static string[] VisibleTexts(Control root) => root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();

	[AvaloniaFact]
	public void Save_states_opens_the_replays_of_this_copy_most_voted_first_with_a_cheats_badge()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		OpenFromSaveStates(h);

		Assert.True(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.False(h.Window.FindNamed<Border>("PlayerSaveStatesSheet").IsOnScreen());
		Assert.False(h.Window.IsPauseCardActive());
		string[] texts = VisibleTexts(h.Window);
		int bob = Array.IndexOf(texts, "bob — no death");
		int alice = Array.IndexOf(texts, "alice — stage skip");
		Assert.True(bob >= 0 && alice > bob, string.Join(" | ", texts));
		Assert.Contains("uses 2 cheats", texts);
		Assert.Equal(ReplayWatch.StatusLine(2), h.Window.FindNamed<TextBlock>("ReplaysStatusLine").Text);
		Assert.Equal(new[] { "👍 9 ↗", "👍 2 ↗" }, Named(h.Window, "ReplaysVotesButton").Select(b => b.Content as string));
	}

	[AvaloniaFact]
	public void Another_copy_lists_nothing()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(OtherSha1, Full);
		OpenFromSaveStates(h);

		Assert.Empty(Named(h.Window, "ReplaysWatchButton"));
		Assert.Equal(ReplayWatch.StatusLine(0), h.Window.FindNamed<TextBlock>("ReplaysStatusLine").Text);
	}

	[AvaloniaFact]
	public void A_closed_issue_is_gone_once_the_fetched_catalog_drops_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full, fetched: Catalog(Replay(301, 2, "alice", "stage skip")));
		OpenFromSaveStates(h);
		Dispatcher.UIThread.RunJobs();

		Assert.DoesNotContain("bob — no death", VisibleTexts(h.Window));
		Assert.Single(Named(h.Window, "ReplaysWatchButton"));
	}

	[AvaloniaFact]
	public void The_vote_count_opens_the_issue()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		OpenFromSaveStates(h);

		Click(Named(h.Window, "ReplaysVotesButton")[0]);
		Assert.Equal(new[] { "https://github.com/sbihaiko/MesenAI/issues/302" }, h.Opened);
	}

	[AvaloniaFact]
	public void Watch_asks_once_then_downloads_plays_and_takes_the_pause_surfaces_down()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		OpenFromSaveStates(h);

		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Assert.Empty(h.Downloaded);
		Assert.Equal(ReplayWatch.ArmedText, h.Window.FindNamed<TextBlock>("ReplaysNoticeLine").Text);
		Assert.Equal("Restart & watch", Named(h.Window, "ReplaysWatchButton")[0].Content as string);

		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Assert.Equal(new[] { 302 }, h.Downloaded);
		Assert.Equal(new[] { "/cache/302.mmo" }, h.Played);
		Assert.False(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.False(h.Window.IsPauseCardActive());
	}

	[AvaloniaFact]
	public void A_download_that_does_not_verify_never_plays()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		h.Failure = ReplayFetchFailure.Mismatch;
		OpenFromSaveStates(h);

		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Assert.Equal(new[] { 302 }, h.Downloaded);
		Assert.Empty(h.Played);
		Assert.True(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.Equal(ReplayWatch.FailureText(ReplayFetchFailure.Mismatch), h.Window.FindNamed<TextBlock>("ReplaysNoticeLine").Text);
	}

	[AvaloniaFact]
	public void Watch_is_off_with_its_reason_while_a_movie_runs()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		h.Model.ReplayWatchReasonSource = () => ReplayWatchReason.MovieBusy;
		OpenFromSaveStates(h);

		Assert.All(Named(h.Window, "ReplaysWatchButton"), b => Assert.False(b.IsEnabled));
		Assert.Equal(ReplayWatch.ReasonText(ReplayWatchReason.MovieBusy), h.Window.FindNamed<TextBlock>("ReplaysNoticeLine").Text);
	}

	[AvaloniaFact]
	public void Esc_closes_the_sheet_back_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		OpenFromSaveStates(h);

		h.Model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.True(h.Window.FindNamed<Border>("PlayerOverlay").IsOnScreen());

		OpenFromSaveStatesAgain(h);
		Click(h.Window.FindNamed<Button>("ReplaysDoneButton"));
		Assert.True(h.Window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//#640: Restart & watch, then the download is still running.
	private static Harness WatchPending(string romSha1)
	{
		Harness h = ShowPlayer(romSha1, Full);
		h.Pending = new TaskCompletionSource<ReplayFetchResult>();
		OpenFromSaveStates(h);
		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Click(Named(h.Window, "ReplaysWatchButton")[0]);
		Assert.Equal(new[] { 302 }, h.Downloaded);
		Assert.Empty(h.Played);
		return h;
	}

	private static void FinishDownload(Harness h)
	{
		h.Pending!.SetResult(new ReplayFetchResult("/cache/302.mmo", ReplayFetchFailure.None));
		Dispatcher.UIThread.RunJobs();
	}

	[AvaloniaFact]
	public void Esc_during_the_download_cancels_the_watch()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = WatchPending(ContraSha1);

		h.Model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		FinishDownload(h);

		Assert.Empty(h.Played);
		Assert.False(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.True(h.Window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	[AvaloniaFact]
	public void Reopening_the_sheet_does_not_revive_a_cancelled_watch()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = WatchPending(ContraSha1);

		Click(h.Window.FindNamed<Button>("ReplaysDoneButton"));
		OpenFromSaveStatesAgain(h);
		FinishDownload(h);

		Assert.Empty(h.Played);
		Assert.True(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
		Assert.All(Named(h.Window, "ReplaysWatchButton"), b => Assert.True(b.IsEnabled));
	}

	[AvaloniaFact]
	public void Another_game_loaded_during_the_download_cancels_the_watch()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = WatchPending(ContraSha1);

		h.RomSha1 = OtherSha1;
		FinishDownload(h);

		Assert.Empty(h.Played);
		Assert.All(Named(h.Window, "ReplaysWatchButton"), b => Assert.True(b.IsEnabled));
	}

	private static void OpenFromSaveStatesAgain(Harness h)
	{
		h.Model.OpenSaveStatesSheet();
		Dispatcher.UIThread.RunJobs();
		Click(h.Window.FindNamed<Button>("SaveStatesReplaysButton"));
	}

	//#641: Esc on the sheet opens the overlay once. With a pack-file notice
	//waiting, that one open shows the pack-file sheet instead of the overlay;
	//the two are never on screen together (replace-not-stack).
	[AvaloniaFact]
	public void Esc_with_a_pack_file_waiting_shows_the_pack_file_sheet_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		OpenFromSaveStates(h);
		h.Model.SetPendingPackDeps("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", System.IO.Path.GetTempPath()) });
		Dispatcher.UIThread.RunJobs();

		h.Model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Assert.True(h.Window.FindNamed<Panel>("PackDepSheetBackdrop").IsOnScreen());
		Assert.False(h.Window.IsPauseCardActive());
		Assert.False(h.Model.IsPlayerOverlayVisible);
	}

	//#642: switching the UI mode to Advanced takes every Player sheet down,
	//and none of them brings the Player overlay back in Advanced.
	[AvaloniaFact]
	public void Switching_to_advanced_closes_the_sheet_without_reopening_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Harness h = ShowPlayer(ContraSha1, Full);
		try {
			OpenFromSaveStates(h);
			Assert.True(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());

			ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
			Dispatcher.UIThread.RunJobs();

			Assert.False(h.Window.FindNamed<Border>("PlayerReplaysSheet").IsOnScreen());
			Assert.False(h.Model.ReplaysSheet.IsVisible);
			Assert.False(h.Model.IsPlayerOverlayVisible);
		} finally {
			ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		}
	}
}
