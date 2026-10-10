using System;
using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//The pressed-key overlay of the GUI test hook (the GUI test hook ADR, PR #1202,
//item 4). A press is a code held down in the host pressed-key set - the set every
//GUI consumer polls - for a number of the application's own ticks, or of emulated
//frames while the emulated clock advances. Both layers overlay physical input; a
//pad that is plugged in does not break a run.
//
//Time here is never the host's: Advance is called once per UI tick by the pad
//bridge, and the frame counter is the core's.
public sealed class TestHookKeys
{
	private sealed record Hold(ushort Code, long UntilTick, long? UntilFrame, long? StartFrame, string? Key);

	private readonly Action<ushort, bool> _setKey;
	private readonly Func<string, ushort> _codeOf;
	private readonly Func<long?> _frames;
	private readonly Action<ushort, bool>? _raise;
	private readonly Func<int, PadFamily>? _familyOf;
	private readonly List<Hold> _holds = new();

	public long Tick { get; private set; }

	//setKey: put a code in / take it out of the host set. codeOf: the backend's
	//key-name lookup (0 = unknown). frames: the emulated frame counter while the
	//clock advances (a game loaded and not paused), null in every other state.
	//raise: delivers a literal key (by its backend code) to the GUI as KeyDown / KeyUp, because
	//the GUI keyboard reads Avalonia key events, not the pressed set; null = set only.
	//familyOf: the family the pad on a device index was connected with (#1281,
	//TestHookPads); null = Xbox, the spelling every backend here defines first.
	public TestHookKeys(Action<ushort, bool> setKey, Func<string, ushort> codeOf, Func<long?> frames,
		Action<ushort, bool>? raise = null, Func<int, PadFamily>? familyOf = null)
	{
		_setKey = setKey;
		_codeOf = codeOf;
		_frames = frames;
		_raise = raise;
		_familyOf = familyOf;
	}

	public long Frames => _frames() ?? 0;

	//Null when the press was taken, otherwise why it was not.
	public string? Press(int pad, string button, int? ticks, int? frames)
	{
		return StartHold(CodeOfButton(pad, button), Unknown(pad, button), ticks, frames, null);
	}

	//`pad.hold`: the button goes down and stays down for that many ticks. The unit
	//is always the tick and never the emulated frame (#1281): a hold is a gesture of
	//the clock the GUI itself runs on, and that one never freezes - which is why a
	//hold is expressible while a game runs, where pad.press demands frames.
	public string? HoldDown(int pad, string button, int ticks)
	{
		ushort code = CodeOfButton(pad, button);
		if(code == 0) {
			return Unknown(pad, button);
		}
		if(ticks < 1) {
			return "a hold is a duration in ticks (at least 1), not " + ticks;
		}
		//The hold goes in on the tick counter whatever the emulated clock is doing:
		//a hold has no frame form, so the counter pad.press insists on (and which
		//freezes on a pause) is not what ends this one.
		_holds.Add(new Hold(code, Tick + ticks, null, null, null));
		_setKey(code, true);
		return null;
	}

	//`pad.release`: the button goes up now, before the hold it is under has run out
	//- what a two-button gesture (hold A, press B) is written with. A button no hold
	//is keeping down is put up anyway, the way letting go of a button nobody pressed
	//is nothing: a release never fails a step. Only a name this backend has no code
	//for is an error, and that is a wrong step rather than a no-op.
	public string? Release(int pad, string button)
	{
		ushort code = CodeOfButton(pad, button);
		if(code == 0) {
			return Unknown(pad, button);
		}
		for(int i = _holds.Count - 1; i >= 0; i--) {
			if(_holds[i].Code == code) {
				Hold hold = _holds[i];
				_holds.RemoveAt(i);
				RaiseUp(hold);
			}
		}
		_setKey(code, false);
		return null;
	}

	private static string Unknown(int pad, string button) => "unknown button " + button + " on pad " + (pad + 1);

