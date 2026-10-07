using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#965 (ADR-0255 slice 3): the port light reads the binding of the slot the
	//selected pad holds, the same slot the rebind joins (TargetSlot's `padSlot`),
	//so the light and the write agree on which slot is the pad's. The first
	//non-zero field is only the answer for a pad the port holds in no slot.
	public class ControllerSheetRemapPadSlotTests
	{
		private static ushort PadKey(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

		//A keyboard key (Z = 0x5A on the Windows table; any code below the pad
		//block is a keyboard code).
		private const ushort KeyboardZ = 0x5A;

		[Fact]
		public void With_a_keyboard_in_slot_0_and_the_pad_in_slot_1_the_bound_code_is_slot_1s()
		{
			ushort padA = PadKey(0, 0);
			Assert.Equal(padA, ControllerSheetRemap.BoundCode(new ushort[] { KeyboardZ, padA, 0, 0 }, padSlot: 1));
		}

		//The pad's slot binds nothing for this control: the row is unbound for the
		//pad, rather than borrowing the keyboard's key from another slot.
		[Fact]
		public void A_control_the_pads_slot_leaves_unbound_reads_as_unbound()
		{
			Assert.Equal((ushort)0, ControllerSheetRemap.BoundCode(new ushort[] { KeyboardZ, 0, 0, 0 }, padSlot: 1));
		}

		[Fact]
		public void With_no_pad_slot_it_falls_back_to_the_first_non_zero_field()
		{
			ushort padA = PadKey(0, 0);
			Assert.Equal(padA, ControllerSheetRemap.BoundCode(new ushort[] { 0, padA, KeyboardZ, 0 }, padSlot: null));
			Assert.Equal((ushort)0, ControllerSheetRemap.BoundCode(new ushort[] { 0, 0, 0, 0 }, padSlot: null));
		}

		//A pad alone on the port, in slot 0: the answer the sheet always gave.
		[Fact]
		public void With_the_pad_only_the_bound_code_is_unchanged()
		{
			ushort padA = PadKey(0, 0);
			Assert.Equal(padA, ControllerSheetRemap.BoundCode(new ushort[] { padA, 0, 0, 0 }, padSlot: 0));
		}

		//A slot index outside the port is no slot at all.
		[Fact]
		public void An_out_of_range_pad_slot_falls_back_to_the_first_non_zero_field()
		{
			Assert.Equal(KeyboardZ, ControllerSheetRemap.BoundCode(new ushort[] { KeyboardZ, 0, 0, 0 }, padSlot: 4));
		}
	}
}
