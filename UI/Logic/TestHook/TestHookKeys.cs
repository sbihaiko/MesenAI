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
	private sealed record Hold(ushort Code, long UntilTick, long? UntilFrame, long? StartFrame);

	private readonly Action<ushort, bool> _setKey;
	private readonly Func<string, ushort> _codeOf;
	private readonly Func<long?> _frames;
	private readonly List<Hold> _holds = new();

	public long Tick { get; private set; }

	//setKey: put a code in / take it out of the host set. codeOf: the backend's
	//key-name lookup (0 = unknown). frames: the emulated frame counter while the
	//clock advances (a game loaded and not paused), null in every other state.
	public TestHookKeys(Action<ushort, bool> setKey, Func<string, ushort> codeOf, Func<long?> frames)
	{
		_setKey = setKey;
		_codeOf = codeOf;
		_frames = frames;
	}

	public long Frames => _frames() ?? 0;

	//Null when the press was taken, otherwise why it was not.
	public string? Press(int pad, string button, int? ticks, int? frames)
	{
		return StartHold(CodeOfButton(pad, button), "unknown button " + button + " on pad " + pad, ticks, frames);
	}

	//A literal key name, as the backend names it (ADR-0272 item 4, key.*).
	public string? PressKey(string key, int? ticks, int? frames)
	{
		return StartHold(_codeOf(key), "unknown key " + key, ticks, frames);
	}

	private string? StartHold(ushort code, string unknown, int? ticks, int? frames)
	{
		if(code == 0) {
			return unknown;
		}
		long? now = _frames();
		if(now.HasValue) {
			if(frames is not int heldFrames || heldFrames < 1) {
				return "the emulated clock is running: write the press in frames";
			}
			_holds.Add(new Hold(code, 0, now.Value + heldFrames, now.Value));
		} else {
			if(ticks is not int heldTicks || heldTicks < 1) {
				return "the emulated clock is not running: write the press in ticks";
			}
			_holds.Add(new Hold(code, Tick + heldTicks, null, null));
		}
		_setKey(code, true);
		return null;
	}

	//ADR-0272 item 4: a navigation control is named the way the pad bridge names
	//it (PadNavControls.NamesOf, the first name the backend defines), because the
	//host defines "Pad1 Up" on one platform and "Joy1 DPad Up" on another. Any
	//other name is a literal key name on the pad, or on the joystick of that number.
	private ushort CodeOfButton(int pad, string button)
	{
		foreach(PadNavAction action in PadNavControls.Navigation) {
			if(string.Equals(action.ToString(), button, StringComparison.OrdinalIgnoreCase)) {
				foreach(string name in PadNavControls.NamesOf(PadFamily.Xbox, pad - 1, action)) {
					ushort named = _codeOf(name);
					if(named != 0) {
						return named;
					}
				}
				return 0;
			}
		}
		ushort code = _codeOf("Pad" + pad + " " + button);
		return code != 0 ? code : _codeOf("Joy" + pad + " " + button);
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
			}
		}
	}

	public void ReleaseAll()
	{
		foreach(Hold hold in _holds) {
			_setKey(hold.Code, false);
		}
		_holds.Clear();
	}
}
