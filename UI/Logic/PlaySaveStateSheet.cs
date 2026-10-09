using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#909 (PRD Part B §13.5.2 W-P4): the Save states row opens ONE grid over the
//game, not the two-button sheet that launched today's separate save and load
//grids. Every slot carries its own *Save here* and *Load*, so a state is one
//press away in either direction - and *Load* is disabled while the slot holds
//nothing, because an empty slot has nothing to load.
//
//The grid is the ten manual slots plus the auto-save, the same slots today's
//grid shows. The auto-save is the core's to write, never the player's, so that
//row offers *Load* alone.

public enum SaveSlotAction
{
	SaveHere,
	Load
}

public enum SaveSlotKind
{
	Manual,
	AutoSave
}

//One row of the grid, as the rules see it: which slot, whether it holds a state
//(and when it was written, which is the age line the wireframe keeps).
public sealed record SaveStateSlotRow(int Slot, SaveSlotKind Kind, bool HasState, DateTime? Written);

public static class SaveStateSheet
{
	public const int ManualSlots = 10;

	//The slot the core's auto-save writes; the same number the classic grid's
	//load list gives it (RecentGamesViewModel).
	public const int AutoSaveSlot = ManualSlots + 1;

	//What one row offers, in the order the grid draws them. The auto-save has no
	//*Save here*: the player cannot overwrite the state the core keeps for them.
	public static IReadOnlyList<SaveSlotAction> Actions(SaveSlotKind kind)
	{
		return kind == SaveSlotKind.AutoSave
			? new[] { SaveSlotAction.Load }
			: new[] { SaveSlotAction.SaveHere, SaveSlotAction.Load };
	}

	//An action is offered by an empty slot only when it does not need a state:
	//*Save here* writes one, *Load* has nothing to read - the disabled control
	//the wireframe's "Load disabled on an empty slot" asks for.
	public static bool IsEnabled(SaveSlotAction action, bool hasState)
	{
		return action == SaveSlotAction.SaveHere || hasState;
	}

	//Where the sheet opens with the key (or the pad) on: the newest slot that
	//holds a state, so returning to a game is one press; with no state at all,
	//the first slot - its *Save here* is the only useful thing on screen.
	public static int FocusSlot(IReadOnlyList<SaveStateSlotRow> rows)
	{
		int best = -1;
		DateTime newest = DateTime.MinValue;
		for(int i = 0; i < rows.Count; i++) {
			if(rows[i].HasState && rows[i].Written.HasValue && rows[i].Written!.Value > newest) {
				newest = rows[i].Written!.Value;
				best = i;
			}
		}
		return best < 0 ? 0 : best;
	}
}
