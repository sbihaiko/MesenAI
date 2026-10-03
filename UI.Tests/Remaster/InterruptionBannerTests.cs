using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//ADR-0249 (W-X1, W-X3): the interruption question is drawn as the shared
	//in-place banner. A recording that would end is the red stop square on a
	//pale red banner, anything else the orange warning on a pale orange one;
	//the button that goes on is dark for a quit and the asking workspace's tint
	//for an open or a reload; the banner takes the tint of the workspace whose
	//work would be lost.
	public class InterruptionBannerTests
	{
		[Theory]
		[InlineData(InterruptionKind.QuitWhileRecording, BannerKind.Stop)]
		[InlineData(InterruptionKind.OpenWhileRecording, BannerKind.Stop)]
		[InlineData(InterruptionKind.ReloadWhileRecording, BannerKind.Stop)]
		[InlineData(InterruptionKind.QuitWhileJob, BannerKind.Warning)]
		[InlineData(InterruptionKind.QuitWhilePackaging, BannerKind.Warning)]
		[InlineData(InterruptionKind.OpenWhileClassicBuilder, BannerKind.Warning)]
		public void A_lost_recording_is_a_stop_banner_and_the_rest_a_warning(InterruptionKind kind, BannerKind banner)
		{
			Assert.Equal(banner, InterruptionBanner.KindOf(kind));
		}

		[Theory]
		[InlineData(InterruptionKind.QuitWhileRecording, false)]
		[InlineData(InterruptionKind.QuitWhileJob, false)]
		[InlineData(InterruptionKind.QuitWhilePackaging, false)]
		[InlineData(InterruptionKind.OpenWhileRecording, true)]
		[InlineData(InterruptionKind.ReloadWhileRecording, true)]
		[InlineData(InterruptionKind.OpenWhileClassicBuilder, true)]
		public void Quitting_goes_on_with_a_dark_button_and_opening_with_the_tint(InterruptionKind kind, bool tinted)
		{
			Assert.Equal(tinted, InterruptionBanner.GoIsTinted(kind));
		}

		[Theory]
		[InlineData(InterruptionKind.QuitWhileRecording, Workspace.Remaster)]
		[InlineData(InterruptionKind.QuitWhileJob, Workspace.Remaster)]
		[InlineData(InterruptionKind.OpenWhileRecording, Workspace.Remaster)]
		[InlineData(InterruptionKind.ReloadWhileRecording, Workspace.Remaster)]
		[InlineData(InterruptionKind.QuitWhilePackaging, Workspace.Share)]
		[InlineData(InterruptionKind.OpenWhileClassicBuilder, Workspace.Play)]
		public void The_banner_takes_the_tint_of_the_workspace_whose_work_is_lost(InterruptionKind kind, Workspace workspace)
		{
			Assert.Equal(workspace, InterruptionBanner.WorkspaceOf(kind));
		}
	}
}
