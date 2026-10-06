using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#913 (the W-P15 prerequisite): on Windows the setup sheet and the sentence its
//confirmation shows named the pad by the key manager's own prefix ("PadN"), because
//the name the backend reports never reached the sheet. The window now hands the
//sheet the host's pad list (PlayControllerSetupViewModel.HostPads) and the pad is
//found by the block its keys carry, so a DirectInput joystick is named by its
//product name; a pad the backend cannot name keeps the key prefix.
//
//The real core is here for the mapping the flow writes: the sheet's Save reaches
//ConfigManager.ApplyConfig, which talks to it.
[Collection(NativeCoreCollection.Name)]
public class ControllerSetupNameTests
{
	[AvaloniaFact]
	public void A_named_joystick_names_the_sheet_and_its_confirmation()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const int device = 2;
		ushort Key(int button) => (ushort)(ControllerDevices.BaseDirectInputIndex + device * 0x100 + button);
		//What WindowsKeyManager::GetGamepadInfo reports for a joystick: the family
		//it is enumerated in and the slot a mapping's key codes carry, plus the
		//product name DirectInputManager::GetName reads off the device's descriptor.
		List<HostPad> pads = new() {
			new(ControllerDevices.PadBlock(GamepadBackend.DirectInput, device), "8BitDo SN30")
		};

		(string title, string? result, ushort bound) = RunSetup(pads, Key, "Joy3");

		Assert.Equal("Set up “8BitDo SN30”", title);
		Assert.Equal("8BitDo SN30 is set up.", result);
		Assert.Equal(Key(20), bound);
	}

	[AvaloniaFact]
	public void A_pad_the_backend_cannot_name_keeps_the_key_prefix()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		const int device = 2;
		ushort Key(int button) => (ushort)(ControllerDevices.BaseDirectInputIndex + device * 0x100 + button);
		//A joystick whose descriptor carries no product name: what the sheet falls
		//back to is the key manager's own device prefix, as before #913.
		List<HostPad> pads = new() {
			new(ControllerDevices.PadBlock(GamepadBackend.DirectInput, device), "")
		};

		(string title, string? result, ushort bound) = RunSetup(pads, Key, "Joy3");

		Assert.Equal("Set up “Joy3”", title);
		Assert.Equal("Joy3 is set up.", result);
		Assert.Equal(Key(20), bound);
	}

	//W-P15's flow, driven exactly as the window drives it: the pill on the pad's
	//first press, Start opens the sheet, and one press/release per step. Returns
	//the sheet's title, the sentence it finished with and the key written to the
	//free slot, so the caller can say what the pad was named.
	private static (string Title, string? Result, ushort Bound) RunSetup(List<HostPad> pads, Func<int, ushort> key, string prefix)
	{
		NesControllerConfig port = ConfigManager.Config.Nes.Port1;
		NesKeyMapping[] saved = { port.Mapping1, port.Mapping2, port.Mapping3, port.Mapping4 };
		//Slots 1-3 bound, slot 4 free: the setup writes the free one, whichever it is.
		port.Mapping1 = new NesKeyMapping() { A = 0x0101 };
		port.Mapping2 = new NesKeyMapping() { TurboA = 0x0102 };
		port.Mapping3 = new NesKeyMapping() { GenericKey1 = 0x0103 };
		port.Mapping4 = new NesKeyMapping();

		PlayControllerSetupViewModel setup = new() {
			//The key manager names the pad's keys after the device it numbered, which
			//is what the sheet falls back to when the host has no product name.
			KeyName = k => k == key(9) ? prefix + " Start" : prefix + " But" + (k & 0xFF),
			HostPads = () => pads,
			CurrentConsole = () => ConsoleType.Nes,
			IsPaused = () => false,
			Pause = () => { },
			Resume = () => { }
		};
		string? result = null;
		setup.Finished += r => result = r;
		try {
			TimeSpan t = setup.Now;
			ushort[] none = Array.Empty<ushort>();
			setup.Tick(new[] { key(1) }, t);
			Assert.True(setup.IsPillVisible);
			setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			setup.Tick(new[] { key(9) }, t += TimeSpan.FromMilliseconds(100));
			Assert.True(setup.IsVisible, "the sheet did not open on Start");
			string title = setup.Title;
			setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			for(int step = 0; step < 8; step++) {
				setup.Tick(new[] { key(20 + step) }, t += TimeSpan.FromMilliseconds(100));
				setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			}
			Assert.False(setup.IsVisible, "the sheet stayed open after every step");
			Assert.Equal(key(20), port.Mapping4.A);
			return (title, result, port.Mapping4.A);
		} finally {
			port.Mapping1 = saved[0];
			port.Mapping2 = saved[1];
			port.Mapping3 = saved[2];
			port.Mapping4 = saved[3];
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
		}
	}
}
