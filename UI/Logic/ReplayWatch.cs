using System;

namespace Mesen.Logic
{
	//Why a shared replay's Watch is disabled (PRD Part B §13.3 rule 4).
	public enum ReplayWatchReason
	{
		None,
		NoGame,
		//A movie is already recording or playing (Record and share included).
		MovieBusy,
		Netplay
	}

	//Why a listed replay could not be fetched for playback.
	public enum ReplayFetchFailure
	{
		None,
		//Off the allow-list, a network error, a non-200 answer or over the cap.
		Unavailable,
		//The bytes are not the row's (size or sha256): never played (MEI §3 item 1).
		Mismatch
	}

	//The verified .mmo in the downloads/ cache, or the failure.
	public sealed record ReplayFetchResult(string? Path, ReplayFetchFailure Failure);

	public enum ReplayWatchStep
	{
		//First click: the row asks once, in place, before restarting the game.
		Arm,
		Play
	}

	//R.2 (ADR-0205 §7): the host-free rules of the Shared replays sheet
	//(W-P4 › Save states › Shared replays). Playing a replay power-cycles the
	//game (MesenMovie::Play), so the player's unsaved progress is lost: Watch
	//confirms once in place (rule 7) instead of opening a dialog.
	public static class ReplayWatch
	{
		public static ReplayWatchReason Reason(bool gameRunning, bool movieBusy, bool netplay)
		{
			if(!gameRunning) {
				return ReplayWatchReason.NoGame;
			}
			if(movieBusy) {
				return ReplayWatchReason.MovieBusy;
			}
			return netplay ? ReplayWatchReason.Netplay : ReplayWatchReason.None;
		}

		//"<alias> — <subtitle>", the two parts of the issue title read off the
		//artifact (§5); the alias alone when there is no description.
		public static string Title(CommunityReplay row)
		{
			return string.IsNullOrWhiteSpace(row.Subtitle) ? row.Author : row.Author + " — " + row.Subtitle;
		}

		//The run's length from its frame count, at 60 frames per second.
		public static string Length(int frames)
		{
			TimeSpan t = TimeSpan.FromSeconds(Math.Max(1, (long)Math.Round(Math.Max(0, frames) / 60.0)));
			return t.TotalHours >= 1 ? ((int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")) : ((int)t.TotalMinutes + ":" + t.Seconds.ToString("00"));
		}

		//§7: a row whose replay carries cheats says so before it is picked.
		public static int CheatCount(CommunityReplay row) => row.Cheats.Count;

		//armedIssue is the row that already asked (0 for none).
		public static ReplayWatchStep Click(int armedIssue, int clickedIssue)
		{
			return armedIssue != 0 && armedIssue == clickedIssue ? ReplayWatchStep.Play : ReplayWatchStep.Arm;
		}
		//The user's rule (2026-10-03): until the catalog fetch answers, an
		//empty list is "looking", never "none" (with a moving bar beside it).
		public const string LoadingLine = "Looking for shared replays…";

		//The verified download before Watch plays (with a moving bar).
		public const string DownloadingLine = "Downloading the replay…";

		public static string StatusLine(int count, bool loading)
		{
			return loading && count == 0 ? LoadingLine : StatusLine(count);
		}

		//The sheet's status line: how many rows this exact copy has.
		public static string StatusLine(int count)
		{
			return count switch {
				0 => "No shared replay for this exact copy of the game yet.",
				1 => "1 shared replay for this copy.",
				_ => count + " shared replays for this copy."
			};
		}

		//Rule 4: a disabled Watch says why.
		public static string ReasonText(ReplayWatchReason reason)
		{
			return reason switch {
				ReplayWatchReason.NoGame => "Load the game to watch its replays.",
				ReplayWatchReason.MovieBusy => "Stop the movie that is recording or playing first.",
				ReplayWatchReason.Netplay => "Replays cannot play during a netplay session.",
				_ => ""
			};
		}

		public static string FailureText(ReplayFetchFailure failure)
		{
			return failure switch {
				ReplayFetchFailure.Unavailable => "The replay could not download. Try again later.",
				ReplayFetchFailure.Mismatch => "The downloaded file is not the listed replay, so it was not played.",
				_ => ""
			};
		}

		//#640: the verified download is awaited (up to the size cap), and
		//playing power-cycles whatever is loaded. It plays only while the sheet
		//is still up for the same Watch (Close and Open bump the generation)
		//and the loaded ROM is still the copy the rows were listed for.
		public static bool PlaysAfterDownload(bool sheetVisible, int watchGeneration, int currentGeneration, string sheetRomSha1, string currentRomSha1)
		{
			return sheetVisible
				&& watchGeneration == currentGeneration
				&& !string.IsNullOrEmpty(sheetRomSha1)
				&& string.Equals(sheetRomSha1, currentRomSha1, StringComparison.OrdinalIgnoreCase);
		}

		public static string WatchLabel(bool armed) => armed ? "Restart & watch" : "Watch";

		public const string ArmedText = "Watching restarts the game: unsaved progress is lost. Press Restart & watch to go on.";

		//§7: the badge on a row whose replay turns cheats on.
		public static string CheatBadge(int count)
		{
			return count switch {
				0 => "",
				1 => "uses 1 cheat",
				_ => "uses " + count + " cheats"
			};
		}
	}
}
