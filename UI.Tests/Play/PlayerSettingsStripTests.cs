using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#852: the Player settings strip's segments are sized by the tab count, not
//fixed at 96 px. ADR-0256 Decision 8 added a fifth tab (System) to a strip the
//reference mockups draw at four, a fixed 96 px ran 42 px past the 480 px
//sheet's right edge, and the last label rendered as "Syst" instead of
//"System". The rule lives in PlayerSettingsEssentials, host-free; the rendered
//result is in UI.HeadlessTests (PlayerThemeSettingsRenderTests).
public class PlayerSettingsStripTests
{
	[Fact]
	public void Four_segments_keep_the_mockups_ninety_six_pixels()
	{
		//docs/media/gui-redesign/W-P8..W-P11.png draw the strip at four tabs,
		//where 96 px each still fits: 4 x 96 = 384 in a 440 px strip, which
		//leaves the track centred and narrower than the sheet.
		Assert.Equal(96.0, PlayerSettingsEssentials.SegmentWidth(440, 4));
	}

	[Fact]
	public void A_fifth_segment_shares_the_strip_instead_of_overflowing_it()
	{
		//Five 96 px segments are 480 px - wider than the sheet's 440 px of
		//content, which is what #852 rendered. Sharing the width puts the last
		//one back inside the sheet.
		double width = PlayerSettingsEssentials.SegmentWidth(440, 5);
		Assert.Equal(88.0, width);
		Assert.True(width * 5 <= 440);
	}

	[Fact]
	public void Every_count_fits_the_width_it_is_given()
	{
		foreach(int count in new[] { 1, 2, 3, 4, 5, 6, 7, 9, 13 }) {
			double width = PlayerSettingsEssentials.SegmentWidth(440, count);
			Assert.True(width > 0, $"{count} tabs collapsed to {width} px");
			Assert.True(width <= PlayerSettingsEssentials.MaxSegment, $"{count} tabs asked for {width} px, over the mockup's segment");
			Assert.True(width * count <= 440.001, $"{count} tabs need {width * count} px in 440");
		}
	}

	[Fact]
	public void An_unconstrained_measure_falls_back_to_the_mockups_segment()
	{
		//MeasureOverride can be handed Infinity. Dividing by it would be a NaN
		//width, and a NaN in a Rect arranges children nowhere.
		Assert.Equal(96.0, PlayerSettingsEssentials.SegmentWidth(double.PositiveInfinity, 5));
		Assert.Equal(96.0, PlayerSettingsEssentials.SegmentWidth(double.NaN, 5));
	}

	[Fact]
	public void No_children_is_not_a_division_by_zero()
	{
		Assert.Equal(96.0, PlayerSettingsEssentials.SegmentWidth(440, 0));
	}
}
