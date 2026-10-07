using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 5, the config half: a pad's keys move when its device index
//changes. The decision (which move, and which moves are safe) is host-free in
//UI/Logic/DeviceReconnect and pinned by UI.Tests/Play/DeviceReconnectTests; what
//only the app assembly can show is the walk over the real KeyMapping objects -
//the fixed fields, the custom-button arrays, the slice-4 spare pad controls and
//every console port - and that keys below the gamepad base (the keyboard) are
//left alone.
public class ControllerKeyMigrationTests
{
	private static ushort Key(int device, int button) => (ushort)(0x1000 + device * 0x100 + button);
	private static ushort DiKey(int device, int button) => (ushort)(0x2000 + device * 0x100 + button);

	private static readonly DeviceMove[] _none = System.Array.Empty<DeviceMove>();

	[Fact]
	public void Keys_bound_to_a_pads_device_index_move_with_it()
	{
		Configuration config = new();
		config.Nes.Port2.Mapping1.A = Key(1, 3);
		config.Nes.Port2.Mapping1.Start = Key(1, 6);
		//A different device index, in another port: not this pad's keys.
		config.Nes.Port1.Mapping1.B = Key(0, 1);
		//A keyboard key below the gamepad base has no device to move.
		config.Nes.Port1.Mapping1.Select = 0x0020;

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) });

		Assert.Equal(2, moved);
		Assert.Equal(Key(0, 3), config.Nes.Port2.Mapping1.A);
		Assert.Equal(Key(0, 6), config.Nes.Port2.Mapping1.Start);
		Assert.Equal(Key(0, 1), config.Nes.Port1.Mapping1.B);
		Assert.Equal(0x0020, config.Nes.Port1.Mapping1.Select);
	}

	[Fact]
	public void Every_console_port_is_walked()
	{
		Configuration config = new();
		config.Gameboy.Controller.Mapping1.A = Key(1, 3);
		config.Gba.Controller.Mapping1.A = Key(1, 3);
		config.Sms.Port2.Mapping1.B = Key(1, 1);
		config.Nes.ExpPort.Mapping1.Start = Key(1, 6);

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) });

		Assert.Equal(4, moved);
		Assert.Equal(Key(0, 3), config.Gameboy.Controller.Mapping1.A);
		Assert.Equal(Key(0, 3), config.Gba.Controller.Mapping1.A);
		Assert.Equal(Key(0, 1), config.Sms.Port2.Mapping1.B);
		Assert.Equal(Key(0, 6), config.Nes.ExpPort.Mapping1.Start);
	}

	[Fact]
	public void A_swap_moves_both_sides_at_once()
	{
		Configuration config = new();
		//Player 1 on device 0 and player 2 on device 1, then the two renumber.
		config.Nes.Port1.Mapping1.A = Key(0, 3);
		config.Nes.Port2.Mapping1.A = Key(1, 3);

		int moved = ControllerKeyMigration.Apply(config, new[] {
			new DeviceMove(GamepadBackend.Evdev, 0, 1),
			new DeviceMove(GamepadBackend.Evdev, 1, 0)
		});

		Assert.Equal(2, moved);
		Assert.Equal(Key(1, 3), config.Nes.Port1.Mapping1.A);
		Assert.Equal(Key(0, 3), config.Nes.Port2.Mapping1.A);
	}

	[Fact]
	public void Custom_button_arrays_move_too()
	{
		Configuration config = new();
		//A Zapper and a light phaser bound to the pad, plus one keyboard entry.
		config.Nes.Port1.Mapping1.ZapperButtons = new ushort[] { Key(1, 4), 0x0041 };
		config.Sms.Port1.Mapping1.LightPhaserButtons = new ushort[] { Key(1, 5) };

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) });

		Assert.Equal(2, moved);
		Assert.Equal(new ushort[] { Key(0, 4), 0x0041 }, config.Nes.Port1.Mapping1.ZapperButtons);
		Assert.Equal(new ushort[] { Key(0, 5) }, config.Sms.Port1.Mapping1.LightPhaserButtons);
	}

	[Fact]
	public void A_DirectInput_move_rewrites_its_own_family_and_not_XInput()
	{
		//The review's finding 3: Windows' two pad families share ordinal 1, so a
		//joystick move must rewrite the 0x2000 keys and leave the 0x1000 ones be.
		Configuration config = new();
		config.Nes.Port1.Mapping1.A = DiKey(1, 3);
		config.Nes.Port1.Mapping1.B = Key(1, 3);

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.DirectInput, 1, 0) });

		Assert.Equal(1, moved);
		Assert.Equal(DiKey(0, 3), config.Nes.Port1.Mapping1.A);
		Assert.Equal(Key(1, 3), config.Nes.Port1.Mapping1.B);
	}

	[Fact]
	public void Slice4_spare_pad_controls_move_too()
	{
		//The review's finding 4: a slice-4 extra-button shortcut carries the same
		//device-bearing key code, so it must move with the pad as well.
		Configuration config = new();
		config.Preferences.ShortcutKeys.Add(new ShortcutKeyInfo {
			Shortcut = EmulatorShortcut.ToggleFps,
			PadBinding = new PadShortcutBinding { KeyCode = Key(1, 3), ThresholdPercent = 35 }
		});

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) });

		Assert.Equal(1, moved);
		Assert.Equal(Key(0, 3), config.Preferences.ShortcutKeys[0].PadBinding!.KeyCode);
		//The threshold is not a device and does not move.
		Assert.Equal(35, config.Preferences.ShortcutKeys[0].PadBinding!.ThresholdPercent);
	}

	[Fact]
	public void No_moves_and_unbound_slots_change_nothing()
	{
		Configuration config = new();
		config.Nes.Port1.Mapping1.A = Key(1, 3);

		Assert.Equal(0, ControllerKeyMigration.Apply(config, _none));
		Assert.Equal(Key(1, 3), config.Nes.Port1.Mapping1.A);

		//A move for a device no key uses is also a no-op (0 moved, so no save).
		Assert.Equal(0, ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 2, 3) }));
		Assert.Equal(Key(1, 3), config.Nes.Port1.Mapping1.A);
	}

	[Fact]
	public void A_repair_of_the_Four_Score_P3_and_P4_keys_reaches_the_core_config()
	{
		//Issue #943: P3/P4 are Port1C/Port1D (Port1A/Port1B go out with Port1's and
		//Port2's keys), so a repair of P3/P4 only holds if those two ports are walked
		//AND pushed with their own keys. NesConfig.ToInterop is the struct
		//ApplyConfig hands the Core, built without calling it.
		Configuration config = new();
		config.Nes.Port1C.Mapping1.A = Key(1, 3);
		config.Nes.Port1D.Mapping2.Start = Key(1, 6);

		int moved = ControllerKeyMigration.Apply(config, new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) });
		InteropNesConfig pushed = config.Nes.ToInterop();

		Assert.Equal(2, moved);
		Assert.Equal(Key(0, 3), pushed.Port1C.Keys.Mapping1.A);
		Assert.Equal(Key(0, 6), pushed.Port1D.Keys.Mapping2.Start);
	}
}
