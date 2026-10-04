using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//W-P7's draft across a detour (PRD Part B §8, §13.5.2 W-P7, ADR-0244
	//Decision 3): the Pack row opens the pack sheets to look at the pack, which
	//is not a decision about the switches - so the flips come back with the
	//player, and only the one button applies them.
	public class EnhancementsDraftVisitTests
	{
		private static readonly EnhancementsState Off = new(false, false, false, false);
		private static readonly EnhancementsState On = new(true, true, true, true);

		[Fact]
		public void The_pack_row_holds_the_draft_and_any_other_exit_ends_it()
		{
			Assert.True(EnhancementsDraftVisit.Holds(EnhancementsDraftExit.PackRow));
			Assert.False(EnhancementsDraftVisit.Holds(EnhancementsDraftExit.Closed));
		}

		//Nothing was flipped: the panel reads what is applied, as a fresh open does.
		[Fact]
		public void A_visit_with_no_flip_resumes_on_what_is_applied()
		{
			Assert.Equal(On, EnhancementsSheet.Resume(Off, Off, On));
		}

		//The flip the player made is still pending against the new applied state.
		[Fact]
		public void A_flip_stays_flipped_on_the_way_back()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.Widescreen, true);
			EnhancementsState resumed = EnhancementsSheet.Resume(Off, draft, Off);

			Assert.Equal(draft, resumed);
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(Off, resumed, layerChangeKeepsPlace: false));
		}

		//A switch changed elsewhere during the detour (Settings › Audio turns the
		//synth on) is followed, not shown stale - and it is not a pending flip.
		[Fact]
		public void A_switch_the_player_did_not_touch_follows_what_is_applied_now()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.Widescreen, true);
			EnhancementsState appliedNow = new(true, false, false, false);

			EnhancementsState resumed = EnhancementsSheet.Resume(Off, draft, appliedNow);

			Assert.True(resumed.ModernInstruments, "the switch turned on elsewhere is shown on");
			Assert.True(resumed.Widescreen, "the player's flip is kept");
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(appliedNow, resumed, layerChangeKeepsPlace: false));
		}

		//The player flipped a switch to where the world moved it: nothing pending.
		[Fact]
		public void A_flip_that_matches_what_is_applied_now_leaves_nothing_pending()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.Widescreen, true);
			EnhancementsState appliedNow = new(false, false, true, false);

			EnhancementsState resumed = EnhancementsSheet.Resume(Off, draft, appliedNow);

			Assert.Equal(appliedNow, resumed);
			Assert.Equal(EnhancementsApplyKind.None, EnhancementsSheet.Pending(appliedNow, resumed, layerChangeKeepsPlace: false));
		}
	}
}
