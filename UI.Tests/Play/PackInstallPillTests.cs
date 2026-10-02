using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.4 (PRD Part B §8, §13.5.2 W-P9, ADR-0146): a pack that installs while the
	//game plays is a HUD pill - when it shows, what ends it.
	public class PackInstallPillTests
	{
		private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0);

		[Fact]
		public void A_new_or_changed_artifact_shows_the_pill()
		{
			Assert.True(PackInstallPill.ShowsFor(null, "ab12"));
			Assert.True(PackInstallPill.ShowsFor("cd34", "ab12"));
		}

		//The routine load of a game whose pack is already installed must not flash.
		[Fact]
		public void The_installed_artifact_shows_nothing()
		{
			Assert.False(PackInstallPill.ShowsFor("AB12", "ab12"));
			Assert.False(PackInstallPill.ShowsFor(null, ""));
		}

		[Fact]
		public void A_successful_install_hides_the_pill()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			Assert.Equal(PackInstallPillState.Installing, pill.State);
			Assert.Equal("Contra 80s", pill.PackName);
			pill.Finish(installed: true, T0);
			Assert.Equal(PackInstallPillState.Hidden, pill.State);
		}

		[Fact]
		public void A_failure_stays_five_seconds_then_goes()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			pill.Finish(installed: false, T0);
			Assert.Equal(PackInstallPillState.Failed, pill.State);
			pill.Tick(T0.AddSeconds(4.9));
			Assert.Equal(PackInstallPillState.Failed, pill.State);
			pill.Tick(T0.Add(PackInstallPill.FailureDuration));
			Assert.Equal(PackInstallPillState.Hidden, pill.State);
		}

		[Fact]
		public void A_finish_without_a_pill_changes_nothing()
		{
			PackInstallPill pill = new();
			pill.Finish(installed: false, T0);
			Assert.Equal(PackInstallPillState.Hidden, pill.State);
		}

		[Fact]
		public void A_silent_skip_cancels_the_pill()
		{
			PackInstallPill pill = new();
			pill.Begin("Contra 80s");
			pill.Cancel();
			Assert.Equal(PackInstallPillState.Hidden, pill.State);
		}
	}
}
