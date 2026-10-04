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
			//These tests use fixed keys only, so the slots and the keyboard slots
			//are the same: Slots carries the port type's custom keys too (the
			//overload below builds a port where the two differ).
			return new SheetPort(key, $"Player {player}", player - 1, four, four);
		}

		private static SheetPort PortWithCustomKeys(string key, int player, ushort[] all, ushort[] keyboard)
		{
			ushort[][] slots = { all, System.Array.Empty<ushort>(), System.Array.Empty<ushort>(), System.Array.Empty<ushort>() };
			ushort[][] keyboardSlots = { keyboard, System.Array.Empty<ushort>(), System.Array.Empty<ushort>(), System.Array.Empty<ushort>() };
			return new SheetPort(key, $"Player {player}", player - 1, slots, keyboardSlots);
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

		//A slot's device is the key-code block of the one gamepad whose keys it
		//holds: the family base plus the device index (base + device*0x100), which
		//is the same value the host's own (Backend, Slot) builds. A keyboard slot
		//is not a gamepad, and a slot that names two pads is not an assignment this
		//sheet made - both read as no device rather than a guess.
		[Theory]
		[InlineData(new ushort[] { }, null)]
		[InlineData(new ushort[] { 0x10, 0x11 }, null)]                              //keyboard only
		[InlineData(new ushort[] { 0x1000 + 2 * 0x100 + 3 }, 0x1200)]                //one pad
		[InlineData(new ushort[] { 0x1000 + 1 * 0x100 + 4, 0x10 }, 0x1100)]          //pad keys and a keyboard key
		public void A_slots_device_is_the_one_gamepad_block_it_holds(ushort[] slot, int? block)
		{
			Assert.Equal(block, ControllerSheetPorts.SlotDevice(slot));
		}

		[Fact]
		public void A_slot_that_names_two_pads_has_no_device()
		{
			Assert.Null(ControllerSheetPorts.SlotDevice(new[] { PadKey(0, 0), PadKey(1, 0) }));
		}

		//The block carries the family the keys were written in: a DirectInput pad
		//(0x2000) is not read as the XInput pad of the same ordinal (0x1000), which
		//is exactly the coincidence #813's family-relative Slot exists to keep.
		[Fact]
		public void A_slots_block_carries_the_key_codes_own_family()
		{
			Assert.Equal(0x1000 + 2 * 0x100, ControllerSheetPorts.SlotDevice(new[] { (ushort)(0x1000 + 2 * 0x100 + 3) }));
			Assert.Equal(0x2000, ControllerSheetPorts.SlotDevice(new[] { (ushort)(0x2000 + 3) }));
		}

		//The port's device is its first slot that names one: W-P15 writes a whole
		//pad into one free slot, so a port has one device however many slots it
		//fills.
		[Fact]
		public void A_ports_device_is_the_one_its_first_named_slot_holds()
		{
			SheetPort port = Port("Port1", 1, System.Array.Empty<ushort>(), new[] { PadKey(3, 0) });
			Assert.Equal(0x1300, ControllerSheetPorts.PortDevice(port));
			Assert.Null(ControllerSheetPorts.PortDevice(Port("Port2", 2, new[] { KeyboardKey })));
		}

		//#813 / ADR-0255 slice 2: a pad is named by its backend's family plus its
		//family-relative slot, which is exactly the block its key codes carry - never
		//the host's enumeration ordinal (global on Windows). Windows has two families,
		//so XInput's device 2 and DirectInput's device 2 are different blocks.
		[Fact]
		public void A_pads_block_is_its_backend_family_plus_its_family_slot()
		{
			Assert.Equal(0x1000, ControllerDevices.FamilyOf(GamepadBackend.XInput));
			Assert.Equal(0x1000, ControllerDevices.FamilyOf(GamepadBackend.Evdev));
			Assert.Equal(0x1000, ControllerDevices.FamilyOf(GamepadBackend.GameController));
			Assert.Equal(0x2000, ControllerDevices.FamilyOf(GamepadBackend.DirectInput));

			Assert.Equal(0x1200, ControllerDevices.PadBlock(GamepadBackend.XInput, 2));
			Assert.Equal(0x2000, ControllerDevices.PadBlock(GamepadBackend.DirectInput, 0));
			Assert.NotEqual(ControllerDevices.PadBlock(GamepadBackend.XInput, 2), ControllerDevices.PadBlock(GamepadBackend.DirectInput, 2));

			//The block a pad builds is the block its keys carry, and a keyboard key
			//has no block at all.
			Assert.Equal(ControllerDevices.PadBlock(GamepadBackend.XInput, 3), ControllerDevices.KeyBlock((ushort)(0x1000 + 3 * 0x100 + 7)));
			Assert.Equal(ControllerDevices.PadBlock(GamepadBackend.DirectInput, 0), ControllerDevices.KeyBlock((ushort)(0x2000 + 5)));
			Assert.Null(ControllerDevices.KeyBlock(KeyboardKey));

			//The collision the block cannot resolve: a base-family pad at device 16
			//addresses 0x2000, which is DirectInput device 0's block, and KeyBlock
			//reads the same 0x2000 off the code. A block is a within-host key-code
			//address, not an identity - safe only because no host runs both backends
			//(No_host_runs_two_backends_in_the_same_family pins that).
			Assert.Equal(ControllerDevices.PadBlock(GamepadBackend.DirectInput, 0), ControllerDevices.PadBlock(GamepadBackend.Evdev, 16));
			Assert.Equal(ControllerDevices.PadBlock(GamepadBackend.Evdev, 16), ControllerDevices.KeyBlock((ushort)0x2000));
		}

		//The one assumption keeping a key-code block unambiguous between backends:
		//no host runs two backends that number their pads in the same family. This
		//fails the day a base-family backend is added to Windows (where XInput
		//already lives), which is exactly when the block collision above starts to
		//bite. FamilyOf's own comment points here.
		[Fact]
		public void No_host_runs_two_backends_in_the_same_family()
		{
			GamepadBackend[] backends = System.Enum.GetValues<GamepadBackend>();
			foreach(GamepadBackend a in backends) {
				if(a == GamepadBackend.None) {
					continue;
				}
				foreach(GamepadBackend b in backends) {
					if(b == GamepadBackend.None || a == b || ControllerDevices.HostOf(a) != ControllerDevices.HostOf(b)) {
						continue;
					}
					Assert.NotEqual(ControllerDevices.FamilyOf(a), ControllerDevices.FamilyOf(b));
				}
			}
			//Non-vacuous: Windows really runs two backends, on two families - so the
			//pairwise check above has a same-host pair to examine.
			Assert.Equal(ControllerDevices.HostOf(GamepadBackend.XInput), ControllerDevices.HostOf(GamepadBackend.DirectInput));
			Assert.NotEqual(ControllerDevices.FamilyOf(GamepadBackend.XInput), ControllerDevices.FamilyOf(GamepadBackend.DirectInput));
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
			PortMove move = ControllerSheetPorts.PlanMove(new[] { p1, p2 }, 0x1100, 1);
			Assert.Equal(PortMoveOutcome.Moved, move.Outcome);
			SlotMove slot = Assert.Single(move.Slots);
			Assert.Equal(0, slot.SourcePort);
			Assert.Equal(0, slot.SourceSlot);
			Assert.Equal(0, slot.TargetSlot);
		}

		//ADR-0255 slice 2: a device's keys move as one - a pad bound in two slots of
		//a port must not leave one behind and end up playing as two players. The plan
		//names every slot, each to its own free slot of the target.
		[Fact]
		public void Assignment_moves_every_slot_the_device_holds()
		{
			SheetPort p1 = Port("Port1", 1, new[] { PadKey(1, 0) }, System.Array.Empty<ushort>(), new[] { PadKey(1, 7) });
			SheetPort p2 = Port("Port2", 2);

			PortMove move = ControllerSheetPorts.PlanMove(new[] { p1, p2 }, 0x1100, 1);
			Assert.Equal(PortMoveOutcome.Moved, move.Outcome);
			Assert.Equal(2, move.Slots.Count);
			Assert.Contains(move.Slots, s => s.SourceSlot == 0 && s.TargetSlot == 0);
			Assert.Contains(move.Slots, s => s.SourceSlot == 2 && s.TargetSlot == 1);
		}

		[Fact]
		public void A_device_already_on_the_port_is_not_moved_and_a_port_with_no_free_slot_refuses()
		{
			SheetPort p1 = Port("Port1", 1, new[] { PadKey(1, 0) });
			Assert.Equal(PortMoveOutcome.AlreadyThere, ControllerSheetPorts.PlanMove(new[] { p1 }, 0x1100, 0).Outcome);

			SheetPort full = Port("Port2", 2,
				new[] { PadKey(2, 0) }, new[] { PadKey(2, 1) }, new[] { PadKey(2, 2) }, new[] { PadKey(2, 3) });
			Assert.Equal(PortMoveOutcome.NoFreeSlot, ControllerSheetPorts.PlanMove(new[] { p1, full }, 0x1100, 1).Outcome);
		}

		//A pad whose keys need more free slots than the target has is refused whole,
		//not moved in part: the plan is all-or-nothing so the device cannot split.
		[Fact]
		public void A_target_with_too_few_free_slots_for_every_slot_refuses_the_whole_move()
		{
			SheetPort p1 = Port("Port1", 1, new[] { PadKey(1, 0) }, System.Array.Empty<ushort>(), new[] { PadKey(1, 7) });
			//Port2 has one free slot (3) and the rest taken.
			SheetPort p2 = Port("Port2", 2, new[] { PadKey(2, 0) }, new[] { PadKey(2, 1) }, new[] { PadKey(2, 2) });
			Assert.Equal(PortMoveOutcome.NoFreeSlot, ControllerSheetPorts.PlanMove(new[] { p1, p2 }, 0x1100, 1).Outcome);
		}

		//A pad nothing has bound yet has no keys to move: the sheet says so
		//instead of writing an empty slot over the player's port.
		[Fact]
		public void A_device_no_mapping_holds_has_nothing_to_move()
		{
			PortMove move = ControllerSheetPorts.PlanMove(new[] { Port("Port1", 1), Port("Port2", 2) }, 0x1400, 1);
			Assert.Equal(PortMoveOutcome.NotBound, move.Outcome);
			Assert.Empty(move.Slots);
		}

		//"Already there" is about the whole target, not just the first slot that
		//names a device: a pad whose keys sit in a later slot of the target is
		//bound to that very player even when an earlier slot of the same port
		//holds another device. PortDevice's first-named-slot answer misses it, and
		//answering NotBound would tell the player to set up a pad already set up.
		[Fact]
		public void A_device_in_a_later_slot_of_the_target_is_already_there()
		{
			SheetPort target = Port("Port2", 2, new[] { PadKey(2, 0) }, new[] { PadKey(1, 0) });
			PortMove move = ControllerSheetPorts.PlanMove(new[] { target }, 0x1100, 0);
			Assert.Equal(PortMoveOutcome.AlreadyThere, move.Outcome);
			Assert.Empty(move.Slots);
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

		//Finding 1: the keyboard line reads the fixed fields (KeyboardSlots), never
		//the port type's custom keys (Slots, which the free-slot rule reads). A
		//Zapper's mouse buttons (0x200) are that device's buttons, so a port that
		//binds only them reports no keyboard keys - even though the slot is taken.
		[Fact]
		public void The_keyboard_line_ignores_a_ports_custom_keys()
		{
			SheetPort zapper = PortWithCustomKeys("Port2", 2, all: new ushort[] { 0x200, 0x201 }, keyboard: System.Array.Empty<ushort>());
			Assert.Empty(ControllerSheetKeyboard.Keys(new[] { zapper }));
			//The slot is still taken: the free-slot rule reads Slots, which has them.
			Assert.NotEmpty(zapper.Slots[0]);
		}
	}
}
