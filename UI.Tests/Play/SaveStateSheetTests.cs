using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#909 (PRD Part B §13.5.2 W-P4): W-P4's Save states sheet is one grid. These are
//its host-free rules; the crossing into the window (the grid the sheet draws,
//the disabled Load, a save landing in the slot) is pinned in
//UI.HeadlessTests/SaveStatesSheetTests.cs with the real core.
public class SaveStateSheetTests
{
	[Fact]
	public void A_manual_slot_offers_save_here_and_load()
	{
		Assert.Equal(new[] { SaveSlotAction.SaveHere, SaveSlotAction.Load }, SaveStateSheet.Actions(SaveSlotKind.Manual));
	}

	//The auto-save belongs to the core: the row offers *Load* alone, so no press
	//on the grid can overwrite the state the emulator keeps for the player.
	[Fact]
	public void The_auto_save_row_offers_load_alone()
	{
		Assert.Equal(new[] { SaveSlotAction.Load }, SaveStateSheet.Actions(SaveSlotKind.AutoSave));
	}

	[Fact]
	public void Load_is_disabled_on_a_slot_with_no_state_and_save_here_is_not()
	{
		Assert.False(SaveStateSheet.IsEnabled(SaveSlotAction.Load, hasState: false));
		Assert.True(SaveStateSheet.IsEnabled(SaveSlotAction.SaveHere, hasState: false));
	}

	[Fact]
	public void Both_actions_are_enabled_once_the_slot_holds_a_state()
	{
		Assert.True(SaveStateSheet.IsEnabled(SaveSlotAction.Load, hasState: true));
		Assert.True(SaveStateSheet.IsEnabled(SaveSlotAction.SaveHere, hasState: true));
	}

	//The sheet opens with the key on the newest state, so returning to the game
	//is one press; ties go to the lower slot (the grid's own order).
	[Fact]
	public void The_sheet_opens_on_the_newest_slot_that_holds_a_state()
	{
		DateTime t = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
		List<SaveStateSlotRow> rows = new() {
			new(1, SaveSlotKind.Manual, false, null),
			new(2, SaveSlotKind.Manual, true, t),
			new(3, SaveSlotKind.Manual, true, t.AddMinutes(10)),
			new(4, SaveSlotKind.Manual, false, null)
		};
		Assert.Equal(2, SaveStateSheet.FocusSlot(rows));

		rows[3] = new(4, SaveSlotKind.Manual, true, t.AddMinutes(5));
		Assert.Equal(2, SaveStateSheet.FocusSlot(rows));
	}

	[Fact]
	public void An_empty_grid_opens_on_the_first_slot()
	{
		List<SaveStateSlotRow> rows = Enumerable.Range(1, SaveStateSheet.ManualSlots)
			.Select(slot => new SaveStateSlotRow(slot, SaveSlotKind.Manual, false, null)).ToList();
		rows.Add(new SaveStateSlotRow(SaveStateSheet.AutoSaveSlot, SaveSlotKind.AutoSave, false, null));

		Assert.Equal(0, SaveStateSheet.FocusSlot(rows));
	}

	//The auto-save is a real slot on the grid, and the only way to reach it is
	//*Load* - it is the newest state far more often than not.
	[Fact]
	public void An_auto_save_alone_focuses_its_own_row()
	{
		List<SaveStateSlotRow> rows = Enumerable.Range(1, SaveStateSheet.ManualSlots)
			.Select(slot => new SaveStateSlotRow(slot, SaveSlotKind.Manual, false, null)).ToList();
		rows.Add(new SaveStateSlotRow(SaveStateSheet.AutoSaveSlot, SaveSlotKind.AutoSave, true, new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc)));

		Assert.Equal(SaveStateSheet.ManualSlots, SaveStateSheet.FocusSlot(rows));
	}
}