	//A literal key name, as the backend names it (ADR-0272 item 4, key.*).
	public string? PressKey(string key, int? ticks, int? frames)
	{
		return StartHold(_codeOf(key), "unknown key " + key, ticks, frames, key);
	}

	private string? StartHold(ushort code, string unknown, int? ticks, int? frames, string? key)
	{
		if(code == 0) {
			return unknown;
		}
		long? now = _frames();
		if(now.HasValue) {
			if(frames is not int heldFrames || heldFrames < 1) {
				return "the emulated clock is running: write the press in frames";
			}
			_holds.Add(new Hold(code, 0, now.Value + heldFrames, now.Value, key));
		} else {
			if(ticks is not int heldTicks || heldTicks < 1) {
				return "the emulated clock is not running: write the press in ticks";
			}
			_holds.Add(new Hold(code, Tick + heldTicks, null, null, key));
		}
		_setKey(code, true);
		if(key is not null) {
			_raise?.Invoke(code, true);
		}
		return null;
	}

	//ADR-0272 item 4: a navigation control is named the way the pad bridge names
	//it (PadNavControls.NamesOf, the first name the backend defines), because the
	//host defines "Pad1 Up" on one platform and "Joy1 DPad Up" on another. Any
	//other name is a literal key name on the pad, or on the joystick of that number.
	//
	//`pad` is a device index - 0 is the pad in the hand, the same index the wire
	//carries (#1281) - and the family is the one that pad was connected with, so a
	//script that plugged a PlayStation pad in has its buttons read in DirectInput's
	//spelling first. The backend names devices from 1 in both spellings
	//("Pad1 A", "Joy1 But2"), which is why the number written here is pad + 1.
	private ushort CodeOfButton(int pad, string button)
	{
		PadFamily family = _familyOf?.Invoke(pad) ?? PadFamily.Xbox;
		foreach(PadNavAction action in PadNavControls.Navigation) {
			if(string.Equals(action.ToString(), button, StringComparison.OrdinalIgnoreCase)) {
				foreach(string name in PadNavControls.NamesOf(family, pad, action)) {
					ushort named = _codeOf(name);
					if(named != 0) {
						return named;
					}
				}
				return 0;
			}
		}
		string xbox = "Pad" + (pad + 1) + " " + button;
		string ps4 = "Joy" + (pad + 1) + " " + button;
		foreach(string name in family == PadFamily.Xbox ? new[] { xbox, ps4 } : new[] { ps4, xbox }) {
			ushort code = _codeOf(name);
			if(code != 0) {
				return code;
			}
		}
		return 0;
	}

	//One UI tick: counted, and every hold that has run its length is let go.
	public void Advance()
	{
		Tick++;
		long? now = _frames();
		for(int i = _holds.Count - 1; i >= 0; i--) {
			Hold hold = _holds[i];
			//A frames hold also ends when the clock stops (pause, the game closing) or
			//the counter restarts below where the hold began (a reset, a new game):
			//a counter that no longer counts up would never reach its target.
			bool done = hold.UntilFrame is long until
				? now is not long current || current >= until || current < hold.StartFrame
				: Tick >= hold.UntilTick;
			if(done) {
				_holds.RemoveAt(i);
				//A code held twice stays down until its longest hold ends.
				if(!_holds.Exists(h => h.Code == hold.Code)) {
					_setKey(hold.Code, false);
				}
				RaiseUp(hold);
			}
		}
	}

	public void ReleaseAll()
	{
		foreach(Hold hold in _holds) {
			_setKey(hold.Code, false);
			RaiseUp(hold);
		}
		_holds.Clear();
	}

	//Every key.press raised a KeyDown, so every one that ends raises its KeyUp.
	private void RaiseUp(Hold hold)
	{
		if(hold.Key is not null) {
			_raise?.Invoke(hold.Code, false);
		}
	}
}
