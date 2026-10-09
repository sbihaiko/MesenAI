using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#925 (ruling on #916, ADR-0255): the pad's own light is the second of the
//player colour's two places - the port label is the first. A pad lights in the
//colour of the player port its keys live under, read off the ports themselves
//(SheetPort, the sheet's own reading), so moving a pad's keys to another port
//moves its colour with them and there is no second table of "who is P1".
//Host-free (ADR-0123); the window's 1 s poll hands the result to the core,
//whose light call is a no-op everywhere but macOS (GCController.light).

//One player colour, as the bytes the core's light call takes.
public readonly record struct PlayerColor(byte R, byte G, byte B);

//One pad's light: the pad's index in the host's connected list, its key block
//(ControllerDevices.PadBlock) and the colour of the port that holds it.
public sealed record PadLight(uint PadIndex, int Block, PlayerColor Color);

public static class PadLights
{
	//The player palette, indexed by SheetPort.ColorIndex: play blue, red,
	//orange, share green. The sheet's PLAYERS rows paint their brushes from it.
	public static IReadOnlyList<PlayerColor> PlayerColors { get; } = new[] {
		new PlayerColor(0x00, 0x7A, 0xFF),
		new PlayerColor(0xFF, 0x3B, 0x30),
		new PlayerColor(0xFF, 0x9F, 0x0A),
		new PlayerColor(0x34, 0xC7, 0x59)
	};

	public static PlayerColor ColorOf(int colorIndex) => PlayerColors[Math.Clamp(colorIndex, 0, PlayerColors.Count - 1)];

	//The lights for the connected pads (`padBlocks[i]` is pad i's key block). A
	//pad lights in the first port holding it in any slot (HoldsDevice, the same
	//rule a rebind uses); a pad no port holds has no player colour and is left
	//out, so its light is not touched.
	public static IReadOnlyList<PadLight> Plan(IReadOnlyList<SheetPort> ports, IReadOnlyList<int> padBlocks)
	{
		List<PadLight> lights = new();
		for(int i = 0; i < padBlocks.Count; i++) {
			foreach(SheetPort port in ports) {
				if(ControllerSheetPorts.HoldsDevice(port, padBlocks[i])) {
					lights.Add(new PadLight((uint)i, padBlocks[i], ColorOf(port.ColorIndex)));
					break;
				}
			}
		}
		return lights;
	}
}

//What the poll actually sends. The plan is rebuilt every second, and a light
//that did not change is not re-sent (each send is an output report to the pad).
//A change in the connected count resets that: the host's list renumbers and a
//reconnected pad comes back with its own default light, so everything goes out
//again.
public sealed class PadLightSync
{
	private IReadOnlyList<PadLight> _sent = Array.Empty<PadLight>();
	private int _connected = -1;

	public IReadOnlyList<PadLight> Due(IReadOnlyList<PadLight> plan, int connected)
	{
		if(connected != _connected) {
			_sent = Array.Empty<PadLight>();
			_connected = connected;
		}
		List<PadLight> due = new();
		foreach(PadLight light in plan) {
			if(!Contains(_sent, light)) {
				due.Add(light);
			}
		}
		_sent = plan;
		return due;
	}

	private static bool Contains(IReadOnlyList<PadLight> lights, PadLight light)
	{
		foreach(PadLight sent in lights) {
			if(sent == light) {
				return true;
			}
		}
		return false;
	}
}
