using System;

namespace Mesen.Logic;

//The four directions the grid selection moves in - the same four the pad's
//preset and the player's console mapping both drive (ADR-0256 Decision 4).
public enum GridDirection
{
	Up,
	Down,
	Left,
	Right
}

//#897: StateGrid's selection arithmetic, lifted out of the control so it is
//testable without a host (UI/Logic is dual-compiled by UI.Tests). The rules are
//the control's own, moved verbatim - the extraction changes nothing - plus the
//#897 fix for the one shape they got wrong.
//
//The grid draws two shapes. The classic 4 x 3 (Advanced's game-selection and
//Save/Load screens, and Play's Save/Load slots) steps by a row and wraps at the
//edges. The Play home's row of tiles (ADR-0249 W-P2) is a single row whose
//column count follows the window, not the entry count - five columns and two
//tiles is a real shape there - which is what the classic wrap was never written
//for.
public static class GridSelection
{
	//The entry a selection index points at, always in 0..count-1.
	//
	//The load path reads Entries[SlotOf(SelectedIndex, Entries.Count)]. The
	//control used to write Entries[SelectedIndex % Entries.Count], and C#'s %
	//keeps the sign of the dividend, so a negative SelectedIndex - which the old
	//MoveUp could answer, and which any host can set - became Entries[-1], an
	//IndexOutOfRangeException on Confirm (#897). Folding into 0..count-1 is this
	//read's own guard: no negative and no past-the-end index reaches the list,
	//whatever set SelectedIndex.
	public static int SlotOf(int index, int count)
	{
		if(count <= 0) {
			return 0;
		}
		return ((index % count) + count) % count;
	}

	//The index a direction moves to, given the current selection, the entry
	//count, the column count and the ROW count. InitGrid keeps both counts at
	//least 1 on a live grid; the guards keep a direct caller from dividing by
	//zero.
	//
	//The row count is a parameter and not something derived from the other two on
	//purpose. "Is there anything above me" is a fact about the layout, and the
	//obvious inference - entries <= columns means one row - is wrong for the grid
	//that paged: the Play home's row of tiles takes up to five columns and pages
	//the rest, so with eight tiles it is still one row and the inference would
	//hand back the classic wrap for it (a jump to whatever the modulo lands on)
	//instead of the truth, which is that nothing above it moved.
	public static int Next(int current, GridDirection direction, int count, int columns, int rows)
	{
		if(count <= 0) {
			return 0;
		}
		if(columns < 1) {
			columns = 1;
		}
		if(rows < 1) {
			rows = 1;
		}
		//Normalize before moving. A negative or past-the-end index (set from
		//outside, or left behind by a pre-fix move) is folded into range first,
		//so every branch below answers an in-range index (#897).
		current = SlotOf(current, count);

		int next;
		switch(direction) {
			case GridDirection.Left:
				//Wrap: the entry before the first is the last.
				next = current == 0 ? count - 1 : current - 1;
				break;

			case GridDirection.Right:
				//Wrap: the entry after the last is the first.
				next = (current + 1) % count;
				break;

			case GridDirection.Down:
				//A step down is one row - columns entries - while a row remains;
				//past the last row it wraps to the top of the same column, clamped
				//to the last entry for a short final row.
				next = current + columns < count
					? current + columns
					: Math.Min(current % columns, count - 1);
				break;

			case GridDirection.Up:
				//A single row has nothing above it: Up stays put, whatever the
				//counts say. The classic wrap below is the 4x3 grid's, and on the
				//Play home's tiles (rowCount = 1, columns from the window, entries
				//from the library) it answered a *negative* index whenever the row
				//held fewer entries than it had columns - five columns and two
				//tiles is 2 - 5 - which the load path read as Entries[-1] (#897).
				//Down still reaches the next page there, as the arrows do.
				if(rows <= 1) {
					next = current;
					break;
				}
				//Top row wraps to the bottom row, same column.
				next = current < columns ? count - (columns - (current % columns)) : current - columns;
				break;

			default:
				next = current;
				break;
		}

		//The contract, stated once: whatever the branch answered, the caller gets
		//an index into the entries. The classic branches are in range by their own
		//arithmetic; this is what makes that a property of the function rather
		//than a property of four separate proofs.
		return SlotOf(next, count);
	}
}
