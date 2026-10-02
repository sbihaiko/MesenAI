using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//G.2 (PRD Part B §8, ADR-0241, §13.5.2 W-P1/W-P2): the Play home's host-free
//rules. The home is the Player-mode recent-games screen; Advanced keeps its
//classic game-selection grid (GameSelectionScreenMode) unchanged.
public enum PlayHomeKind
{
	//W-P1: no recent game - one primary action, Open a ROM… (replaces the
	//§6.2 Welcome card: recents are necessarily empty on a true first run).
	FirstRun,
	//W-P2: Continue playing (the most recent game) + Open a ROM… + the grid
	//of the other recent games.
	WithRecents
}

public enum PlayHomeOrientation
{
	AudioAndPacks,
	AudioOnly,
	PacksOnly,
	None
}

public enum LastPlayedKind
{
	Today,
	Yesterday,
	DaysAgo,
	OnDate
}

public static class PlayHome
{
	//Beyond this many days the subtitle names the date instead of a count.
	public const int MaxDaysAgo = 6;

	public static PlayHomeKind Classify(int recentCount)
	{
		return recentCount > 0 ? PlayHomeKind.WithRecents : PlayHomeKind.FirstRun;
	}

	//W-P2: the Continue card already is the most recent game, so the Recent
	//grid lists the others (the wireframe's tiles never repeat the card).
	public static List<T> RecentGrid<T>(IReadOnlyList<T> recents)
	{
		return recents.Skip(1).ToList();
	}

	//W-P2's "last played today". Calendar days in local time, not 24 h spans,
	//so a game played at 23:50 reads "yesterday" ten minutes after midnight.
	public static (LastPlayedKind Kind, int Days) LastPlayed(DateTime played, DateTime now)
	{
		int days = (now.Date - played.Date).Days;
		if(days <= 0) {
			return (LastPlayedKind.Today, 0);
		}
		if(days == 1) {
			return (LastPlayedKind.Yesterday, 1);
		}
		return days <= MaxDaysAgo ? (LastPlayedKind.DaysAgo, days) : (LastPlayedKind.OnDate, days);
	}

	//W-P1's orientation sentence states what will happen, so it only says what
	//the settings make true (rule 10): "Enhanced audio is on" only while the
	//pack audio layer is on, the automatic community-pack clause only while
	//AutoInstallCommunityPacks is on (ADR-0146's master switch).
	public static PlayHomeOrientation Orientation(bool enhancedAudioOn, bool autoInstallCommunityPacks)
	{
		if(enhancedAudioOn) {
			return autoInstallCommunityPacks ? PlayHomeOrientation.AudioAndPacks : PlayHomeOrientation.AudioOnly;
		}
		return autoInstallCommunityPacks ? PlayHomeOrientation.PacksOnly : PlayHomeOrientation.None;
	}
}
