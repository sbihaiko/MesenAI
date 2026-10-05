using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//ADR-0249 (W-S1) / ADR-0255: the arcade cabinet's four port lamps. The
	//count the Core reports is the truth; the name is a nicety. Host-free, so
	//these run with no pad and no core.
	public class PadPortLampsTests
	{
		private static string? Named(uint index) => "Pad " + index + " name";

		[Fact]
		public void No_pads_report_four_dark_lamps()
		{
			PadPortStrip strip = PadPortLamps.Build(0, Named);

			Assert.Equal(4, strip.Lamps.Count);
			Assert.All(strip.Lamps, l => Assert.False(l.IsLit));
			Assert.Equal(new[] { 1, 2, 3, 4 }, strip.Lamps.Select(l => l.Port));
			Assert.False(strip.HasOverflow);
			Assert.Equal(0, strip.OverflowPads);
			//A dim port names nobody and opens no tooltip.
			Assert.All(strip.Lamps, l => Assert.Null(l.Tip));
			Assert.Equal("P1", strip.Lamps[0].Label);
		}

		[Fact]
		public void One_pad_lights_exactly_the_first_port()
		{
			PadPortStrip strip = PadPortLamps.Build(1, Named);

			Assert.True(strip.Lamps[0].IsLit);
			Assert.False(strip.Lamps[1].IsLit);
			Assert.False(strip.Lamps[2].IsLit);
			Assert.False(strip.Lamps[3].IsLit);
			//The lit lamp carries the pad's name, which is what its tooltip shows.
			Assert.Equal("Pad 0 name", strip.Lamps[0].Name);
			Assert.Equal("Pad 0 name", strip.Lamps[0].Tip);
		}

		[Fact]
		public void Four_pads_light_every_port()
		{
			PadPortStrip strip = PadPortLamps.Build(4, Named);

			Assert.All(strip.Lamps, l => Assert.True(l.IsLit));
			Assert.False(strip.HasOverflow);
		}

		[Fact]
		public void More_pads_than_ports_never_adds_a_fifth_lamp()
		{
			PadPortStrip strip = PadPortLamps.Build(6, Named);

			//The strip is fixed at four lamps whatever the backend reports - the
			//cabinet has four ports, so a fifth pad is not a fifth lamp.
			Assert.Equal(4, strip.Lamps.Count);
			Assert.All(strip.Lamps, l => Assert.True(l.IsLit));
			//...and the extra is said in words, not drawn.
			Assert.Equal(2, strip.OverflowPads);
			Assert.True(strip.HasOverflow);
			Assert.Equal(PadPortLamps.OverflowManyKey, PadPortLamps.OverflowKey(strip.OverflowPads));
			Assert.Equal(PadPortLamps.OverflowOneKey, PadPortLamps.OverflowKey(1));
		}

		[Fact]
		public void A_strip_that_draws_the_same_is_left_alone()
		{
			//The 1 s poll builds a fresh strip every second; an unchanged one must
			//not be re-assigned, or the strip's visuals would rebuild - and a
			//hovered pad name's tooltip would drop - once a second.
			Assert.True(PadPortLamps.Build(2, Named).DrawsSameAs(PadPortLamps.Build(2, Named)));
			Assert.True(PadPortLamps.Build(0, Named).DrawsSameAs(PadPortLamps.Build(0, Named)));
			//A different lit pattern, a different name, or a different overflow all
			//count as a change.
			Assert.False(PadPortLamps.Build(2, Named).DrawsSameAs(PadPortLamps.Build(3, Named)));
			Assert.False(PadPortLamps.Build(2, Named).DrawsSameAs(PadPortLamps.Build(2, _ => "another pad")));
			Assert.False(PadPortLamps.Build(6, Named).DrawsSameAs(PadPortLamps.Build(7, Named)));
		}

		[Fact]
		public void A_pad_the_backend_cannot_name_still_lights_its_port()
		{
			//The count is the truth, the name is a nicety: a backend that answers
			//nothing (macOS reports every pad unidentified) must not dim a lamp.
			PadPortStrip strip = PadPortLamps.Build(2, _ => null);

			Assert.True(strip.Lamps[0].IsLit);
			Assert.True(strip.Lamps[1].IsLit);
			Assert.Equal("", strip.Lamps[0].Name);
			Assert.Null(strip.Lamps[0].Tip);
			Assert.Equal("P1", strip.Lamps[0].Label);
		}
	}
}
