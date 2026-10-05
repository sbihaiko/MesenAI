using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0255 slice 3 (W-P17 REMAP): the host-free half of "remapping, as a mode
	//of the same sheet". Which controls a console offers a row, where a rebind
	//lands, the pad button a bound code names, the row's two lights, and the
	//capture's own state machine - the last of which is where the two rules this
	//slice adds live: the press that opened the capture must be released first,
	//and the pad's navigation controls are refused.
	public class ControllerSheetRemapTests
	{
		private static ushort PadKey(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

		private const int Device0Block = ControllerDevices.BaseGamepadIndex;
		private const int Device1Block = ControllerDevices.BaseGamepadIndex + 0x100;

		//The rows are the console's own controls - the same list W-P15's setup
		//walks, so the two surfaces can bind the same things and nothing else.
		[Theory]
		[InlineData(ConsoleType.Nes, SetupConsole.Nes)]
		[InlineData(ConsoleType.Gameboy, SetupConsole.GameBoy)]
		[InlineData(ConsoleType.Sms, SetupConsole.MasterSystem)]
		[InlineData(ConsoleType.Gba, SetupConsole.Gba)]
		public void The_rows_are_the_consoles_own_controls(ConsoleType console, SetupConsole setup)
		{
			Assert.Equal(ControllerSetupSteps.For(setup), ControllerSheetRemap.Controls(console));
		}

		[Fact]
		public void A_console_with_no_player_port_has_no_rows()
		{
			Assert.Empty(ControllerSheetRemap.Controls(ConsoleType.Snes));
			Assert.Empty(ControllerSheetRemap.Controls(ConsoleType.PcEngine));
		}

		//Where a rebind lands: the slot that already binds the control, so a
		//rebind replaces its own binding; else the first slot that binds nothing at
		//all; else nothing, and the sheet refuses rather than overwriting a slot.
		[Fact]
		public void A_rebind_lands_in_the_slot_that_already_binds_the_control()
		{
			ushort code = PadKey(0, 0);
			Assert.Equal(1, ControllerSheetRemap.TargetSlot(new ushort[] { 0, code, 0, 0 }, new[] { true, true, true, true }));
		}

		[Fact]
		public void An_unbound_control_lands_in_the_first_free_slot()
		{
			Assert.Equal(2, ControllerSheetRemap.TargetSlot(new ushort[] { 0, 0, 0, 0 }, new[] { true, true, false, false }));
		}

		[Fact]
		public void A_port_with_no_free_slot_refuses()
		{
			Assert.Null(ControllerSheetRemap.TargetSlot(new ushort[] { 0, 0, 0, 0 }, new[] { true, true, true, true }));
		}

		//The pad's own button a bound code names, read off the core's name for that
		//button (the per-backend order slice 1 reads). XInput's A is bit 12 where
		//macOS's is bit 0, and evdev has no D-pad at all - the same three tables the
		//drawn pad reads, never a second one.
		[Theory]
		[InlineData("Pad1 A", GamepadBackend.GameController, 0)]
		[InlineData("Pad1 Select", GamepadBackend.GameController, 7)]
		[InlineData("Pad2 Start", GamepadBackend.GameController, 6)]
		[InlineData("Pad1 A", GamepadBackend.XInput, 12)]
		[InlineData("Pad1 Up", GamepadBackend.XInput, 0)]
		[InlineData("Pad1 Up", GamepadBackend.Evdev, null)]   //evdev's D-pad is axes, not a button
		[InlineData("Joy1 But1", GamepadBackend.DirectInput, null)] //no console name in that family
		[InlineData("Space", GamepadBackend.GameController, null)]  //a keyboard key: no device prefix
		[InlineData("Pad1 ", GamepadBackend.GameController, null)]  //nothing after the device
		[InlineData("Pad1 Foo", GamepadBackend.GameController, null)]
		public void A_bound_code_names_the_pads_own_button(string keyName, GamepadBackend backend, int? bit)
		{
			Assert.Equal(bit, ControllerSheetRemap.ButtonBitOfCodeName(keyName, backend));
		}

		//The two lights, each a different reading of the same binding. The case the
		//ADR names - a row lit on the pad side and dark on the port side - is a
		//binding whose device index moved (slice 5's reconnect): the pad in hand
		//still presses the button the row names, while the port's stale code is
		//never held, so the console receives nothing.
		[Fact]
		public void A_correct_binding_lights_both_sides()
		{
			RemapLights lights = ControllerSheetRemap.Lights(PadKey(0, 0), 0, padButtonHeld: true, portCodeHeld: true);
			Assert.True(lights.PadLit);
			Assert.True(lights.PortLit);
		}

		[Fact]
		public void A_binding_whose_device_moved_lights_the_pad_and_not_the_port()
		{
			RemapLights lights = ControllerSheetRemap.Lights(PadKey(1, 0), 0, padButtonHeld: true, portCodeHeld: false);
			Assert.True(lights.PadLit);
			Assert.False(lights.PortLit);
		}

		[Fact]
		public void An_unbound_control_lights_nothing()
		{
			RemapLights lights = ControllerSheetRemap.Lights(0, null, padButtonHeld: true, portCodeHeld: true);
			Assert.False(lights.PadLit);
			Assert.False(lights.PortLit);
		}

		[Fact]
		public void A_binding_the_pads_table_cannot_name_stays_dark_on_the_pad_side()
		{
			RemapLights lights = ControllerSheetRemap.Lights(PadKey(0, 0), null, padButtonHeld: true, portCodeHeld: true);
			Assert.False(lights.PadLit);
			Assert.True(lights.PortLit);
		}

		//The capture. Arming waits for the pad to be let go of first, or the press
		//that picked the row (a pad Confirm) would bind itself; a press from another
		//pad binds nothing; and a navigation control is refused, visibly, with the
		//capture still armed.
		private static ControllerSheetCapture Armed(SetupButton button = SetupButton.A)
		{
			ControllerSheetCapture capture = new();
			capture.Arm(button);
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new ushort[] { PadKey(0, 0) }, Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, _ => false));
			return capture;
		}

		[Fact]
		public void The_press_that_armed_the_capture_must_be_released_first()
		{
			ControllerSheetCapture capture = new();
			capture.Arm(SetupButton.A);

			//The button that picked the row is still down: nothing armed yet, and
			//the same code held across the next tick is not a new press either.
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new[] { PadKey(0, 0) }, Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new[] { PadKey(0, 0) }, Device0Block, _ => false));
			Assert.True(capture.IsCapturing);

			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Bound, capture.OnTick(new[] { PadKey(0, 1) }, Device0Block, _ => false));
			Assert.Equal(PadKey(0, 1), capture.BoundCode);
			Assert.False(capture.IsCapturing);
		}

		[Fact]
		public void A_button_from_another_pad_is_not_a_press()
		{
			ControllerSheetCapture capture = Armed();
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new[] { PadKey(1, 2) }, Device0Block, _ => false));
			Assert.True(capture.IsCapturing);
		}

		[Fact]
		public void A_navigation_control_is_refused_and_the_capture_stays_armed()
		{
			ControllerSheetCapture capture = Armed();
			ushort confirm = PadKey(0, 12);
			Assert.Equal(CaptureOutcome.Refused, capture.OnTick(new[] { confirm }, Device0Block, code => code == confirm));
			Assert.True(capture.IsCapturing);

			//Still listening: another control binds as usual.
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, code => code == confirm));
			Assert.Equal(CaptureOutcome.Bound, capture.OnTick(new[] { PadKey(0, 1) }, Device0Block, code => code == confirm));
		}

		[Fact]
		public void Two_buttons_in_one_tick_bind_the_lowest_code()
		{
			ControllerSheetCapture capture = Armed();
			Assert.Equal(CaptureOutcome.Bound, capture.OnTick(new[] { PadKey(0, 5), PadKey(0, 1) }, Device0Block, _ => false));
			Assert.Equal(PadKey(0, 1), capture.BoundCode);
		}

		[Fact]
		public void Cancelling_stops_the_capture()
		{
			ControllerSheetCapture capture = Armed();
			capture.Cancel();
			Assert.False(capture.IsCapturing);
			Assert.Equal(CaptureOutcome.None, capture.OnTick(new[] { PadKey(0, 1) }, Device0Block, _ => false));
		}

		[Fact]
		public void A_cancelled_capture_does_not_rebind_without_arming_again()
		{
			ControllerSheetCapture capture = Armed();
			capture.Cancel();
			capture.Arm(SetupButton.B);
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new[] { PadKey(0, 1) }, Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Bound, capture.OnTick(new[] { PadKey(0, 1) }, Device0Block, _ => false));
			Assert.Equal(SetupButton.B, capture.Button);
		}
	}
}
