using Mesen.Logic;
using Xunit;

namespace Mesen.Tests
{
	//#1066: the Play picker's two scan claims (IsFinishFallback, IsRestoreLanding)
	//are about ONE bump of the grid and expire with the ring being the player's
	//again or with the visit that made them. The rule is host-free so it is
	//covered here without a window or the core (ADR-0123).
	public class ScanClaimsTests
	{
		private static readonly ScanClaims Both = new(FinishFallback: true, RestoreLanding: true);

		[Fact]
		public void A_fresh_visit_holds_no_claim()
		{
			Assert.Equal(new ScanClaims(false, false), ScanClaims.None);
		}

		[Fact]
		public void The_player_taking_the_ring_spends_both_claims()
		{
			Assert.Equal(ScanClaims.None, Both.AfterRingTaken());
		}

		[Fact]
		public void The_sheet_going_down_spends_both_claims()
		{
			Assert.Equal(ScanClaims.None, Both.AfterVisibilityChanged(false));
		}

		[Fact]
		public void The_sheet_coming_up_starts_a_visit_with_no_claim()
		{
			Assert.Equal(ScanClaims.None, Both.AfterVisibilityChanged(true));
		}

		[Fact]
		public void A_claim_made_by_the_scan_survives_until_the_ring_moves()
		{
			ScanClaims made = ScanClaims.None.WithFinishFallback();
			Assert.True(made.FinishFallback);
			Assert.False(made.RestoreLanding);
			Assert.True(made.WithRestoreLanding().RestoreLanding);
		}
	}
}
