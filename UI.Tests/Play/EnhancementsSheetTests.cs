using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.4 (PRD Part B §8, §13.5.2 W-P7, ADR-0244 Decision 3): the toggles edit a
	//draft, and the button names the biggest restart the draft causes.
	public class EnhancementsSheetTests
	{
		private static readonly EnhancementsState Off = new(false, false, false, false);

		[Fact]
		public void Nothing_pending_is_done()
		{
			Assert.Equal(EnhancementsApplyKind.None, EnhancementsSheet.Pending(Off, Off, layerChangeKeepsPlace: false));
		}

		[Fact]
		public void Widescreen_alone_applies_without_a_reload()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.Widescreen, true);
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(Off, draft, false));
		}

		//One place per switch: Textures and Music live per game in W-P6 (and as
		//defaults in Options), so the sheet has only Border as a pack layer.
		[Fact]
		public void The_border_change_reloads_until_P9_keeps_the_place()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.Border, true);
			Assert.True(EnhancementsSheet.LayersChanged(Off, draft));
			Assert.Equal(EnhancementsApplyKind.Reload, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: false));
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: true));
		}

		//Modern instruments is the live synth switch: applied at once, no reload.
		[Fact]
		public void Modern_instruments_applies_live_without_a_reload()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, EnhancementToggle.ModernInstruments, true);
			Assert.True(draft.ModernInstruments);
			Assert.False(EnhancementsSheet.LayersChanged(Off, draft));
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: false));
		}

		//ADR-0244: Overclock is not covered by the in-place swap; it restarts.
		[Fact]
		public void Overclock_names_the_restart_even_with_other_changes()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(EnhancementsSheet.Flip(Off, EnhancementToggle.Border, true), EnhancementToggle.Overclock, true);
			Assert.Equal(EnhancementsApplyKind.Restart, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: true));
		}

		[Fact]
		public void Overclock_does_not_flip_where_the_console_has_none()
		{
			Assert.Equal(Off, EnhancementsSheet.Flip(Off, EnhancementToggle.Overclock, overclockSupported: false));
		}

		[Fact]
		public void Flipping_twice_leaves_nothing_pending()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(EnhancementsSheet.Flip(Off, EnhancementToggle.ModernInstruments, true), EnhancementToggle.ModernInstruments, true);
			Assert.Equal(EnhancementsApplyKind.None, EnhancementsSheet.Pending(Off, draft, false));
		}
	}
}
