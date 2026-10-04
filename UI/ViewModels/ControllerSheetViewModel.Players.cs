using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//One row of the sheet's PLAYERS list: a player port, the device whose keys
	//live under it (read off the port's own slots, never a second table of "who
	//is P1"), and the player's colour.
	public partial class ControllerSheetPlayerRow : ObservableObject
	{
		public int PortIndex { get; }
		public string Label { get; }
		public string DeviceName { get; }
		public bool HasDevice { get; }
		public IBrush Color { get; }

		public ControllerSheetPlayerRow(int portIndex, string label, int colorIndex, string deviceName, bool hasDevice)
		{
			PortIndex = portIndex;
			Label = label;
			DeviceName = deviceName;
			HasDevice = hasDevice;
			Color = ControllerSheetViewModel.PlayerBrush(colorIndex);
		}
	}

	//ADR-0255 slice 2 (PRD Part B §13.5.2 W-P17): PLAYERS, read off the ports.
	//One row per port, naming the device whose keys are under it; assignment
	//moves a device's keys from one port's slots to another's through the same
	//ConfigManager path and the same ApplyConfig() call the classic Input page
	//uses, so the two surfaces cannot disagree about which slot a pad is in.
	//
	//A device index is a *connection ordering*, not an identity (ADR-0255
	//Consequences): a pad unplugged and plugged back can return under another
	//index with its keys still in this port, handing the port to a different
	//pad. Slice 5 repairs that by VID:PID; until it lands, nothing here may
	//present slot membership as stable - the rows name the device as it is read
	//*now*, and say so.
	public partial class ControllerSheetViewModel
	{
		[ObservableProperty] public partial int SelectedPadIndex { get; set; }

		[ObservableProperty, NotifyPropertyChangedFor(nameof(HasPlayers))] public partial IReadOnlyList<ControllerSheetPlayerRow> Players { get; private set; } = Array.Empty<ControllerSheetPlayerRow>();
		[ObservableProperty] public partial bool ShowPlayers { get; private set; }
		[ObservableProperty] public partial bool ShowKeyboard { get; private set; }
		[ObservableProperty] public partial bool CanRestoreKeyboard { get; private set; }
		[ObservableProperty] public partial string KeyboardText { get; private set; } = "";
		[ObservableProperty] public partial string AssignNote { get; private set; } = "";
		//The strip of connected pads the user picks from before assigning one to
		//a player; shown only when there is more than one to pick between.
		[ObservableProperty] public partial bool ShowPadPicker { get; private set; }

		public bool HasPlayers => Players.Count > 0;

		//Injectable for the headless tests; the Core's answer by default.
		public Func<ConsoleType> CurrentConsole { get; set; } = () => EmuApi.GetRomInfo().ConsoleType;
		public Func<ushort, string> KeyName { get; set; } = InputApi.GetKeyName;

		private static readonly IBrush[] _playerBrushes = {
			new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)), //play blue
			new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)), //red
			new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), //orange
			new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59))  //share green
		};

		internal static IBrush PlayerBrush(int colorIndex) => _playerBrushes[Math.Clamp(colorIndex, 0, _playerBrushes.Length - 1)];

		partial void OnSelectedPadIndexChanged(int value) => ApplyPad();

		//The ports of the loaded console, read from ConfigManager as the sheet's
		//own plain data. Nothing about "who is P1" is stored: the row's device is
		//the device of the keys the port already holds.
		private IReadOnlyList<SheetPort> BuildPorts()
		{
			ConsoleType console = CurrentConsole();
			List<SheetPort> ports = new();
			foreach((string key, int player) in ControllerSheetPorts.For(console)) {
				if(PortConfig(console, key) is not ControllerConfig config) {
					continue;
				}
				ushort[][] slots = {
					SlotKeys(config.Mapping1), SlotKeys(config.Mapping2),
					SlotKeys(config.Mapping3), SlotKeys(config.Mapping4)
				};
				ports.Add(new SheetPort(key, ResourceHelper.GetMessage("ControllerSheetPlayer", player), player - 1, slots));
			}
			return ports;
		}

		private static ControllerConfig? PortConfig(ConsoleType console, string key) => (console, key) switch {
			(ConsoleType.Nes, "Port1") => ConfigManager.Config.Nes.Port1,
			(ConsoleType.Nes, "Port2") => ConfigManager.Config.Nes.Port2,
			(ConsoleType.Sms, "Port1") => ConfigManager.Config.Sms.Port1,
			(ConsoleType.Sms, "Port2") => ConfigManager.Config.Sms.Port2,
			(ConsoleType.Gameboy, "Controller") => ConfigManager.Config.Gameboy.Controller,
			(ConsoleType.Gba, "Controller") => ConfigManager.Config.Gba.Controller,
			_ => null
		};

		//A slot's keys, in KeyMapping's own field order, with the unbound (zero)
		//fields dropped - so a slot that binds nothing is the empty array the
		//host-free rule reads as free. A slot that binds custom keys only (a
		//Zapper's mouse clicks, a keyboard device) reads as free here; this sheet
		//assigns player pads, and none of those bind custom keys.
		private static ushort[] SlotKeys(KeyMapping m) => new[] {
			m.A, m.B, m.X, m.Y, m.L, m.R, m.Up, m.Down, m.Left, m.Right, m.Start, m.Select, m.U, m.D,
			m.TurboA, m.TurboB, m.TurboX, m.TurboY, m.TurboL, m.TurboR, m.TurboSelect, m.TurboStart, m.GenericKey1
		}.Where(key => key != 0).ToArray();

		//The rows, and which of the sheet's sections a state shows. Appears with
		//more than one pad: with none the keyboard plays, and with one there is
		//nothing to assign (ADR-0255 slice 2).
		public void RefreshPlayers()
		{
			ConsoleType console = CurrentConsole();
			IReadOnlyList<SheetPort> ports = BuildPorts();
			List<ControllerSheetPlayerRow> rows = new();
			for(int i = 0; i < ports.Count; i++) {
				int? device = ControllerSheetPorts.PortDevice(ports[i]);
				rows.Add(new ControllerSheetPlayerRow(i, ports[i].Label, ports[i].ColorIndex, DeviceLabel(device), device != null));
			}
			Players = rows;
			ShowPadPicker = Tester.Gamepads.Count > 1;
			ShowPlayers = Tester.Gamepads.Count > 1 && ports.Count > 1;
			RefreshKeyboard(ports);
		}

		private void RefreshKeyboard(IReadOnlyList<SheetPort> ports)
		{
			IReadOnlyList<ushort> keys = ControllerSheetKeyboard.Keys(ports);
			ShowKeyboard = Tester.Gamepads.Count == 0;
			CanRestoreKeyboard = ShowKeyboard && ControllerSheetKeyboard.NothingBound(ports);
			if(keys.Count == 0) {
				KeyboardText = ResourceHelper.GetMessage("ControllerSheetKeyboardNothing");
			} else {
				KeyboardText = ResourceHelper.GetMessage("ControllerSheetKeyboardPlays", string.Join(", ", keys.Select(KeyName)));
			}
		}

		//Which device is under a port, as a name: the connected pad's own name
		//when it is still connected, else its place in the connection order. The
		//latter is why the row is not a stable identity (ADR-0255 slice 5).
		private string DeviceLabel(int? device)
		{
			if(device is not int index) {
				return ResourceHelper.GetMessage("ControllerSheetNoDevice");
			}
			GamepadTestItem? pad = Tester.Gamepads.FirstOrDefault(g => g.Index == index);
			return pad?.Name ?? ResourceHelper.GetMessage("ControllerSheetUnnamedDevice", index + 1);
		}

		//The assignment: move the selected pad's keys to the chosen port, through
		//the same ConfigManager path and ApplyConfig() call the classic Input
		//page uses. The keys live under the port, so there is nothing else to
		//update - no second table of "who is P1" to keep in step.
		public void AssignTo(int portIndex)
		{
			IReadOnlyList<SheetPort> ports = BuildPorts();
			if(Pad is not GamepadTestItem pad || portIndex < 0 || portIndex >= ports.Count) {
				return;
			}
			PortMove plan = ControllerSheetPorts.PlanMove(ports, (int)pad.Index, portIndex);
			AssignNote = plan.Outcome switch {
				PortMoveOutcome.Moved => ResourceHelper.GetMessage("ControllerSheetAssigned", pad.Name, ports[portIndex].Label),
				PortMoveOutcome.NotBound => ResourceHelper.GetMessage("ControllerSheetNotBound", pad.Name),
				PortMoveOutcome.NoFreeSlot => ResourceHelper.GetMessage("ControllerSheetNoSlot"),
				_ => ""
			};
			if(!plan.Moves) {
				return;
			}

			ConsoleType console = CurrentConsole();
			if(PortConfig(console, ports[portIndex].Key) is not ControllerConfig target
				|| PortConfig(console, ports[plan.SourcePort].Key) is not ControllerConfig source) {
				return;
			}
			KeyMapping from = Slot(source, plan.SourceSlot);
			KeyMapping to = Slot(target, plan.TargetSlot);
			CopySlot(from, to);
			ClearSlot(from);
			//The same tail the classic Input page and W-P15 use: push the config
			//to the core, then persist it.
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			RefreshPlayers();
		}

		//The keyboard case's action: write the default keyboard (and pad) preset
		//back when nothing is bound anywhere - the guard lives on Configuration,
		//the same one the load path uses, so a config the player built by hand is
		//never overwritten.
		public void RestoreKeyboardPreset()
		{
			if(!ConfigManager.Config.RestoreKeyboardPresetIfNothingIsBound()) {
				return;
			}
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			RefreshPlayers();
		}

		private static KeyMapping Slot(ControllerConfig config, int index) => index switch {
			0 => config.Mapping1,
			1 => config.Mapping2,
			2 => config.Mapping3,
			_ => config.Mapping4
		};

		private static void CopySlot(KeyMapping from, KeyMapping to)
		{
			to.A = from.A; to.B = from.B; to.X = from.X; to.Y = from.Y; to.L = from.L; to.R = from.R;
			to.Up = from.Up; to.Down = from.Down; to.Left = from.Left; to.Right = from.Right;
			to.Start = from.Start; to.Select = from.Select; to.U = from.U; to.D = from.D;
			to.TurboA = from.TurboA; to.TurboB = from.TurboB; to.TurboX = from.TurboX; to.TurboY = from.TurboY;
			to.TurboL = from.TurboL; to.TurboR = from.TurboR; to.TurboSelect = from.TurboSelect; to.TurboStart = from.TurboStart;
			to.GenericKey1 = from.GenericKey1;
		}

		private static void ClearSlot(KeyMapping m) => CopySlot(new KeyMapping(), m);
	}
}
