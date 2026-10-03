using System;

namespace Mesen.Logic;

public enum UpdateCheckAction
{
	//Startup check with no feed: do nothing, say nothing.
	Skip,
	//Help > Check for updates with no feed: offer to open the release page.
	OfferReleasePage,
	//Fetch FeedUrl and compare versions (only once the fork has a feed).
	FetchFeed
}

//#672: where "Check for updates" looks, and which download URLs it accepts.
//MesenAI reports the upstream version number (Core/Shared/EmuSettings.cpp), so
//the upstream feed (nesdev-org/MesenAutoUpdate) would offer every install an
//upstream build and install it over the fork. The fork's only channel is
//ADR-0204's `ci-latest` pre-release, which publishes fixed-name assets and no
//machine-readable version feed - so there is no feed, the startup check is a
//no-op and the menu points at the fork's release page. Pointing the check at a
//fork feed is an open decision; until then only fork release assets would ever
//be accepted as a download.
public static class UpdateChannel
{
	public static string? FeedUrl => null;

	public static bool HasFeed => FeedUrl != null;

	public const string ReleasesPageUrl = "https://github.com/sbihaiko/MesenAI/releases";

	private const string DownloadPrefix = ReleasesPageUrl + "/download/";

	public static bool IsAllowedDownloadUrl(string? url)
	{
		return url != null && url.StartsWith(DownloadPrefix, StringComparison.Ordinal);
	}

	//silent: the startup check (Preferences.AutomaticallyCheckForUpdates);
	//otherwise Help > Check for updates.
	public static UpdateCheckAction Decide(bool silent) => Decide(HasFeed, silent);

	public static UpdateCheckAction Decide(bool hasFeed, bool silent)
	{
		if(hasFeed) {
			return UpdateCheckAction.FetchFeed;
		}
		return silent ? UpdateCheckAction.Skip : UpdateCheckAction.OfferReleasePage;
	}
}
