using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//#672: MesenAI reports the upstream version number, so the upstream feed
	//(nesdev-org/MesenAutoUpdate) would offer - and install - an upstream build
	//over the fork. The fork's only channel is ADR-0204's ci-latest pre-release,
	//which publishes no machine-readable version feed.
	public class UpdateChannelTests
	{
		[Fact]
		public void There_is_no_version_feed_to_contact()
		{
			Assert.Null(UpdateChannel.FeedUrl);
			Assert.False(UpdateChannel.HasFeed);
		}

		[Theory]
		[InlineData("https://github.com/nesdev-org/SourMesen/releases/download/2.2.2/Mesen_2.2.2_Windows.zip")]
		[InlineData("https://github.com/SourMesen/Mesen2/releases/download/2.2.2/Mesen_2.2.2_Windows.zip")]
		[InlineData("http://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-macos-arm64.zip")]
		[InlineData("https://github.com/sbihaiko/MesenAI.evil/releases/download/x.zip")]
		[InlineData("https://github.com/sbihaiko/MesenAIx/releases/download/x.zip")]
		[InlineData("https://github.com/sbihaiko/MesenAI/archive/main.zip")]
		[InlineData("")]
		[InlineData(null)]
		public void An_upstream_or_foreign_download_url_is_refused(string? url)
		{
			Assert.False(UpdateChannel.IsAllowedDownloadUrl(url));
		}

		[Fact]
		public void Only_a_fork_release_asset_is_accepted()
		{
			Assert.True(UpdateChannel.IsAllowedDownloadUrl("https://github.com/sbihaiko/MesenAI/releases/download/ci-latest/MesenAI-ci-macos-arm64.zip"));
		}

		[Fact]
		public void The_release_page_is_the_forks()
		{
			Assert.Equal("https://github.com/sbihaiko/MesenAI/releases", UpdateChannel.ReleasesPageUrl);
		}

		[Theory]
		[InlineData(false, true, UpdateCheckAction.Skip)]
		[InlineData(false, false, UpdateCheckAction.OfferReleasePage)]
		[InlineData(true, true, UpdateCheckAction.FetchFeed)]
		[InlineData(true, false, UpdateCheckAction.FetchFeed)]
		public void Without_a_feed_the_startup_check_is_silent_and_the_menu_points_at_the_release_page(bool hasFeed, bool silent, UpdateCheckAction expected)
		{
			Assert.Equal(expected, UpdateChannel.Decide(hasFeed, silent));
		}

		[Fact]
		public void Today_the_startup_check_does_nothing_and_the_menu_offers_the_release_page()
		{
			Assert.Equal(UpdateCheckAction.Skip, UpdateChannel.Decide(silent: true));
			Assert.Equal(UpdateCheckAction.OfferReleasePage, UpdateChannel.Decide(silent: false));
		}

		[Fact]
		public void The_first_run_sheet_does_not_promise_a_check_that_cannot_run()
		{
			Assert.Equal(UpdateChannel.HasFeed, PlayFirstRun.Defaults.CheckForUpdates);
		}
	}
}
