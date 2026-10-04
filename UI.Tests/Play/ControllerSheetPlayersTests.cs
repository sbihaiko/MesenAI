using Mesen.Interop;
using Mesen.Logic;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0255 slice 2 (W-P17 PLAYERS): the sheet reads which player a pad is by
	//reading the ports - Port1/Port2/Controller - never the four mapping slots,
	//which are alternatives within one port. These pin the two host-free rules
	//the sheet needs on its own side: which ports a console offers, which device
	//a port or a slot speaks for, and what "move this pad to that player" plans.
	public class ControllerSheetPlayersTests
	{
		private static ushort PadKey(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
		private static readonly ushort KeyboardKey = 0x10; //below BaseGamepadIndex: a keyboard key

		private static SheetPort Port(string key, int player, params ushort[][] slots)
		{
			ushort[][] four = new ushort[4][];
			for(int i = 0; i < 4; i++) {
				four[i] = i < slots.Length ? slots[i] : System.Array.Empty<ushort>();
			}
			return new SheetPort(key, $"Player {player}", player - 1, four);
		}

		//The ports are the console's own. Mapping1..4 are alternatives inside one
		//port and must never appear here: reading them as four ports is exactly
		//the mistake ADR-0255 records under "The answers, against the code".
		[Fact]
		public void The_ports_are_the_consoles_own_not_its_mapping_slots()
		{
			Assert.Equal(new[] { ("Port1", 1), ("Port2", 2) }, ControllerSheetPorts.For(ConsoleType.Nes));
			Assert.Equal(new[] { ("Port1", 1), ("Port2", 2) }, ControllerSheetPorts.For(ConsoleType.Sms));
			Assert.Equal(new[] { ("Controller", 1) }, ControllerSheetPorts.For(ConsoleType.Gameboy));
			Assert.Equal(new[] { ("Controller", 1) }, ControllerSheetPorts.For(ConsoleType.Gba));
			Assert.DoesNotContain(ControllerSheetPorts.For(ConsoleType.Nes), p => p.Key.StartsWith("Mapping"));
		}

		//A slot's device is the one gamepad whose keys it holds. A keyboard slot
		//is not a gamepad, and a slot that names two pads is not an assignment
		//this sheet made - both read as no device rather than a guess.
		[Theory]
		[InlineData(new ushort[] { }, null)]
		[InlineData(new ushort[] { 0x10, 0x11 }, null)]                       //keyboard only
		[InlineData(new ushort[] { 0x1000 + 2 * 0x100 + 3 }, 2)]              //one pad
		[InlineData(new ushort[] { 0x1000 + 1 * 0x100 + 4, 0x10 }, 1)]        //pad keys and a keyboard key
		public void A_slots_device_is_the_one_gamepad_it_holds(ushort[] slot, int? device)
		{
			Assert.Equal(device, ControllerSheetPorts.SlotDevice(slot));
		}

		[Fact]
		public void A_slot_that_names_two_pads_has_no_device()
		{
			Assert.Null(ControllerSheetPorts.SlotDevice(new[] { PadKey(0, 0), PadKey(1, 0) }));
		}

		//The port's device is its first slot that names one: W-P15 writes a whole
		//pad into one free slot, so a port has one device however many slots it
		//fills.
		[Fact]
		public void A_ports_device_is_the_one_its_first_named_slot_holds()
		{
			SheetPort port = Port("Port1", 1, System.Array.Empty<ushort>(), new[] { PadKey(3, 0) });
			Assert.Equal(3, ControllerSheetPorts.PortDevice(port));
			Assert.Null(ControllerSheetPorts.PortDevice(Port("Port2", 2, new[] { KeyboardKey })));
		}

		[Fact]
		public void The_first_free_slot_is_the_first_that_binds_nothing()
		{
			Assert.Equal(0, ControllerSheetPorts.FreeSlot(Port("Port1", 1, System.Array.Empty<ushort>())));
			Assert.Equal(2, ControllerSheetPorts.FreeSlot(Port("Port1", 1, new[] { PadKey(0, 0) }, new[] { PadKey(0, 1) })));
			Assert.Null(ControllerSheetPorts.FreeSlot(Port("Port1", 1,
				new[] { PadKey(0, 0) }, new[] { PadKey(0, 1) }, new[] { PadKey(0, 2) }, new[] { PadKey(0, 3) })));
		}

		[Fact]
		public void Assignment_moves_a_device_between_ports_and_says_where_to()
		{
			SheetPort p1 = Port("Port1", 1, new[] { PadKey(1, 0) });
			SheetPort p2 = Port("Port2", 2);

			//The keys live in Port1's slot 0; the move writes Port2's first free
			//slot (0) and clears Port1's slot 0.
			PortMove move = ControllerSheetPorts.PlanMove(new[] { p1, p2 }, 1, 1);
			Assert.Equal(PortMoveOutcome.Moved, move.Outcome);
			Assert.Equal(0, move.TargetSlot);
			Assert.Equal(0, move.SourcePort);
			Assert.Equal(0, move.SourceSlot);
		}

		[Fact]
		public void A_device_already_on_the_port_is_not_moved_and_a_port_with_no_free_slot_refuses()
		{
			SheetPort p1 = Port("Port1", 1, new[] { PadKey(1, 0) });
			Assert.Equal(PortMoveOutcome.AlreadyThere, ControllerSheetPorts.PlanMove(new[] { p1 }, 1, 0).Outcome);

			SheetPort full = Port("Port2", 2,
				new[] { PadKey(2, 0) }, new[] { PadKey(2, 1) }, new[] { PadKey(2, 2) }, new[] { PadKey(2, 3) });
			Assert.Equal(PortMoveOutcome.NoFreeSlot, ControllerSheetPorts.PlanMove(new[] { p1, full }, 1, 1).Outcome);
		}

		//A pad nothing has bound yet has no keys to move: the sheet says so
		//instead of writing an empty slot over the player's port.
		[Fact]
		public void A_device_no_mapping_holds_has_nothing_to_move()
		{
			PortMove move = ControllerSheetPorts.PlanMove(new[] { Port("Port1", 1), Port("Port2", 2) }, 4, 1);
			Assert.Equal(PortMoveOutcome.NotBound, move.Outcome);
		}

		//What the keyboard does, read off the same ports: the keys that are not a
		//gamepad's, in slot order.
		[Fact]
		public void The_keyboards_keys_are_the_players_own()
		{
			SheetPort p1 = Port("Port1", 1, new ushort[] { KeyboardKey, PadKey(0, 0) }, new[] { (ushort)0x11 });
			SheetPort p2 = Port("Port2", 2, new[] { new ushort[] { (ushort)0x12 } });
			Assert.Equal(new ushort[] { 0x10, 0x11 }, ControllerSheetKeyboard.Keys(new[] { p1, p2 }));
		}

		[Fact]
		public void Nothing_bound_is_when_every_slot_of_every_port_is_empty()
		{
			Assert.True(ControllerSheetKeyboard.NothingBound(new[] { Port("Port1", 1), Port("Port2", 2) }));
			Assert.False(ControllerSheetKeyboard.NothingBound(new[] { Port("Port1", 1), Port("Port2", 2, new[] { PadKey(0, 0) }) }));
			Assert.False(ControllerSheetKeyboard.NothingBound(new[] { Port("Port1", 1, new[] { new ushort[] { KeyboardKey } }) }));
		}
	}
}
