using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.2 (PRD Part B §8, §13.5.2 W-P1/W-P2): which home shows, what the Recent
	//grid lists, and the Continue card's "last played" wording.
	public class PlayHomeTests
	{
		[Fact]
		public void No_recents_is_the_first_run_home()
		{
			Assert.Equal(PlayHomeKind.FirstRun, PlayHome.Classify(0));
			Assert.Equal(PlayHomeKind.WithRecents, PlayHome.Classify(1));
			Assert.Equal(PlayHomeKind.WithRecents, PlayHome.Classify(40));
		}

		[Fact]
		public void Recent_grid_lists_every_game_but_the_continue_card()
		{
			Assert.Equal(new List<string> { "Zelda", "Metroid" }, PlayHome.RecentGrid(new[] { "Contra", "Zelda", "Metroid" }));
			Assert.Empty(PlayHome.RecentGrid(new[] { "Contra" }));
			Assert.Empty(PlayHome.RecentGrid(Array.Empty<string>()));
		}

		[Fact]
		public void Last_played_uses_calendar_days()
		{
			DateTime now = new(2026, 10, 2, 0, 10, 0);
			Assert.Equal((LastPlayedKind.Today, 0), PlayHome.LastPlayed(now.AddMinutes(-5), now));
			Assert.Equal((LastPlayedKind.Yesterday, 1), PlayHome.LastPlayed(now.AddMinutes(-20), now));
			Assert.Equal((LastPlayedKind.DaysAgo, 3), PlayHome.LastPlayed(now.AddDays(-3), now));
			Assert.Equal((LastPlayedKind.DaysAgo, PlayHome.MaxDaysAgo), PlayHome.LastPlayed(now.AddDays(-PlayHome.MaxDaysAgo), now));
			Assert.Equal((LastPlayedKind.OnDate, 30), PlayHome.LastPlayed(now.AddDays(-30), now));
			Assert.Equal((LastPlayedKind.Today, 0), PlayHome.LastPlayed(now.AddHours(2), now));
		}

		[Theory]
		[InlineData(true, true, PlayHomeOrientation.AudioAndPacks)]
		[InlineData(true, false, PlayHomeOrientation.AudioOnly)]
		[InlineData(false, true, PlayHomeOrientation.PacksOnly)]
		[InlineData(false, false, PlayHomeOrientation.None)]
		public void First_run_sentence_only_says_what_the_settings_make_true(bool audio, bool autoPacks, PlayHomeOrientation expected)
		{
			Assert.Equal(expected, PlayHome.Orientation(audio, autoPacks));
		}
	}
}
