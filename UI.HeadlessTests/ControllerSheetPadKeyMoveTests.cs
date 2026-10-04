using Mesen.Config;
using Mesen.ViewModels;
using System;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 2, the write half of the PLAYERS assignment: a move takes the
//whole slot, so the port type's own custom-button array travels with the fixed
//KeyMapping fields. The read side (ControllerSheetViewModel.SlotKeys) folds
//KeyMapping.ToInterop's CustomKeys into the keys it matches a device on, so a
//write that touched only the fixed fields reported a device "assigned" while its
//custom-bound keys stayed on the source port - the same pad bound in two ports at
//once, which ADR-0255's all-slots-together move forbids. A clear that touched
//only the fixed fields left the slot it had just reported as free still naming
//the old device.
//
//These are not UI.Tests (host-free) cases: NesControllerConfig/NesKeyMapping and
//SmsControllerConfig/SmsKeyMapping live in Mesen.Config, which UI.Tests does not
//dual-compile. Nothing here needs a running app or the native core - the slot
//writer is a plain static on the app assembly - so these are plain Facts, not
//AvaloniaFacts.
public class ControllerSheetPadKeyMoveTests
{
	private static ushort PadKey(int device, int button) => (ushort)(0x1000 + device * 0x100 + button);

	//A Zapper is the narrowest real port type whose device keys can live only in a
	//custom-button array (its Fire/AimOffscreen buttons bound to a pad).
	private static NesControllerConfig ZapperPort() => new() { Type = ControllerType.NesZapper };

	[Fact]
	public void A_pad_bound_only_in_a_ports_custom_keys_moves_with_them()
	{
		NesControllerConfig port1 = ZapperPort();
		NesControllerConfig port2 = ZapperPort();
		//The pad's only binding lives in the port type's custom-button array.
		port1.Mapping1.ZapperButtons = new ushort[] { PadKey(1, 0) };

		ControllerSheetSlotWrite.MoveSlot(port1, 0, port2, 1);

		//The target slot now holds the pad's key...
		Assert.NotNull(port2.Mapping2.ZapperButtons);
		Assert.Contains(PadKey(1, 0), port2.Mapping2.ZapperButtons!);
		//...and the source no longer does.
		Assert.DoesNotContain(PadKey(1, 0), port1.Mapping1.ZapperButtons ?? Array.Empty<ushort>());
	}

	//A slot whose keys are split between the fixed fields and the custom array must
	//move as one: the pad is not two bindings.
	[Fact]
	public void A_slot_with_fixed_and_custom_keys_moves_both_together()
	{
		NesControllerConfig port1 = ZapperPort();
		NesControllerConfig port2 = ZapperPort();
		port1.Mapping1.ZapperButtons = new ushort[] { PadKey(1, 0) };
		port1.Mapping1.Start = 0x30; //a keyboard key, in a fixed field

		ControllerSheetSlotWrite.MoveSlot(port1, 0, port2, 2);

		Assert.Contains(PadKey(1, 0), port2.Mapping3.ZapperButtons!);
		Assert.Equal(0x30, port2.Mapping3.Start);
		//The source slot is left empty, custom keys included.
		Assert.Equal(0, port1.Mapping1.Start);
		Assert.DoesNotContain(PadKey(1, 0), port1.Mapping1.ZapperButtons ?? Array.Empty<ushort>());
	}

	//The other real type with custom buttons: the SMS light phaser.
	[Fact]
	public void An_SMS_light_phasers_custom_keys_move_with_the_slot()
	{
		SmsControllerConfig port1 = new() { Type = ControllerType.SmsLightPhaser };
		SmsControllerConfig port2 = new() { Type = ControllerType.SmsLightPhaser };
		port1.Mapping1.LightPhaserButtons = new ushort[] { PadKey(1, 1) };

		ControllerSheetSlotWrite.MoveSlot(port1, 0, port2, 0);

		Assert.NotNull(port2.Mapping1.LightPhaserButtons);
		Assert.Contains(PadKey(1, 1), port2.Mapping1.LightPhaserButtons!);
		Assert.DoesNotContain(PadKey(1, 1), port1.Mapping1.LightPhaserButtons ?? Array.Empty<ushort>());
	}
}
