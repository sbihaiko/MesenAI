using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.5 (PRD Part B §13.5.2 W-P15): a controller nobody has set up - the
	//detection rule, the pad-driven setup steps and the slot it is written to.
	public class ControllerSetupTests
	{
		private const ushort Pad1A = 0x1000;
		private const ushort Pad1Start = 0x1007;
		private const ushort Pad2Button3 = 0x1103;
		private const ushort Pad2Button9 = 0x1109;
		private const ushort Pad2Button1 = 0x1101;
		private const ushort KeyboardS = 0x0053;

		private static readonly ushort[] Mapped = { Pad1A, Pad1Start, KeyboardS };

		private static bool NoStart(ushort key) => false;
		private static bool StartIs9(ushort key) => key == Pad2Button9;

		[Fact]
		public void Device_comes_from_the_key_code()
		{
			Assert.Null(ControllerDevices.DeviceOf(KeyboardS));
			Assert.Equal(0, ControllerDevices.DeviceOf(Pad1A));
			Assert.Equal(1, ControllerDevices.DeviceOf(Pad2Button3));
		}

		[Fact]
		public void A_device_with_any_mapped_key_is_known()
		{
			Assert.False(ControllerDevices.IsUnknown(0, Mapped));
			Assert.True(ControllerDevices.IsUnknown(1, Mapped));
		}

		[Fact]
		public void First_press_on_an_unknown_device_shows_the_pill_once_per_session()
		{
			UnknownControllerDetector d = new();
			Assert.Equal(DetectorEvent.ShowPill, d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.Zero));
			Assert.Equal(1, d.PillDevice);
			Assert.Equal(DetectorEvent.DismissPill, d.OnPressed(new[] { KeyboardS }, Mapped, NoStart, TimeSpan.FromSeconds(1)));
			//Released, then pressed again: never asked again this session.
			Assert.Equal(DetectorEvent.None, d.OnPressed(Array.Empty<ushort>(), Mapped, NoStart, TimeSpan.FromSeconds(2)));
			Assert.Equal(DetectorEvent.None, d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.FromSeconds(3)));
		}

		[Fact]
		public void Known_devices_and_the_keyboard_never_show_the_pill()
		{
			UnknownControllerDetector d = new();
			Assert.Equal(DetectorEvent.None, d.OnPressed(new[] { Pad1A, KeyboardS }, Mapped, NoStart, TimeSpan.Zero));
		}

		[Fact]
		public void Start_on_that_pad_opens_the_sheet()
		{
			UnknownControllerDetector d = new();
			d.OnPressed(new[] { Pad2Button3 }, Mapped, StartIs9, TimeSpan.Zero);
			d.OnPressed(Array.Empty<ushort>(), Mapped, StartIs9, TimeSpan.FromSeconds(0.5));
			Assert.Equal(DetectorEvent.OpenSheet, d.OnPressed(new[] { Pad2Button9 }, Mapped, StartIs9, TimeSpan.FromSeconds(1)));
			Assert.Equal(1, d.SheetDevice);
		}

		[Fact]
		public void A_pad_that_names_no_start_opens_with_its_first_button_again()
		{
			UnknownControllerDetector d = new();
			d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.Zero);
			d.OnPressed(Array.Empty<ushort>(), Mapped, NoStart, TimeSpan.FromSeconds(0.5));
			Assert.Equal(DetectorEvent.OpenSheet, d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.FromSeconds(1)));
		}

		[Fact]
		public void Another_button_on_that_pad_dismisses_the_pill()
		{
			UnknownControllerDetector d = new();
			d.OnPressed(new[] { Pad2Button3 }, Mapped, StartIs9, TimeSpan.Zero);
			d.OnPressed(Array.Empty<ushort>(), Mapped, StartIs9, TimeSpan.FromSeconds(0.5));
			Assert.Equal(DetectorEvent.DismissPill, d.OnPressed(new[] { Pad2Button1 }, Mapped, StartIs9, TimeSpan.FromSeconds(1)));
		}

		[Fact]
		public void The_pill_goes_away_after_eight_seconds()
		{
			UnknownControllerDetector d = new();
			d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.Zero);
			Assert.Equal(DetectorEvent.None, d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.FromSeconds(7.9)));
			Assert.Equal(DetectorEvent.DismissPill, d.OnPressed(new[] { Pad2Button3 }, Mapped, NoStart, TimeSpan.FromSeconds(8)));
		}

		[Theory]
		[InlineData(SetupConsole.Nes, 8)]
		[InlineData(SetupConsole.GameBoy, 8)]
		[InlineData(SetupConsole.MasterSystem, 6)]
		[InlineData(SetupConsole.Gba, 10)]
		public void Step_counts_per_console(SetupConsole console, int count)
		{
			Assert.Equal(count, ControllerSetupSteps.For(console).Count);
		}

		[Fact]
		public void Nes_steps_are_in_the_wireframe_order()
		{
			Assert.Equal(new[] { SetupButton.A, SetupButton.B, SetupButton.Select, SetupButton.Start, SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right },
				ControllerSetupSteps.For(SetupConsole.Nes));
		}

		private static ControllerSetupSession NewSession() => new(1, ControllerSetupSteps.For(SetupConsole.Nes), TimeSpan.Zero);

		[Fact]
		public void The_button_that_opened_the_sheet_must_be_released_first()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(new[] { Pad2Button9 }, TimeSpan.FromSeconds(0.1));
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.2));
			Assert.Equal(0, s.StepIndex);
			Assert.Empty(s.Bindings);
		}

		[Fact]
		public void A_short_press_binds_the_step_and_moves_on()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.1));
			s.Tick(new[] { Pad2Button3 }, TimeSpan.FromSeconds(0.2));
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.4));
			Assert.Equal(1, s.StepIndex);
			Assert.Equal(Pad2Button3, s.Bindings[SetupButton.A]);
		}

		[Fact]
		public void Other_devices_do_not_drive_the_setup()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(new[] { KeyboardS, Pad1A }, TimeSpan.FromSeconds(0.2));
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.4));
			Assert.Equal(0, s.StepIndex);
		}

		[Fact]
		public void Holding_two_seconds_skips_the_step()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.1));
			s.Tick(new[] { Pad2Button3 }, TimeSpan.FromSeconds(0.2));
			s.Tick(new[] { Pad2Button3 }, TimeSpan.FromSeconds(2.2));
			Assert.Equal(1, s.StepIndex);
			Assert.False(s.Bindings.ContainsKey(SetupButton.A));
			//Still held: the next step waits for the release.
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(2.5));
			Assert.Equal(1, s.StepIndex);
		}

		[Fact]
		public void Ten_seconds_of_silence_cancel()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(9.9));
			Assert.Equal(SetupState.Running, s.State);
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(10));
			Assert.Equal(SetupState.Cancelled, s.State);
		}

		[Fact]
		public void Skip_and_cancel_work_for_mouse_and_keyboard_too()
		{
			ControllerSetupSession s = NewSession();
			s.Skip(TimeSpan.FromSeconds(1));
			Assert.Equal(1, s.StepIndex);
			s.Cancel();
			Assert.Equal(SetupState.Cancelled, s.State);
		}

		[Fact]
		public void Finishing_every_step_is_done()
		{
			ControllerSetupSession s = NewSession();
			double t = 0.1;
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(t));
			for(int i = 0; i < 8; i++) {
				ushort key = (ushort)(0x1100 + i);
				s.Tick(new[] { key }, TimeSpan.FromSeconds(t += 0.1));
				s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(t += 0.1));
			}
			Assert.Equal(SetupState.Done, s.State);
			Assert.Equal(8, s.Bindings.Count);
			Assert.Equal((ushort)0x1103, s.Bindings[SetupButton.Start]);
		}

		[Fact]
		public void One_button_never_binds_two_steps()
		{
			ControllerSetupSession s = NewSession();
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.1));
			s.Tick(new[] { Pad2Button3 }, TimeSpan.FromSeconds(0.2));
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.3));
			s.Tick(new[] { Pad2Button3 }, TimeSpan.FromSeconds(0.4));
			s.Tick(Array.Empty<ushort>(), TimeSpan.FromSeconds(0.5));
			Assert.Equal(1, s.StepIndex);
		}

		[Fact]
		public void The_mapping_goes_to_the_first_free_slot()
		{
			Assert.Equal(2, ControllerSetupSteps.FirstFreeSlot(new[] { true, true, false, false }));
			Assert.Equal(0, ControllerSetupSteps.FirstFreeSlot(new[] { false, true, true, true }));
			Assert.Null(ControllerSetupSteps.FirstFreeSlot(new[] { true, true, true, true }));
		}

		[Theory]
		[InlineData("Pad2 But3", "Pad2")]
		[InlineData("Joy1 Button 4", "Joy1")]
		[InlineData("Pad3", "Pad3")]
		[InlineData("", "")]
		public void Device_label_is_the_key_name_prefix(string keyName, string label)
		{
			Assert.Equal(label, ControllerDevices.Label(keyName));
		}

		[Theory]
		[InlineData("Pad2 Start", true)]
		[InlineData("Pad2 Menu", true)]
		[InlineData("Pad2 Options", true)]
		[InlineData("Pad2 But9", false)]
		public void Start_is_recognised_by_its_name(string keyName, bool isStart)
		{
			Assert.Equal(isStart, ControllerDevices.NamesStart(keyName));
		}
	}
}
