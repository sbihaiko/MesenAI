using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#965 (ADR-0255 slice 3): on a port holding a keyboard in slot 0 and the pad in
//slot 1, a press of the pad's A lights both of the A row's lights. The port light
//used to read the first non-zero field across the four slots - the keyboard's
//key, which the pad never holds - so the row lit "the pad sends" and stayed dark
//on "the port receives" although the port did receive the press. The rule is
//host-free (UI.Tests/Play/ControllerSheetRemapPadSlotTests); this is the sheet
//feeding it the slot the selected pad holds, with a synthetic pad.
[Collection(NativeCoreCollection.Name)]
public class ControllerSheetPadSlotLampTests
{
	private static ushort PadButton(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

	//A keyboard key code (below the pad block).
	private const ushort KeyboardZ = 0x5A;

	private static ControllerSheetViewModel Sheet()
	{
		ControllerSheetViewModel sheet = new();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		//The pad's own codes as its backend names them ("Pad1 A" is device 0's
		//button 0), from the tester's per-backend table, so the sheet's button
		//lookup reads back the name it was given; anything else is a key.
		string[] names = ControllerLivePad.NameList(GamepadBackend.GameController);
		sheet.KeyName = key => key >= ControllerDevices.BaseGamepadIndex
			? "Pad" + (((key - ControllerDevices.BaseGamepadIndex) >> 8) + 1) + " " + names[key & 0xFF]
			: "Z";
		return sheet;
	}

	[AvaloniaFact]
	public void A_pad_press_on_a_mixed_keyboard_and_pad_port_lights_both_lights()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		NesConfig saved = ConfigManager.Config.Nes.Clone();
		ControllerSheetViewModel sheet = Sheet();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			//Slot 0 is a keyboard, slot 1 the pad - both bind the console's A.
			ConfigManager.Config.Nes.Port1.Mapping1.A = KeyboardZ;
			ConfigManager.Config.Nes.Port1.Mapping2.A = PadButton(0, 0);
			ConfigManager.Config.Nes.Port1.Mapping2.B = PadButton(0, 1);

			GamepadTestItem pad = new(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController };
			sheet.Tester.Gamepads.Add(pad);
			sheet.SelectedPadIndex = 0;
			pad.Buttons[0].IsPressed = true;
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 0) };
			sheet.ApplyPad();

			Assert.True(sheet.ShowRemap);
			ControllerSheetRemapRow a = sheet.RemapRows[0];
			Assert.Equal("A", a.Label);
			Assert.True(a.PadLit);
			Assert.True(a.PortLit);
			//The row names the pad's binding, the one the press reaches the port by.
			Assert.Equal("Pad1 A", a.BoundName);
		} finally {
			sheet.Dispose();
			ConfigManager.Config.Nes = saved;
		}
	}

	//Pad only: the port light still reads the pad's own binding, as before.
	[AvaloniaFact]
	public void A_pad_press_on_a_pad_only_port_still_lights_both_lights()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		NesConfig saved = ConfigManager.Config.Nes.Clone();
		ControllerSheetViewModel sheet = Sheet();
		try {
			ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
			ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(0, 0);

			GamepadTestItem pad = new(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController };
			sheet.Tester.Gamepads.Add(pad);
			sheet.SelectedPadIndex = 0;
			pad.Buttons[0].IsPressed = true;
			sheet.PressedKeys = () => new ushort[] { PadButton(0, 0) };
			sheet.ApplyPad();

			Assert.True(sheet.RemapRows[0].PadLit);
			Assert.True(sheet.RemapRows[0].PortLit);
		} finally {
			sheet.Dispose();
			ConfigManager.Config.Nes = saved;
		}
	}
}
