using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//#925 (ruling on #916, ADR-0255): a pad's own light shows the colour of the
	//player port its keys live under, and follows the keys when they move.
	//Host-free: the ports are plain data and the pads are their key blocks.
	public class PadLightsTests
	{
		private const int FirstPad = 0x1000;
		private const int SecondPad = 0x1100;

		private static SheetPort Port(int colorIndex, params ushort[] keys) =>
			new SheetPort("Port" + (colorIndex + 1), "P" + (colorIndex + 1), colorIndex,
				new[] { keys, Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>() },
				new[] { Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>() });

		[Fact]
		public void Each_pad_lights_in_the_colour_of_the_port_its_keys_are_under()
		{
			IReadOnlyList<SheetPort> ports = new[] {
				Port(0, (ushort)(SecondPad + 1), (ushort)(SecondPad + 2)),
				Port(1, (ushort)(FirstPad + 1))
			};

			IReadOnlyList<PadLight> lights = PadLights.Plan(ports, new[] { FirstPad, SecondPad });

			//The palette's literals (the sheet's PLAYERS rows): P1 blue, P2 red.
			Assert.Equal(new[] {
				new PadLight(0, FirstPad, new PlayerColor(0xFF, 0x3B, 0x30)),
				new PadLight(1, SecondPad, new PlayerColor(0x00, 0x7A, 0xFF))
			}, lights);
		}

		[Fact]
		public void The_light_follows_the_keys_when_the_pad_is_reassigned()
		{
			IReadOnlyList<PadLight> before = PadLights.Plan(new[] { Port(0, (ushort)(FirstPad + 1)), Port(1) }, new[] { FirstPad });
			IReadOnlyList<PadLight> after = PadLights.Plan(new[] { Port(0), Port(1, (ushort)(FirstPad + 1)) }, new[] { FirstPad });

			Assert.Equal(new PlayerColor(0x00, 0x7A, 0xFF), Assert.Single(before).Color);
			Assert.Equal(new PlayerColor(0xFF, 0x3B, 0x30), Assert.Single(after).Color);
		}

		[Fact]
		public void A_pad_under_no_port_is_left_alone()
		{
			//Keyboard keys only under P1, and the pad bound nowhere: no player
			//colour belongs to it, so the plan does not touch its light.
			IReadOnlyList<PadLight> lights = PadLights.Plan(new[] { Port(0, 65, 66), Port(1) }, new[] { FirstPad });

			Assert.Empty(lights);
		}

		[Fact]
		public void A_pad_in_a_later_slot_of_a_port_still_takes_that_ports_colour()
		{
			SheetPort p2 = new SheetPort("Port2", "P2", 1,
				new[] { new[] { (ushort)(SecondPad + 1) }, new[] { (ushort)(FirstPad + 3) }, Array.Empty<ushort>(), Array.Empty<ushort>() },
				new[] { Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>() });

			IReadOnlyList<PadLight> lights = PadLights.Plan(new[] { Port(0), p2 }, new[] { FirstPad });

			Assert.Equal(new PlayerColor(0xFF, 0x3B, 0x30), Assert.Single(lights).Color);
		}

		[Fact]
		public void An_unchanged_light_is_sent_once_and_a_changed_one_again()
		{
			PadLightSync sync = new();
			PadLight blue = new(0, FirstPad, new PlayerColor(0x00, 0x7A, 0xFF));
			PadLight red = new(0, FirstPad, new PlayerColor(0xFF, 0x3B, 0x30));

			Assert.Equal(new[] { blue }, sync.Due(new[] { blue }, 1));
			Assert.Empty(sync.Due(new[] { blue }, 1));
			Assert.Equal(new[] { red }, sync.Due(new[] { red }, 1));
		}

		[Fact]
		public void Every_light_is_sent_again_when_the_connected_pads_change()
		{
			//A pad plugged in or out renumbers the host's list and a reconnected pad
			//comes back with its own default light, so the whole plan goes out again.
			PadLightSync sync = new();
			PadLight blue = new(0, FirstPad, new PlayerColor(0x00, 0x7A, 0xFF));
			sync.Due(new[] { blue }, 1);

			Assert.Equal(new[] { blue }, sync.Due(new[] { blue }, 2));
			Assert.Empty(sync.Due(new[] { blue }, 2));
		}
	}
}
