using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//One row of the sheet's EXTRA BUTTONS section (ADR-0255 slice 4): an emulator
	//action and the spare pad control the player gave it. The action's own name is
	//the core's enum name translated (the classic Input page's own converter), so
	//the two surfaces call the same action the same thing.
	public partial class ControllerSheetExtraRow : ObservableObject
	{
		public EmulatorShortcut Action { get; }
		public string Label { get; }

		[ObservableProperty] public partial string BoundName { get; set; } = "";
		[ObservableProperty] public partial bool HasBinding { get; set; }
		//This is the row the sheet is waiting on ("Press a control...").
		[ObservableProperty] public partial bool Armed { get; set; }

		public ControllerSheetExtraRow(EmulatorShortcut action, string label)
		{
			Action = action;
			Label = label;
		}
	}

	//ADR-0255 slice 4 (PRD Part B §13.5.2 W-P17): EXTRA BUTTONS - the pad's spare
	//buttons carrying the emulator's own actions ("retroceder, avancar,
	//compartilhar, home"). It is a filtered view of the one shortcut list: a row's
	//binding *is* that shortcut's PadBinding slot, so this section and the classic
	//Input page edit the same object through the same ConfigManager/ApplyConfig()
	//pair, and there is no second store to keep in step.
	//
	//The rules are host-free (ControllerSheetExtra: which actions are listed, what
	//a bind and a clear do) and the capture is slice 3's own machine
	//(ControllerSheetCapture), armed with an extra-button target instead of a
	//console control - the release-first rule, the navigation-control refusal and
	//the pad bridge's single IsControllerCapturing flag all come with it, so
	//pressing a row here behaves exactly as pressing a REMAP row does.
	public partial class ControllerSheetViewModel
	{
		[ObservableProperty] public partial IReadOnlyList<ControllerSheetExtraRow> ExtraRows { get; private set; } = Array.Empty<ControllerSheetExtraRow>();
		[ObservableProperty] public partial bool ShowExtra { get; private set; }
		[ObservableProperty] public partial string ExtraNote { get; private set; } = "";

		//The rows the section last built. The set is fixed (ControllerSheetExtra's
		//list), so it is built once and only re-published when the player's
		//settings.json loses an entry for an action it had - a *fresh* list, because
		//re-assigning the one already bound notifies nobody (ObservableProperty skips
		//an equal reference) and the ItemsControl would keep the containers it had
		//(the trap slice 3's REMAP rows were found in).
		private IReadOnlyList<ControllerSheetExtraRow> _extraRows = Array.Empty<ControllerSheetExtraRow>();

		//Where the capture's refusal goes: the section that armed it. Both notes
		//sit under their own list, and a row must not carry a line about a press it
		//did not take.
		private string RefusalNote
		{
			set {
				if(_capture.Target.IsExtra) {
					ExtraNote = value;
				} else {
					RemapNote = value;
				}
			}
		}

		//The adapter between the config's own type and ControllerSheetExtra's
		//host-free pair: the entry a row edits, and its spare slot as the pair. Kept
		//to two one-line helpers here so every rule about what the slot *becomes*
		//lives in the host-free file and is tested there.
		private static ShortcutKeyInfo? FindShortcut(List<ShortcutKeyInfo> shortcuts, EmulatorShortcut action)
		{
			foreach(ShortcutKeyInfo entry in shortcuts) {
				if(entry.Shortcut == action) {
					return entry;
				}
			}
			return null;
		}

		private static (ushort KeyCode, int? ThresholdPercent)? SlotOf(ShortcutKeyInfo? entry)
		{
			PadShortcutBinding? pad = entry?.PadBinding;
			return ControllerSheetExtra.Slot(pad is null || pad.IsEmpty, pad?.KeyCode ?? 0, pad?.ThresholdPercent);
		}

		//One write shape for both the bind and the clear, so the config's "none" is
		//written in exactly one place and the rule that decided it (host-free) is
		//the only thing that varies.
		private static void WriteSlot(ShortcutKeyInfo entry, (ushort KeyCode, int? ThresholdPercent)? slot)
		{
			entry.PadBinding = slot is { } bound
				? new PadShortcutBinding() { KeyCode = bound.KeyCode, ThresholdPercent = bound.ThresholdPercent }
				: null;
		}

		//One 60 Hz read of the EXTRA BUTTONS section. The gate is the same one
		//REMAP uses - a pad to press and a console with a player port - because a
		//binding is made by *pressing a control on the pad*, and the sheet is the
		//Play door's surface for a loaded game. Without a player port there is
		//nothing this section could be about (ADR-0255 Decision 4: dark, like REMAP).
		public void RefreshExtra(IReadOnlyList<SheetPort> ports)
		{
			List<ShortcutKeyInfo> shortcuts = ConfigManager.Config.Preferences.ShortcutKeys;
			bool show = Pad != null && ports.Count > 0;
			ShowExtra = show;
			if(!show) {
				//A pad that goes away takes the capture with it here too, and only
				//when this section armed it: ending the other section's capture from
				//here would be a second place deciding when REMAP's mode is over.
				if(_capture.IsCapturing && _capture.Target.IsExtra) {
					EndCapture();
				}
				ExtraNote = "";
				return;
			}

			List<ControllerSheetExtraRow> rows = new();
			foreach(EmulatorShortcut action in ControllerSheetExtra.Actions) {
				//A settings.json hand-edited down to a subset has no entry for the
				//action: a row for it would be a control with nothing to write to.
				if(FindShortcut(shortcuts, action) is null) {
					continue;
				}
				rows.Add(new ControllerSheetExtraRow(action, ResourceHelper.GetEnumText(action)));
			}
			if(!SameExtraRows(_extraRows, rows)) {
				//A fresh list, so the containers are rebuilt when the row set moves.
				_extraRows = rows;
				ExtraRows = rows;
			}

			ApplyExtraRows(shortcuts);
		}

		private static bool SameExtraRows(IReadOnlyList<ControllerSheetExtraRow> a, IReadOnlyList<ControllerSheetExtraRow> b)
		{
			if(a.Count != b.Count) {
				return false;
			}
			for(int i = 0; i < a.Count; i++) {
				if(a[i].Action != b[i].Action || a[i].Label != b[i].Label) {
					return false;
				}
			}
			return true;
		}

		private void ApplyExtraRows(List<ShortcutKeyInfo> shortcuts)
		{
			foreach(ControllerSheetExtraRow row in _extraRows) {
				(ushort KeyCode, int? ThresholdPercent)? slot = SlotOf(FindShortcut(shortcuts, row.Action));
				row.BoundName = ControllerSheetExtra.Describe(slot, KeyName, ResourceHelper.GetMessage("ControllerSheetRemapUnbound"));
				row.HasBinding = slot is not null;
				//The armed row is the EXTRA one that armed the capture - and only
				//that: a REMAP arm carries no action, so reading Shortcut alone would
				//light the first row for a press aimed at a console control.
				row.Armed = _capture.IsCapturing && _capture.Target.IsExtra && _capture.Target.Shortcut == row.Action;
			}
		}

		//A row was picked: wait for a pad control, through slice 3's own machine.
		public void ArmExtra(EmulatorShortcut action)
		{
			if(!ShowExtra) {
				return;
			}
			_capture.Arm(CaptureTarget.Extra(action));
			IsCapturing = true;
			//One capture, one line saying so: the other section's note goes with it.
			RemapNote = "";
			ExtraNote = ResourceHelper.GetMessage("ControllerSheetExtraArm", ResourceHelper.GetEnumText(action));
			RefreshExtra(BuildPorts());
		}

		//The row's clear control: the spare button goes, and the action falls back
		//to its keyboard combination alone (which the pad slot never replaced).
		public void ClearExtra(EmulatorShortcut action)
		{
			if(!ShowExtra) {
				return;
			}
			if(FindShortcut(ConfigManager.Config.Preferences.ShortcutKeys, action) is not ShortcutKeyInfo entry) {
				return;
			}
			WriteSlot(entry, ControllerSheetExtra.Clear());
			//The same tail every write on this sheet uses: push the config to the
			//core, then persist it.
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			ExtraNote = ResourceHelper.GetMessage("ControllerSheetExtraCleared", ResourceHelper.GetEnumText(action));
			RefreshExtra(BuildPorts());
		}

		//The bind: the captured pad control becomes this action's spare button,
		//through the same ConfigManager path and the same ApplyConfig() call the
		//classic Input page uses (ADR-0255 Consequences). What the slot becomes -
		//and what happens to a threshold - is host-free (ControllerSheetExtra.Bind),
		//so the section is not a second place that decides it.
		private void BindExtraCaptured(ushort code)
		{
			if(FindShortcut(ConfigManager.Config.Preferences.ShortcutKeys, _capture.Target.Shortcut) is not ShortcutKeyInfo entry) {
				return;
			}
			WriteSlot(entry, ControllerSheetExtra.Bind(SlotOf(entry), code));
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			ExtraNote = ResourceHelper.GetMessage("ControllerSheetExtraBound", ResourceHelper.GetEnumText(entry.Shortcut), KeyName(code));
			RefreshExtra(BuildPorts());
		}
	}
}
