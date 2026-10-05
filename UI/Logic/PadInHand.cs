using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//ADR-0256 Decision 6 ("Segue o controle na mão"): with two pads connected the
//app has to know which one the player is holding, both to navigate with it and
//to name the control in "B for the menu" instead of the other pad's circle.
//Nothing in the host answers "which pad is in hand" - the player never says so
//- so it is read off the last pad button pressed, the same signal W-P15's
//detector already uses to notice a pad at all.
//
//PadId carries the family beside the device because the two cannot be told
//apart from a code: each family numbers its blocks from its own base, so 0x2000
//is joystick 1 to a Windows DirectInput pad and pad 17 to a backend that
//numbers seventeen of them, and only that backend knows which one it is looking
//at. This is the same reason Core/Shared/ShortcutKeyRules.h has its PadFamilies
//supplied by the caller rather than derived from the code, and the answer
//belongs to the same layer.
public readonly record struct PadId(int Device, PadFamily Family);

public sealed class PadInHand
{
	private HashSet<ushort> _previous = new();

	public PadId? Current { get; private set; }
	public bool HasPad => Current is not null;

	//pressed: every key down now (InputApi.GetPressedKeys); padOf: which pad a
	//code came from, or null for a key no pad sent - the keyboard, the mouse, or
	//a pad whose family the app cannot tell, which is a real answer and not a
	//failure to report.
	//
	//Only a *new* press moves it: a button held down stays the pad in hand
	//across ticks, so a second pad cannot take over by being held while the
	//first is already down, and the family is re-asked on each press so a
	//preset the player just changed is picked up without a restart.
	public void OnPressed(IReadOnlyCollection<ushort> pressed, Func<ushort, PadId?> padOf)
	{
		PadId? newest = null;
		ushort newestKey = 0;
		foreach(ushort key in pressed) {
			if(_previous.Contains(key)) {
				continue;
			}
			if(padOf(key) is not PadId pad) {
				continue;
			}
			//Two pads pressing in the same tick: the lowest code wins, so the
			//answer never depends on the order the host enumerates a set in.
			if(newest is null || key < newestKey) {
				newest = pad;
				newestKey = key;
			}
		}

		_previous = new HashSet<ushort>(pressed);
		if(newest is PadId winner) {
			Current = winner;
		}
	}
}
