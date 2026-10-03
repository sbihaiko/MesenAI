using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.4 (PRD Part B §8, §13.5.2 W-P7, ADR-0244 Decision 3): the toggles edit a
	//draft, and the button names the biggest restart the draft causes.
	public class EnhancementsSheetTests
	{
		private static readonly EnhancementsState Off = new(false, false, false, false, false);

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

		[Theory]
		[InlineData(EnhancementToggle.Textures)]
		[InlineData(EnhancementToggle.Audio)]
		[InlineData(EnhancementToggle.Border)]
		public void A_pack_layer_change_reloads_until_P9_keeps_the_place(EnhancementToggle toggle)
		{
			EnhancementsState draft = EnhancementsSheet.Flip(Off, toggle, true);
			Assert.True(EnhancementsSheet.LayersChanged(Off, draft));
			Assert.Equal(EnhancementsApplyKind.Reload, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: false));
			Assert.Equal(EnhancementsApplyKind.Apply, EnhancementsSheet.Pending(Off, draft, layerChangeKeepsPlace: true));
		}

		//ADR-0244: Overclock is not covered by the in-place swap; it restarts.
		[Fact]
		public void Overclock_names_the_restart_even_with_other_changes()
		{
			EnhancementsState draft = EnhancementsSheet.Flip(EnhancementsSheet.Flip(Off, EnhancementToggle.Textures, true), EnhancementToggle.Overclock, true);
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
			EnhancementsState draft = EnhancementsSheet.Flip(EnhancementsSheet.Flip(Off, EnhancementToggle.Audio, true), EnhancementToggle.Audio, true);
			Assert.Equal(EnhancementsApplyKind.None, EnhancementsSheet.Pending(Off, draft, false));
		}
	}
}
