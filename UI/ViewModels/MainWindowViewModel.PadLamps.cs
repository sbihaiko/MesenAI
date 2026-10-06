using System;
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

		//One poll of the Core; the lamps follow. The count is the truth, so a pad
		//the backend cannot name still lights its port.
		public void RefreshPadLamps()
		{
			Shell.UpdatePadPorts(PadPortLamps.Build(ConnectedGamepadCount(), GamepadName));
		}

		private static string DefaultGamepadName(uint index) =>
			InputApi.GetGamepadInfo(index, out GamepadInfo info) ? info.Name ?? "" : "";
	}
}
