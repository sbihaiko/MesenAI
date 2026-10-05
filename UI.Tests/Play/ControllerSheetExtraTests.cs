using Mesen.Config.Shortcuts;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0255 slice 4 (W-P17 EXTRA BUTTONS): the host-free half of "a pad with
	//more buttons than the console needs can carry the emulator's own actions".
	//Which actions the section lists is the one judgement call here, and it is
	//written down in ControllerSheetExtra with the two filters that were rejected;
	//what the tests pin is the rest of the rule - the slot a bind writes, what a
	//bind does to a threshold, and what the section is not.
	public class ControllerSheetExtraTests
	{
		//The section is a *filtered view of the one shortcut list*, so a row's
		//binding is the shortcut's own spare slot and nothing else. The list is
		//short on purpose: every entry is a Player-mode action a player reaches for
		//mid-game, and the console's own devices, the debugger's tools and the
		//Advanced-only view toggles are deliberately out.
		[Fact]
		public void The_section_lists_the_users_own_actions_and_no_duplicates()
		{
			IReadOnlyList<EmulatorShortcut> actions = ControllerSheetExtra.Actions;

			Assert.Equal(actions.Distinct().Count(), actions.Count);
			Assert.Contains(EmulatorShortcut.Rewind, actions);          //"retroceder"
			Assert.Contains(EmulatorShortcut.FastForward, actions);     //"avancar"
			Assert.Contains(EmulatorShortcut.TakeScreenshot, actions);  //"compartilhar"
			Assert.Contains(EmulatorShortcut.ToggleOverlay, actions);   //"home" - the pause menu
			//The save-state pair every other emulator puts on a spare button.
			Assert.Contains(EmulatorShortcut.SaveState, actions);
			Assert.Contains(EmulatorShortcut.LoadState, actions);
		}

		//A row must not offer an action the shortcut list has no entry for: a
		//binding would have nowhere to go. Every EmulatorShortcut below
		//LastValidValue is seeded an entry, so this is the guard for a hand-edited
		//config trimmed to a subset.
		[Fact]
		public void Every_listed_action_is_a_shortcut_the_config_can_hold()
		{
			foreach(EmulatorShortcut action in ControllerSheetExtra.Actions) {
				Assert.True(action < EmulatorShortcut.LastValidValue, action + " is not a real shortcut");
			}
		}

		//An empty slot is null and not a zero code: the config says "none" by the
		//slot being absent (ShortcutKeyInfo.PadBinding), and a zero code would be a
		//binding to a key the pad does not have.
		[Fact]
		public void An_empty_slot_reads_as_unbound()
		{
			Assert.Null(ControllerSheetExtra.Slot(isEmpty: true, keyCode: 0, thresholdPercent: null));
			Assert.Null(ControllerSheetExtra.Slot(isEmpty: true, keyCode: PadKey(0, 0), thresholdPercent: 40));
		}

		[Fact]
		public void A_bound_slot_carries_the_code_and_its_threshold()
		{
			(ushort KeyCode, int? ThresholdPercent)? slot = ControllerSheetExtra.Slot(isEmpty: false, keyCode: PadKey(1, 12), thresholdPercent: 80);
			Assert.Equal(PadKey(1, 12), slot!.Value.KeyCode);
			Assert.Equal(80, slot.Value.ThresholdPercent);
		}

		//The bind rule. A threshold is a property of the *direction*, so re-picking
		//the control that is already in the slot keeps it - the player who set 80%
		//for "Pad1 X+" does not lose it by pressing X+ again.
		[Fact]
		public void Binding_the_same_control_again_keeps_its_threshold()
		{
			(ushort KeyCode, int? ThresholdPercent)? current = (PadKey(0, 16), 80);
			(ushort KeyCode, int? ThresholdPercent) bound = ControllerSheetExtra.Bind(current, PadKey(0, 16));
			Assert.Equal(PadKey(0, 16), bound.KeyCode);
			Assert.Equal(80, bound.ThresholdPercent);
		}

		//A different control starts clean: another stick direction is another
		//threshold the player has not picked yet, and a plain button has nothing to
		//threshold at all - keeping the number would be a field that lies about
		//being in use (PadShortcutBinding).
		[Fact]
		public void Binding_a_different_control_drops_the_threshold()
		{
			(ushort KeyCode, int? ThresholdPercent) bound = ControllerSheetExtra.Bind((PadKey(0, 16), 80), PadKey(0, 17));
			Assert.Equal(PadKey(0, 17), bound.KeyCode);
			Assert.Null(bound.ThresholdPercent);

			(ushort KeyCode, int? ThresholdPercent) button = ControllerSheetExtra.Bind((PadKey(0, 16), 80), PadKey(0, 0));
			Assert.Equal(PadKey(0, 0), button.KeyCode);
			Assert.Null(button.ThresholdPercent);
		}

		[Fact]
		public void Binding_an_empty_slot_starts_clean()
		{
			(ushort KeyCode, int? ThresholdPercent) bound = ControllerSheetExtra.Bind(null, PadKey(3, 5));
			Assert.Equal(PadKey(3, 5), bound.KeyCode);
			Assert.Null(bound.ThresholdPercent);
		}

		//The clear rule: the slot goes back to "none", which is null and not an
		//empty binding - the same shape ShortcutKeyInfo.PadBinding uses, so nothing
		//is written for the shortcut afterwards.
		[Fact]
		public void Clearing_a_row_returns_the_slot_to_none()
		{
			Assert.Null(ControllerSheetExtra.Clear());
		}

		//What a row shows: the host's own name for the code, or the caller's "not
		//bound" text. The name is the backend's - the same string the classic Input
		//page and the capture show - so the two surfaces cannot disagree about what
		//a control is called.
		[Fact]
		public void A_row_shows_the_hosts_name_for_the_bound_control()
		{
			Func<ushort, string> keyName = code => code == PadKey(0, 16) ? "Pad1 X+" : "?";

			Assert.Equal("Pad1 X+", ControllerSheetExtra.Describe((PadKey(0, 16), null), keyName, "Not bound"));
			Assert.Equal("Not bound", ControllerSheetExtra.Describe(null, keyName, "Not bound"));
		}

		//The section's rows are actions, and the sheet's other section's rows are
		//the console's controls. One capture serves both, so the target it is armed
		//with has to say which - including for the one action and the one control
		//that share a zero value (SetupButton.A and EmulatorShortcut.FastForward are
		//both 0), which is why the flag is its own field and not inferred from the
		//two payloads.
		[Fact]
		public void An_arm_says_which_section_it_came_from()
		{
			CaptureTarget remap = CaptureTarget.Remap(SetupButton.A);
			Assert.False(remap.IsExtra);
			Assert.Equal(SetupButton.A, remap.Button);

			CaptureTarget extra = CaptureTarget.Extra(EmulatorShortcut.FastForward);
			Assert.True(extra.IsExtra);
			Assert.Equal(EmulatorShortcut.FastForward, extra.Shortcut);
		}

		//The arm of an EXTRA row goes through the same machine as a REMAP row's, so
		//nothing about the capture itself is second: it waits for the pad to be let
		//go of, then binds the first button that goes down - on the pad the sheet is
		//showing.
		[Fact]
		public void An_extra_arm_uses_slice_threes_capture()
		{
			ControllerSheetCapture capture = new();
			capture.Arm(CaptureTarget.Extra(EmulatorShortcut.Rewind));
			Assert.True(capture.IsCapturing);
			Assert.True(capture.Target.IsExtra);

			//The press that picked the row is released first, exactly as REMAP's is.
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(new[] { PadKey(0, 0) }, Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, _ => false));
			Assert.Equal(CaptureOutcome.Bound, capture.OnTick(new[] { PadKey(0, 4) }, Device0Block, _ => false));
			Assert.Equal(PadKey(0, 4), capture.BoundCode);
			Assert.False(capture.IsCapturing);
			//...and it still knows which action it was for, so the write lands on the
			//row the player picked and not on the other section's.
			Assert.Equal(EmulatorShortcut.Rewind, capture.Target.Shortcut);
		}

		//The navigation refusal is the capture's own (ADR-0256 Decision 4) and the
		//EXTRA arm inherits it: a spare button may not be Confirm, Back or the
		//D-pad, because a player who binds them on a pad with no keyboard behind it
		//is stuck.
		[Fact]
		public void A_navigation_control_is_refused_for_an_extra_row_too()
		{
			ControllerSheetCapture capture = new();
			capture.Arm(CaptureTarget.Extra(EmulatorShortcut.TakeScreenshot));
			Assert.Equal(CaptureOutcome.Waiting, capture.OnTick(Array.Empty<ushort>(), Device0Block, _ => false));

			ushort confirm = PadKey(0, 12);
			Assert.Equal(CaptureOutcome.Refused, capture.OnTick(new[] { confirm }, Device0Block, code => code == confirm));
			Assert.True(capture.IsCapturing);
			Assert.True(capture.Target.IsExtra);
		}

		private const int Device0Block = ControllerDevices.BaseGamepadIndex;

		private static ushort PadKey(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
	}
}
