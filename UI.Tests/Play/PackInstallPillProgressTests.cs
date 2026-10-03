using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#734 follow-up (W-P9): the install pill's bar always moves. While the
	//download reports its size the bar fills with the bytes; without a size,
	//and once the bytes are in (hash, extract, install), it is indeterminate.
	public class PackInstallPillProgressTests
	{
		[Fact]
		public void The_bar_starts_indeterminate()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			Assert.Null(pill.Fraction);
		}

		[Fact]
		public void A_download_with_a_size_fills_the_bar()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			Assert.True(pill.Report(50, 200));
			Assert.Equal(0.25, pill.Fraction);
		}

		//The UI is told only when the bar moves by a percent or changes mode.
		[Fact]
		public void Reports_under_a_percent_are_not_a_change()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			pill.Report(50, 200);
			Assert.False(pill.Report(51, 200));
			Assert.True(pill.Report(53, 200));
		}

		[Fact]
		public void A_download_without_a_size_stays_indeterminate()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			Assert.False(pill.Report(4096, null));
			Assert.False(pill.Report(4096, 0));
			Assert.Null(pill.Fraction);
		}

		[Fact]
		public void Once_the_bytes_are_in_the_install_is_indeterminate()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			pill.Report(100, 200);
			Assert.True(pill.Report(200, 200));
			Assert.Null(pill.Fraction);
		}

		[Fact]
		public void Progress_without_an_install_changes_nothing()
		{
			PackInstallPill pill = new();
			Assert.False(pill.Report(50, 200));
			Assert.Null(pill.Fraction);
		}

		[Fact]
		public void A_new_install_starts_over_indeterminate()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			pill.Report(100, 200);
			pill.Begin("Metroid HD");
			Assert.Null(pill.Fraction);
		}

		//W-P9 is a Play render: the pill shows in Player mode's Play, as long as
		//there is something to say.
		[Theory]
		[InlineData(PackInstallPillState.Installing, true, true, true)]
		[InlineData(PackInstallPillState.Failed, true, true, true)]
		[InlineData(PackInstallPillState.Hidden, true, true, false)]
		[InlineData(PackInstallPillState.Installing, false, true, false)]
		[InlineData(PackInstallPillState.Installing, true, false, false)]
		public void The_pill_shows_in_Player_modes_Play(PackInstallPillState state, bool playerMode, bool playWorkspace, bool shown)
		{
			Assert.Equal(shown, PackInstallPill.ShowsOnScreen(state, playerMode, playWorkspace));
		}
	}
}
