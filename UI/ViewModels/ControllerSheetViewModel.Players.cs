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
	//is P1"), and the player's color.
	public partial class ControllerSheetPlayerRow : ObservableObject
	{
		public int PortIndex { get; }
		public string Label { get; }
		public int ColorIndex { get; }
		public string DeviceName { get; }
		public bool HasDevice { get; }
		public IBrush Color { get; }

		public ControllerSheetPlayerRow(int portIndex, string label, int colorIndex, string deviceName, bool hasDevice)
		{
			PortIndex = portIndex;
			Label = label;
			ColorIndex = colorIndex;
			DeviceName = deviceName;
			HasDevice = hasDevice;
			Color = ControllerSheetViewModel.PlayerBrush(colorIndex);
		}
	}

	//ADR-0255 slice 2 (PRD Part B §13.5.2 W-P17): PLAYERS, read off the ports.
	//One row per port, naming the device whose keys are under it; assignment
	//moves a device's keys from one port's slots to another's through the same
	//ConfigManager path and the same ApplyConfig() call the classic Input page
	//uses. The two surfaces agree about which slot a pad is in by construction,
	//not by a test that drives the classic page: both read and write the same
	//KeyMapping objects inside ConfigManager.Config, and the free-slot rule here
	//is the one W-P15's HasKeys applies (KeyMapping.ToInterop, which includes the
	//port type's custom keys). The one test that would catch a drift - a slot
	//bound only by custom keys - is
	//PlayerControllerSheetTests.A_slot_that_binds_only_custom_keys_is_not_free.
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

		//The palette lives in PadLights (UI/Logic) so the pad's own light (#925)
		//and the port label paint the same colour.
		private static readonly IBrush[] _playerBrushes = PadLights.PlayerColors
			.Select(c => (IBrush)new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))).ToArray();

		internal static IBrush PlayerBrush(int colorIndex) => _playerBrushes[Math.Clamp(colorIndex, 0, _playerBrushes.Length - 1)];

		//The rows the sheet last showed. Kept so a 60 Hz read that finds nothing
		//new does not replace the observable list (which would rebuild every row
		//on screen each tick); it is a comparison against what the ports say now,
		//never a cache, so a config change made elsewhere still surfaces.
		private IReadOnlyList<ControllerSheetPlayerRow> _lastPlayers = Array.Empty<ControllerSheetPlayerRow>();

		partial void OnSelectedPadIndexChanged(int value)
		{
			//The note names the pad it was written about (ControllerSheetNotBound /
			//ControllerSheetAssigned take pad.Name). The picker moving to another pad
			//is where it stops being true, and the rows do not move with the picker -
			//so the note goes here, not only when RefreshPlayers sees a row change.
			//(AssignTo writes a fresh note after its own refresh, so this cannot
			//clear what it just said.)
			AssignNote = "";
			ApplyPad();
		}

		//The ports of the loaded console, read from ConfigManager as the sheet's
		//own plain data. Nothing about "who is P1" is stored: the row's device is
		//the device of the keys the port already holds.
		private IReadOnlyList<SheetPort> BuildPorts() => ReadPorts(CurrentConsole());

		//The same reading for a caller with no sheet open - the window's pad-light
		//poll (#925) - so the light and the PLAYERS rows read one port table.
		internal static IReadOnlyList<SheetPort> ReadPorts(ConsoleType console)
		{
			List<SheetPort> ports = new();
			foreach((string key, int player) in ControllerSheetPorts.For(console)) {
				if(PortConfig(console, key) is not ControllerConfig config) {
					continue;
				}
				ushort[][] slots = {
					SlotKeys(config, 0).All, SlotKeys(config, 1).All,
					SlotKeys(config, 2).All, SlotKeys(config, 3).All
				};
				//The keyboard line's own input: the fixed fields alone, never the
				//port type's custom keys (SheetPort explains the split).
				ushort[][] keyboardSlots = {
					SlotKeys(config, 0).Fixed, SlotKeys(config, 1).Fixed,
					SlotKeys(config, 2).Fixed, SlotKeys(config, 3).Fixed
				};
				ports.Add(new SheetPort(key, ResourceHelper.GetMessage("ControllerSheetPlayer", player), player - 1, slots, keyboardSlots));
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

		//A slot's keys, read the way the classic Input page and W-P15 read them:
		//through KeyMapping.ToInterop, so the port type's custom keys (a Zapper's
		//clicks, a keyboard's rows) are included and a slot that binds only those is
		//not read as free. All is every bound key - what the free-slot rule and the
		//device match read; Fixed is the fixed KeyMapping fields alone - what the
		//keyboard line reads, since a port type's custom keys are that device's
		//buttons, not the keyboard's. The unbound (zero) fields are dropped, so a
		//slot that binds nothing is the empty array the host-free rules read as
		//free. This is the same rule W-P15's HasKeys applies; the two surfaces must
		//not disagree about which slot a pad is in (ADR-0255 Consequences).
		private static (ushort[] All, ushort[] Fixed) SlotKeys(ControllerConfig config, int index)
		{
			InteropKeyMapping k = ControllerSheetSlotWrite.Slot(config, index).ToInterop(config.Type, index);
			ushort[] fixedKeys = {
				k.A, k.B, k.X, k.Y, k.L, k.R, k.Up, k.Down, k.Left, k.Right, k.Start, k.Select, k.U, k.D,
				k.TurboA, k.TurboB, k.TurboX, k.TurboY, k.TurboL, k.TurboR, k.TurboSelect, k.TurboStart, k.GenericKey1
			};
			List<ushort> all = new(fixedKeys);
			if(k.CustomKeys != null) {
				all.AddRange(k.CustomKeys);
			}
			return (all.Where(key => key != 0).ToArray(), fixedKeys.Where(key => key != 0).ToArray());
		}

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
			if(!SameRows(_lastPlayers, rows)) {
				//The rows changed under the note (a pad plugged in or taken out, a
				//binding made on another surface): what the note said is no longer
				//true, so it goes.
				AssignNote = "";
				_lastPlayers = rows;
				Players = rows;
			}
			ShowPadPicker = Tester.Gamepads.Count > 1;
			ShowPlayers = Tester.Gamepads.Count > 1 && ports.Count > 1;
			RefreshKeyboard(ports);
			//ADR-0255 slice 3: REMAP reads the same ports this does, so every path
			//that re-reads the sheet re-reads it too - and the sheet's own 60 Hz
			//poll keeps its lights and its capture live.
			RefreshRemap();
			//ADR-0255 slice 4: EXTRA BUTTONS reads them for the same reason (the
			//section needs a player port to be about anything), and rides the same
			//poll - its own note and its armed row stay live with it.
			RefreshExtra(ports);
		}

		//Whether two readings of the rows are the same shown surface. Value
		//comparison, not reference: it is what lets an unchanged read keep the list
		//without rebuilding it, while any real change still gets through.
		private static bool SameRows(IReadOnlyList<ControllerSheetPlayerRow> a, IReadOnlyList<ControllerSheetPlayerRow> b)
		{
			if(a.Count != b.Count) {
				return false;
			}
			for(int i = 0; i < a.Count; i++) {
				if(a[i].PortIndex != b[i].PortIndex || a[i].Label != b[i].Label || a[i].ColorIndex != b[i].ColorIndex
					|| a[i].DeviceName != b[i].DeviceName || a[i].HasDevice != b[i].HasDevice) {
					return false;
				}
			}
			return true;
		}

		private void RefreshKeyboard(IReadOnlyList<SheetPort> ports)
		{
			IReadOnlyList<ushort> keys = ControllerSheetKeyboard.Keys(ports);
			ShowKeyboard = Tester.Gamepads.Count == 0;
			//The button asks Configuration's own guard - the exact predicate
			//RestoreKeyboardPresetIfNothingIsBound uses - so it is never offered where
			//the click would be a silent no-op, and the guard is never weakened to
			//match a weaker gate (ADR-0255).
			CanRestoreKeyboard = ShowKeyboard && ConfigManager.Config.CanRestoreKeyboardPreset();
			if(keys.Count == 0) {
				KeyboardText = ResourceHelper.GetMessage("ControllerSheetKeyboardNothing");
			} else {
				KeyboardText = ResourceHelper.GetMessage("ControllerSheetKeyboardPlays", string.Join(", ", keys.Select(KeyName)));
			}
		}

		//Which device is under a port, as a name: the connected pad's own name when
		//it is still connected, else its place in the connection order. The pad is
		//found by the same block its keys carry - its backend's family plus its
		//family-relative slot - never by the host's enumeration ordinal, which is not
		//the same numbering on Windows (#813). The latter fallback is why the row is
		//not a stable identity (ADR-0255 slice 5).
		private string DeviceLabel(int? deviceBlock)
		{
			if(deviceBlock is not int block) {
				return ResourceHelper.GetMessage("ControllerSheetNoDevice");
			}
			GamepadTestItem? pad = Tester.Gamepads.FirstOrDefault(g => ControllerDevices.PadBlock(g.Backend, (int)g.Slot) == block);
			if(pad != null) {
				return pad.Name;
			}
			//No connected pad owns this block: name the device by its offset from the
			//base family. A DirectInput pad on Windows would number from 0x2000 and so
			//read high here, but such a pad is connected and named above; this is only
			//the "keys are here but the pad is gone" line.
			return ResourceHelper.GetMessage("ControllerSheetUnnamedDevice", ((block - ControllerDevices.BaseGamepadIndex) >> 8) + 1);
		}

		//The assignment: move every slot the selected pad's keys live in to the chosen
		//port, through the same ConfigManager path and ApplyConfig() call the classic
		//Input page uses. All of the device's slots move together, so a pad bound in
		//two slots cannot end up in two ports at once. The keys live under the port,
		//so there is nothing else to update - no second table of "who is P1".
		public void AssignTo(int portIndex)
		{
			IReadOnlyList<SheetPort> ports = BuildPorts();
			if(Pad is not GamepadTestItem pad || portIndex < 0 || portIndex >= ports.Count) {
				return;
			}
			int device = ControllerDevices.PadBlock(pad.Backend, (int)pad.Slot);
			PortMove plan = ControllerSheetPorts.PlanMove(ports, device, portIndex);
			string note = plan.Outcome switch {
				PortMoveOutcome.Moved => ResourceHelper.GetMessage("ControllerSheetAssigned", pad.Name, ports[portIndex].Label),
				PortMoveOutcome.NotBound => ResourceHelper.GetMessage("ControllerSheetNotBound", pad.Name),
				PortMoveOutcome.NoFreeSlot => ResourceHelper.GetMessage("ControllerSheetNoSlot"),
				_ => ""
			};
			if(plan.Moves) {
				ConsoleType console = CurrentConsole();
				if(PortConfig(console, ports[portIndex].Key) is ControllerConfig target) {
					foreach(SlotMove move in plan.Slots) {
						if(PortConfig(console, ports[move.SourcePort].Key) is not ControllerConfig source) {
							continue;
						}
						ControllerSheetSlotWrite.MoveSlot(source, move.SourceSlot, target, move.TargetSlot);
					}
					//The same tail the classic Input page and W-P15 use: push the config
					//to the core, then persist it.
					ConfigManager.Config.ApplyConfig();
					ConfigManager.Config.Save();
				}
			}
			//Refresh first - RefreshPlayers clears a stale note when the rows change,
			//and the move just made is exactly that change - then set the note for the
			//state the refresh landed on.
			RefreshPlayers();
			AssignNote = note;
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

	}

	//ADR-0255 slice 2, the write half of the assignment, moved out of the
	//view-model so the headless suite can pin it without a running app (as
	//ControllerKeyMigration.Apply is pinned). `Slot` is the one index -> KeyMapping
	//mapping the read side (SlotKeys) and the write side (MoveSlot) both use, so
	//the slot a key is read from is the slot it is written to.
	public static class ControllerSheetSlotWrite
	{
		public static KeyMapping Slot(ControllerConfig config, int index) => index switch {
			0 => config.Mapping1,
			1 => config.Mapping2,
			2 => config.Mapping3,
			_ => config.Mapping4
		};

		//ADR-0255 slice 3: one console control's field inside a slot, so the REMAP
		//mode can read what the row is bound to and write the rebind back into the
		//same slot. Only the fixed KeyMapping fields: a port type's custom keys are
		//that device's own buttons, not a console control's, and the sheet's rows
		//are the console's controls (W-P15's SetupButton list).
		public static ushort Field(KeyMapping mapping, SetupButton button) => button switch {
			SetupButton.A => mapping.A,
			SetupButton.B => mapping.B,
			SetupButton.Select => mapping.Select,
			SetupButton.Start => mapping.Start,
			SetupButton.Up => mapping.Up,
			SetupButton.Down => mapping.Down,
			SetupButton.Left => mapping.Left,
			SetupButton.Right => mapping.Right,
			SetupButton.L => mapping.L,
			_ => mapping.R
		};

		public static void SetField(KeyMapping mapping, SetupButton button, ushort code)
		{
			switch(button) {
				case SetupButton.A: mapping.A = code; break;
				case SetupButton.B: mapping.B = code; break;
				case SetupButton.Select: mapping.Select = code; break;
				case SetupButton.Start: mapping.Start = code; break;
				case SetupButton.Up: mapping.Up = code; break;
				case SetupButton.Down: mapping.Down = code; break;
				case SetupButton.Left: mapping.Left = code; break;
				case SetupButton.Right: mapping.Right = code; break;
				case SetupButton.L: mapping.L = code; break;
				default: mapping.R = code; break;
			}
		}

		//Move one slot's every key from `source` to a free slot of `target`. The
		//PLAYERS surface reads a device off all the keys a slot holds - the fixed
		//fields AND the port type's own custom-button array (KeyMapping.ToInterop's
		//CustomKeys: a Zapper's buttons, a keyboard's rows) - so the write has to
		//move that same set. The keys all live in the one KeyMapping object, so the
		//move relocates the object itself: copying the fixed fields alone left the
		//custom keys on the source port, so a pad whose binding lived only in them
		//was reported "assigned" while it still held a binding on the source, and a
		//clear that copied only the fixed fields left the slot it had just reported
		//free still naming the old device. Swap, rather than copy, also keeps keys
		//the target's own Type does not expose (a port type other than the source's)
		//instead of dropping them. The target slot is one PlanMove found free, so
		//what the source receives is an empty slot.
		public static void MoveSlot(ControllerConfig source, int sourceSlot, ControllerConfig target, int targetSlot)
		{
			if(source == target && sourceSlot == targetSlot) {
				return;
			}
			KeyMapping from = Slot(source, sourceSlot);
			KeyMapping to = Slot(target, targetSlot);
			SetSlot(target, targetSlot, from);
			SetSlot(source, sourceSlot, to);
		}

		private static void SetSlot(ControllerConfig config, int index, KeyMapping mapping)
		{
			switch(index) {
				case 0: config.Mapping1 = mapping; break;
				case 1: config.Mapping2 = mapping; break;
				case 2: config.Mapping3 = mapping; break;
				default: config.Mapping4 = mapping; break;
			}
		}
	}
}
