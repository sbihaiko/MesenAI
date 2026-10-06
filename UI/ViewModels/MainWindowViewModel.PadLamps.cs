using System;
using System.Collections.Generic;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//ADR-0249 (W-S1) and ADR-0255: the shell status line's four port lamps - an
	//arcade-cabinet affordance, so the player sees at a glance, with no keyboard
	//and no mouse, which of the four ports has a pad on it. The rule ("given N
	//pads, what do the four lamps read") is host-free (UI/Logic/PadPortLamps,
	//tested in UI.Tests/Shell); this is the one place that reads the Core and
	//hands the strip to WorkspaceShellViewModel. The window's 1 s poll
	//(PlayEdgeFlowsWiring) drives RefreshPadLamps.
	public partial class MainWindowViewModel
	{
		//The per-pad name, injectable the way the tester's lookup is
		//(GamepadTesterViewModel, ADR-0255). The connected count is already one
		//(ConnectedGamepadCount, MainWindowViewModel.EntryToast): a headless test
		//has no pad, and the pair is what makes the strip show a lit lamp.
		public Func<uint, string> GamepadName { get; set; } = DefaultGamepadName;

		//#925: the pad's own light, injectable for the same reason. A pad's key
		//block (null when the backend cannot describe it), the ports of the loaded
		//console as the Controller sheet reads them, and the core's light call -
		//a no-op everywhere but macOS (GCController.light).
		public Func<uint, int?> GamepadBlock { get; set; } = DefaultGamepadBlock;
		public Func<IReadOnlyList<SheetPort>> PadLightPorts { get; set; } = () => ControllerSheetViewModel.ReadPorts(EmuApi.GetRomInfo().ConsoleType);
		public Action<PadLight> SendPadLight { get; set; } = light => InputApi.SetGamepadLight(light.PadIndex, light.Color.R, light.Color.G, light.Color.B);

		private readonly PadLightSync _padLights = new();

		//One poll of the Core; the lamps follow. The count is the truth, so a pad
		//the backend cannot name still lights its port. The pads' own lights ride
		//the same poll, so a reassignment reaches the pad within a second.
		public void RefreshPadLamps()
		{
			uint connected = ConnectedGamepadCount();
			Shell.UpdatePadPorts(PadPortLamps.Build(connected, GamepadName));
			RefreshPadLights(connected);
		}

		private void RefreshPadLights(uint connected)
		{
			List<int> blocks = new();
			for(uint i = 0; i < connected; i++) {
				//A pad the backend cannot describe ends the list: Plan numbers pads by
				//position, and a gap would hand the next pad's colour to the wrong one.
				if(GamepadBlock(i) is not int block) {
					break;
				}
				blocks.Add(block);
			}
			foreach(PadLight light in _padLights.Due(PadLights.Plan(PadLightPorts(), blocks), (int)connected)) {
				SendPadLight(light);
			}
		}

		private static int? DefaultGamepadBlock(uint index) =>
			InputApi.GetGamepadInfo(index, out GamepadInfo info) ? ControllerDevices.PadBlock(info.Backend, (int)info.Slot) : null;

		private static string DefaultGamepadName(uint index) =>
			InputApi.GetGamepadInfo(index, out GamepadInfo info) ? info.Name ?? "" : "";
	}
}
