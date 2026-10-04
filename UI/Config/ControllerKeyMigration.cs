using Mesen.Config.Shortcuts;
using Mesen.Logic;
using System.Collections.Generic;

namespace Mesen.Config
{
	//ADR-0255 slice 5, the config half: move a pad's keys when its device index
	//changes on a reconnect. The rule - which index to which, and which moves are
	//safe - is host-free in UI/Logic/DeviceReconnect; this walks every console
	//port's four mapping slots and every shortcut's spare pad control, and
	//rewrites the device index inside each key code. The caller then
	//ApplyConfig/Saves through the same ConfigManager path the classic Input page
	//uses, so the sheet and the classic page agree.
	public static class ControllerKeyMigration
	{
		//Rewrites every port of every console the player can bind, plus the slice-4
		//spare pad controls a shortcut may carry; returns how many keys moved (0
		//means nothing to save).
		public static int Apply(Configuration config, IReadOnlyList<DeviceMove> moves)
		{
			if(moves.Count == 0) {
				return 0;
			}

			int moved = 0;
			foreach(ControllerConfig port in Ports(config)) {
				moved += port.Mapping1.RemapDevice(moves);
				moved += port.Mapping2.RemapDevice(moves);
				moved += port.Mapping3.RemapDevice(moves);
				moved += port.Mapping4.RemapDevice(moves);
			}

			//The slice-4 spare pad controls carry the same device-bearing key code
			//(a PadShortcutBinding is one button, stored as
			//BaseGamepadIndex + device * 0x100 + button), so they move with the pad
			//too. The threshold is a percentage, not a device, and does not move.
			foreach(ShortcutKeyInfo shortcut in config.Preferences.ShortcutKeys) {
				if(shortcut.PadBinding is PadShortcutBinding pad && !pad.IsEmpty) {
					ushort next = DeviceReconnect.RemapKey(pad.KeyCode, moves);
					if(next != pad.KeyCode) {
						pad.KeyCode = next;
						moved++;
					}
				}
			}
			return moved;
		}

		//Every port that can carry a gamepad binding: the two NES and SMS ports,
		//the NES expansion port and its four players, the mapper input, and the
		//single port Game Boy and GBA each have (plus the link cable's pad).
		public static IEnumerable<ControllerConfig> Ports(Configuration config)
		{
			foreach(ControllerConfig port in new[] {
				config.Nes.Port1, config.Nes.Port2, config.Nes.ExpPort,
				config.Nes.Port1A, config.Nes.Port1B, config.Nes.Port1C, config.Nes.Port1D,
				config.Nes.ExpPortA, config.Nes.ExpPortB, config.Nes.ExpPortC, config.Nes.ExpPortD,
				config.Nes.MapperInput,
				config.Gameboy.Controller, config.Gameboy.LinkedController,
				config.Gba.Controller,
				config.Sms.Port1, config.Sms.Port2
			}) {
				yield return port;
			}
		}
	}
}
