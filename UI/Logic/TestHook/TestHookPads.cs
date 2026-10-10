using System;
using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//The pads a GUI test run simulates (#1281, `pad.connect` / `pad.disconnect`): a
//script hot-plugs a pad on a port, which is what the port lamps and the pad-loss
//pause are read from, without a controller and without an OS input path.
//
//The count the application reads is the script's own set ONCE a script has
//connected or disconnected a pad - a hot-plug cannot be overlaid on a real pad
//the way a press can, since "no pad on this port" is the state itself. A run
//that never touches these two actions reads the backend's real count, so a pad
//plugged into the machine still lights its lamp.
//
//Host-free (BCL only): the wiring hands it the application's own count function
//and the window reads this one back, which is what makes both halves testable
//with no core and no pad.
public sealed class TestHookPads
{
	private readonly Dictionary<int, PadFamily> _connected = new();
	private readonly Func<uint> _backendCount;

	//backendCount: what the application would read with no script in the way
	//(InputApi.GetConnectedGamepadCount in the application).
	public TestHookPads(Func<uint>? backendCount = null)
	{
		_backendCount = backendCount ?? (() => 0);
	}

	//True from the first connect or disconnect of the run: a script that
	//disconnected the last pad means zero pads, never the machine's own count.
	public bool Simulating { get; private set; }

	public uint Count => Simulating ? (uint)_connected.Count : _backendCount();

	//The family a simulated pad's buttons are read in, Xbox for a pad nobody
	//connected: the spelling every backend this app ships on defines first
	//(macOS and Linux name every pad "Pad", Windows defines both).
	public PadFamily FamilyOf(int index) => _connected.TryGetValue(index, out PadFamily family) ? family : PadFamily.Xbox;

	//Null when the pad is connected, otherwise why it was not. The index is the
	//wire's device index: 0 is the pad in the hand, 1 the second pad.
	public string? Connect(int index, string? family)
	{
		if(index < 0) {
			return "pad.connect needs a pad index (0 = the pad in hand), not " + index;
		}
		PadFamily? known = Parse(family);
		if(known is not PadFamily pad) {
			return "unknown family " + family + " (xbox or playstation)";
		}
		_connected[index] = pad;
		Simulating = true;
		return null;
	}

	//Null when the pad is gone, otherwise why it was not. A pad that was never
	//connected is already gone: a step that disconnects one is not a failure.
	public string? Disconnect(int index)
	{
		if(index < 0) {
			return "pad.disconnect needs a pad index (0 = the pad in hand), not " + index;
		}
		_connected.Remove(index);
		Simulating = true;
		return null;
	}

	//null and "" are the default family rather than an error: a step that names no
	//family is asking for the pad the machine has, not for a pad it invented.
	private static PadFamily? Parse(string? family)
	{
		return family switch {
			null or "" or "xbox" => PadFamily.Xbox,
			"playstation" => PadFamily.Ps4,
			_ => null
		};
	}
}
