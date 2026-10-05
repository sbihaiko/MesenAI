using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//One row of the sheet's REMAP mode: a console control, the pad button bound to
	//it, and the row's two lights (ADR-0255 slice 3). Pad is "what the pad sends"
	//- the pad's own button pressed; Port is "what the port receives" - the
	//console being handed the control. A row lit on the pad side and dark on the
	//port side is a wrong binding made visible, and nothing else in the app shows
	//one.
	public partial class ControllerSheetRemapRow : ObservableObject
	{
		public SetupButton Button { get; }
		//The console control's own name ("A", "Select", or the Master System's 1/2).
		public string Label { get; }

		[ObservableProperty] public partial string BoundName { get; set; } = "";
		[ObservableProperty] public partial bool HasBinding { get; set; }
		[ObservableProperty] public partial bool PadLit { get; set; }
		[ObservableProperty] public partial bool PortLit { get; set; }
		//This is the row the sheet is waiting on ("Press a control…").
		[ObservableProperty] public partial bool Armed { get; set; }

		public ControllerSheetRemapRow(SetupButton button, string label)
		{
			Button = button;
			Label = label;
		}
	}

	//ADR-0255 slice 3 (PRD Part B §13.5.2 W-P17): REMAP, as a mode of the same
	//sheet. The rows are the loaded console's controls; picking one arms the
	//capture, pressing a pad control binds it, and Esc cancels - through the one
	//Esc router (PlayEsc), never a second key handler. What each control binds,
	//where a rebind lands and the two lights are host-free (ControllerSheetRemap);
	//this file feeds them and applies what they answer, so the sheet is not a
	//second source of truth for any of it.
	public partial class ControllerSheetViewModel
	{
		[ObservableProperty] public partial IReadOnlyList<ControllerSheetRemapRow> RemapRows { get; private set; } = Array.Empty<ControllerSheetRemapRow>();
		[ObservableProperty] public partial bool ShowRemap { get; private set; }
		[ObservableProperty] public partial string RemapNote { get; private set; } = "";

		//True while the sheet is waiting for a pad control. ADR-0256's bridge asks
		//it (MainWindowViewModel.IsControllerCapturing) so a pad press is the
		//capture's and not also a focus move or a Confirm (ADR-0255 slice 3).
		[ObservableProperty] public partial bool IsCapturing { get; private set; }

		//Injectable for the headless tests; the host's own reads by default. The
		//pressed set is the same one the pad bridge samples, so a press means the
		//same thing to both.
		public Func<IReadOnlyList<ushort>> PressedKeys { get; set; } = () => InputApi.GetPressedKeys();
		public Func<string, ushort> KeyCode { get; set; } = InputApi.GetKeyCode;

		private readonly ControllerSheetCapture _capture = new();
		private List<ControllerSheetRemapRow> _remapRows = new();
		private ConsoleType? _remapConsole;

		//Which port the REMAP rows edit: the port that *holds* the selected pad's
		//keys, in any of its slots - not only the slot PLAYERS names it by. A port
		//whose first named slot holds one pad and a later slot another is a
		//configuration the classic Input page can make, and reading only the first
		//(PortDevice) sent that pad's rebind to the first port instead, over the
		//other player's bindings (found in review). Else the first port, which is
		//where W-P15's setup writes and the port a pad has to reach before it can be
		//assigned at all. The lights are that port's: what the console receives for
		//the player this pad plays as.
		private int RemapPortIndex(IReadOnlyList<SheetPort> ports)
		{
			if(Pad is GamepadTestItem pad) {
				int block = ControllerDevices.PadBlock(pad.Backend, (int)pad.Slot);
				for(int i = 0; i < ports.Count; i++) {
					if(ControllerSheetPorts.HoldsDevice(ports[i], block)) {
						return i;
					}
				}
			}
			return ports.Count > 0 ? 0 : -1;
		}

		//The code the port's slots bind for this control: the first non-zero field
		//across the four slots (which is also the slot the rebind replaces).
		private static ushort BoundCode(ControllerConfig config, SetupButton button)
		{
			for(int slot = 0; slot < 4; slot++) {
				ushort code = ControllerSheetSlotWrite.Field(ControllerSheetSlotWrite.Slot(config, slot), button);
				if(code != 0) {
					return code;
				}
			}
			return 0;
		}

		//ADR-0256 Decision 4 for the capture: a control the pad navigates with may
		//not be bound here, and the refusal is the caller's to show. Read off the
		//pad the code came from (PadNaming's family and its own device index), and
		//never off the console mapping.
		private bool NonRebindable(ushort code)
		{
			if(PadNaming.Of(code, KeyName) is not PadId pad) {
				return false;
			}
			return PadNavControls.NonRebindable(code, PadNavControls.Resolve(pad.Family, pad.Device, KeyCode));
		}

		//One 60 Hz read of the REMAP section: the rows' lights, and the capture's
		//own step. Called from RefreshPlayers, so every path that re-reads the
		//sheet re-reads this too, and the sheet's poll keeps it live.
		public void RefreshRemap()
		{
			ConsoleType console = CurrentConsole();
			IReadOnlyList<SetupButton> controls = ControllerSheetRemap.Controls(console);
			IReadOnlyList<SheetPort> ports = BuildPorts();
			int portIndex = RemapPortIndex(ports);
			ControllerConfig? config = portIndex >= 0 ? PortConfig(console, ports[portIndex].Key) : null;
			GamepadTestItem? pad = Pad;

			//A pad that goes away takes the capture with it: there is nothing left
			//to press, and a stray bind would land on a port the player stopped
			//looking at.
			bool show = pad != null && config != null && controls.Count > 0;
			ShowRemap = show;
			if(!show) {
				if(_capture.IsCapturing) {
					_capture.Cancel();
					IsCapturing = false;
				}
				RemapNote = "";
				return;
			}

			//The rows are rebuilt only when the console (or its control list)
			//changes, so a 60 Hz read updates them in place instead of rebuilding
			//every row on screen each tick. The rebuild hands over a *fresh* list:
			//re-assigning the one already bound notifies nobody ([ObservableProperty]
			//skips an equal reference and List<T> raises no collection change), so the
			//section kept the previous console's rows on screen - frozen, because
			//ApplyRemapRows then only touches the new objects. The PLAYERS rows assign
			//a fresh list for the same reason (found in review).
			if(console != _remapConsole || _remapRows.Count != controls.Count) {
				_remapConsole = console;
				List<ControllerSheetRemapRow> rows = new();
				foreach(SetupButton button in controls) {
					rows.Add(new ControllerSheetRemapRow(button, ControllerSheetRemap.ControlLabel(console, button)));
				}
				_remapRows = rows;
				RemapRows = rows;
			}

			IReadOnlyList<ushort> pressed = PressedKeys();
			if(_capture.IsCapturing) {
				CaptureOutcome outcome = _capture.OnTick(pressed, ControllerDevices.PadBlock(pad!.Backend, (int)pad.Slot), NonRebindable);
				switch(outcome) {
					case CaptureOutcome.Bound:
						IsCapturing = false;
						BindCaptured(_capture.BoundCode, portIndex, ports[portIndex]);
						//The write changed the port, so the lights are re-read from it.
						pressed = PressedKeys();
						break;
					case CaptureOutcome.Refused:
						//Visible, never a silent no-op: the row stays armed and the
						//sheet says why it refused.
						RemapNote = ResourceHelper.GetMessage("ControllerSheetRemapNonRebindable");
						break;
				}
			}
			ApplyRemapRows(config!, pad!, pressed);
		}

		//The rows' two lights, from the two real sources: the pad's own buttons
		//(GamepadTestItem.Buttons, the per-backend order slice 1 reads) and the
		//console's own view (the port's bound code, held) - never a third table.
		private void ApplyRemapRows(ControllerConfig config, GamepadTestItem pad, IReadOnlyList<ushort> pressed)
		{
			foreach(ControllerSheetRemapRow row in _remapRows) {
				ushort bound = BoundCode(config, row.Button);
				int? bit = bound != 0 ? ControllerSheetRemap.ButtonBitOfCodeName(KeyName(bound), pad.Backend) : null;
				bool padHeld = bit is int index && index < pad.Buttons.Count && pad.Buttons[index].IsPressed;
				RemapLights lights = ControllerSheetRemap.Lights(bound, bit, padHeld, bound != 0 && pressed.Contains(bound));

				row.BoundName = bound != 0 ? KeyName(bound) : ResourceHelper.GetMessage("ControllerSheetRemapUnbound");
				row.HasBinding = bound != 0;
				row.PadLit = lights.PadLit;
				row.PortLit = lights.PortLit;
				row.Armed = _capture.IsCapturing && _capture.Button == row.Button;
			}
		}

		//A row was picked: wait for a pad control. The press that picked the row is
		//released first (ControllerSheetCapture), so a pad Confirm that tapped it
		//cannot bind itself.
		public void ArmRemap(SetupButton button)
		{
			if(!ShowRemap) {
				return;
			}
			_capture.Arm(button);
			IsCapturing = true;
			//The armed state is on screen, not only in the model: the note names the
			//control being captured, and says how to get out.
			RemapNote = ResourceHelper.GetMessage("ControllerSheetRemapArm", ControllerSheetRemap.ControlLabel(CurrentConsole(), button));
			RefreshRemap();
		}

		//Esc, through the one router (PlayEsc's CancelCapture state): the capture
		//ends and the sheet stays up. A no-op when nothing is capturing, so the
		//router can ask it without a second state check.
		public void CancelCapture()
		{
			if(!_capture.IsCapturing) {
				return;
			}
			EndCapture();
			RefreshRemap();
		}

		//The one place a capture ends without a bind, so no path can leave the flag
		//the pad bridge reads (MainWindowViewModel.IsControllerCapturing) answering
		//true: Esc cancels, and the sheet closing takes its own mode with it - the
		//poll stops with the sheet, and nothing else would ever notice.
		internal void EndCapture()
		{
			_capture.Cancel();
			IsCapturing = false;
			RemapNote = "";
		}

		//The write: the pad control that was pressed replaces this control's
		//binding, through the same ConfigManager path and the same ApplyConfig()
		//call the classic Input page uses (ADR-0255 Consequences). It lands in the
		//slot that already binds the control, else the port's first free slot; with
		//all four taken the sheet refuses and says so rather than overwriting one.
		private void BindCaptured(ushort code, int portIndex, SheetPort port)
		{
			ConsoleType console = CurrentConsole();
			if(portIndex < 0 || PortConfig(console, port.Key) is not ControllerConfig config) {
				return;
			}
			SetupButton button = _capture.Button;
			ushort[] controlPerSlot = new ushort[4];
			for(int i = 0; i < 4; i++) {
				controlPerSlot[i] = ControllerSheetSlotWrite.Field(ControllerSheetSlotWrite.Slot(config, i), button);
			}
			bool[] slotTaken = port.Slots.Select(slot => slot.Length > 0).ToArray();
			if(ControllerSheetRemap.TargetSlot(controlPerSlot, slotTaken) is not int slot) {
				RemapNote = ResourceHelper.GetMessage("ControllerSheetRemapNoSlot");
				return;
			}
			ControllerSheetSlotWrite.SetField(ControllerSheetSlotWrite.Slot(config, slot), button, code);
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			RemapNote = ResourceHelper.GetMessage("ControllerSheetRemapBound", ControllerSheetRemap.ControlLabel(console, button), KeyName(code));
		}
	}
}
