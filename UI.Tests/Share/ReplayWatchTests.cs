using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Share
{
	//R.2 (ADR-0205 §7, PRD Part B §13.3): the rules of the Shared replays sheet
	//- what a row says, when Watch is disabled and why (rule 4), and the one
	//in-place confirmation before a replay restarts the game (rule 7).
	public class ReplayWatchTests
	{
		private static CommunityReplay Row(int issue, string author = "alice", string subtitle = "stage skip", int frames = 3600, int cheats = 0)
		{
			CommunityReplayCheat[] list = new CommunityReplayCheat[cheats];
			for(int i = 0; i < cheats; i++) {
				list[i] = new CommunityReplayCheat("NesCustom", "0032:0" + i);
			}
			return new CommunityReplay(issue, "https://github.com/user-attachments/files/1/a.mmo", new string('a', 64), 100, "nes", "Contra (USA)", author, subtitle, frames, list, 3);
		}

		[Fact]
		public void Watch_needs_a_running_game_and_no_other_movie_or_netplay()
		{
			Assert.Equal(ReplayWatchReason.NoGame, ReplayWatch.Reason(gameRunning: false, movieBusy: false, netplay: false));
			Assert.Equal(ReplayWatchReason.MovieBusy, ReplayWatch.Reason(true, movieBusy: true, netplay: false));
			Assert.Equal(ReplayWatchReason.Netplay, ReplayWatch.Reason(true, false, netplay: true));
			Assert.Equal(ReplayWatchReason.None, ReplayWatch.Reason(true, false, false));
		}

		[Fact]
		public void A_row_reads_author_and_subtitle_and_its_length()
		{
			Assert.Equal("alice — stage skip", ReplayWatch.Title(Row(1)));
			Assert.Equal("alice", ReplayWatch.Title(Row(1, subtitle: "")));
			Assert.Equal("1:00", ReplayWatch.Length(3600));
			Assert.Equal("0:01", ReplayWatch.Length(59));
			Assert.Equal("1:01:01", ReplayWatch.Length((3600 + 61) * 60));
		}

		[Fact]
		public void A_row_with_cheats_carries_a_badge_before_it_is_picked()
		{
			Assert.Equal(0, ReplayWatch.CheatCount(Row(1)));
			Assert.Equal(2, ReplayWatch.CheatCount(Row(1, cheats: 2)));
		}

		[Fact]
		public void Watch_asks_once_in_place_before_it_restarts_the_game()
		{
			//First click arms the row; the second click on the same row plays.
			Assert.Equal(ReplayWatchStep.Arm, ReplayWatch.Click(armedIssue: 0, clickedIssue: 12));
			Assert.Equal(ReplayWatchStep.Play, ReplayWatch.Click(armedIssue: 12, clickedIssue: 12));
			//Another row moves the confirmation instead of playing.
			Assert.Equal(ReplayWatchStep.Arm, ReplayWatch.Click(armedIssue: 12, clickedIssue: 10));
		}

		[Fact]
		public void The_sheet_says_what_it_found_and_why_watch_is_off()
		{
			Assert.Equal("No shared replay for this exact copy of the game yet.", ReplayWatch.StatusLine(0));
			Assert.Equal("1 shared replay for this copy.", ReplayWatch.StatusLine(1));
			Assert.Equal("3 shared replays for this copy.", ReplayWatch.StatusLine(3));
			Assert.Equal("", ReplayWatch.ReasonText(ReplayWatchReason.None));
			Assert.Contains("game", ReplayWatch.ReasonText(ReplayWatchReason.NoGame));
			Assert.Contains("movie", ReplayWatch.ReasonText(ReplayWatchReason.MovieBusy));
			Assert.Contains("netplay", ReplayWatch.ReasonText(ReplayWatchReason.Netplay));
			Assert.Equal("", ReplayWatch.FailureText(ReplayFetchFailure.None));
			Assert.Contains("download", ReplayWatch.FailureText(ReplayFetchFailure.Unavailable));
			Assert.Contains("not played", ReplayWatch.FailureText(ReplayFetchFailure.Mismatch));
		}

		[Fact]
		public void An_armed_row_says_the_game_restarts_and_badges_its_cheats()
		{
			Assert.Equal("Watch", ReplayWatch.WatchLabel(armed: false));
			Assert.Equal("Restart & watch", ReplayWatch.WatchLabel(armed: true));
			Assert.Contains("unsaved progress", ReplayWatch.ArmedText);
			Assert.Equal("", ReplayWatch.CheatBadge(0));
			Assert.Equal("uses 1 cheat", ReplayWatch.CheatBadge(1));
			Assert.Equal("uses 2 cheats", ReplayWatch.CheatBadge(2));
		}
	}
}
