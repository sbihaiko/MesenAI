using System;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Share
{
	//The user's rule (2026-10-03): every wait the user can see shows a moving
	//indicator, and never an answer it does not have yet - a catalog still
	//being fetched is "looking", not "none".
	public class ShareWaitsTests
	{
		[Fact]
		public void Esc_does_nothing_while_a_replay_starts_or_stops()
		{
			Assert.Equal(ShareEscAction.None, ShareEsc.Next(ReplaySheet.Start, false, RecordingTransition.Starting));
			Assert.Equal(ShareEscAction.None, ShareEsc.Next(ReplaySheet.Recording, false, RecordingTransition.Stopping));
			Assert.Equal(ShareEscAction.CloseSheet, ShareEsc.Next(ReplaySheet.Start, false, RecordingTransition.None));
			Assert.Equal(ShareEscAction.StopRecording, ShareEsc.Next(ReplaySheet.Recording, false, RecordingTransition.None));
		}

		[Fact]
		public void Build_waits_with_its_reason_while_the_tools_are_checked()
		{
			ShareProjectIdentity project = new("/p/Contra", "Contra", "Contra (USA)", "NES", true, true);
			RemasterFeasibility ready = new(PythonGate.Found, "/py", Array.Empty<string>(), "3.12", ToolsGate.Found, "/tools");
			Assert.Equal(ShareBuildReason.CheckingTools, ShareProjectPackage.BuildReason(project, true, ready, false, false, feasibilityPending: true));
			Assert.Equal(ShareBuildReason.None, ShareProjectPackage.BuildReason(project, true, ready, false, false, feasibilityPending: false));
		}

		[Fact]
		public void The_replays_list_says_it_is_looking_until_the_catalog_answers()
		{
			Assert.Equal(ReplayWatch.LoadingLine, ReplayWatch.StatusLine(0, loading: true));
			Assert.Equal(ReplayWatch.StatusLine(0), ReplayWatch.StatusLine(0, loading: false));
			Assert.StartsWith("No shared replay", ReplayWatch.StatusLine(0, loading: false));
			//Rows already known (the last catalog) are counted while it refreshes.
			Assert.Equal(ReplayWatch.StatusLine(2), ReplayWatch.StatusLine(2, loading: true));
			Assert.NotEqual("", ReplayWatch.DownloadingLine);
		}

		[Fact]
		public void The_cheats_status_says_it_is_looking_for_community_codes_until_the_catalog_answers()
		{
			Assert.Equal(CheatSheet.CommunityLoadingLine, CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0, 0, communityLoading: true));
			Assert.Equal(CheatSheet.CommunityLoadingLine, CheatSheet.StatusLine(ConsoleType.Sms, null, false, 0, 0, communityLoading: true));
			Assert.Equal(CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0, 0), CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0, 0, communityLoading: false));
			//Codes already listed: the status line is about them.
			Assert.Equal(CheatSheet.StatusLine(ConsoleType.Sms, null, false, 1, 2), CheatSheet.StatusLine(ConsoleType.Sms, null, false, 1, 2, communityLoading: true));
		}
	}
}
