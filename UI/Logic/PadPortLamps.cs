using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//ADR-0249 (W-S1) and ADR-0255: the four port lamps the shell's status line
//shows beside its sentence - one per pad port, so an arcade cabinet's player
//can see at a glance, with no keyboard and no mouse, which of the four ports
//has a pad on it. Host-free (BCL only, ADR-0123): the window's poll hands it
//the Core's connected count and its name lookup, and every rule that turns
//those two into lamps lives here, exercised in UI.Tests/Shell with no core.

//One lamp: which port (1-based), whether a pad is on it, and the pad's name
//("" when the backend cannot answer - the count is the truth, the name is a
//nicety).
public sealed record PadPortLamp(int Port, bool IsLit, string Name)
{
	//"P1". The number the strip prints beside the lamp, so it reads without
	//hovering.
	public string Label => "P" + Port;

	//The hover text: the pad's own name, or null when the port is empty or the
	//backend could not answer, so an empty port opens no tooltip.
	public string? Tip => IsLit && Name.Length > 0 ? Name : null;
}

//The strip the status line draws: always exactly four lamps, whatever the
//backend reports, and how many pads there were beyond the four ports.
public sealed record PadPortStrip(IReadOnlyList<PadPortLamp> Lamps, int OverflowPads)
{
	public bool HasOverflow => OverflowPads > 0;

	//True when another strip would draw exactly the same thing - the same lamps,
	//lit the same way, with the same names, and the same overflow. The 1 s poll
	//builds a fresh strip every second (record equality compares the lamps list by
	//reference, so it can never say "unchanged"), and this is what keeps the poll
	//from re-assigning - and so rebuilding the strip's visuals, dropping a hover -
	//when nothing moved.
	public bool DrawsSameAs(PadPortStrip other)
	{
		if(OverflowPads != other.OverflowPads || Lamps.Count != other.Lamps.Count) {
			return false;
		}
		for(int i = 0; i < Lamps.Count; i++) {
			if(Lamps[i] != other.Lamps[i]) {
				return false;
			}
		}
		return true;
	}
}

public static class PadPortLamps
{
	//A NES has two ports, an SMS two, a GB one - but the cabinet shows four,
	//and this strip is the cabinet's, not any one console's.
	public const int Ports = 4;

	//"1 pad beyond the four ports" / "{0} pads beyond the four ports"
	//(resources.en.xml): the note the strip shows when the backend reports
	//more pads than there are lamps.
	public const string OverflowOneKey = "ShellPadPortsOverflowOne";
	public const string OverflowManyKey = "ShellPadPortsOverflowMany";

	public static string OverflowKey(int overflowPads) => overflowPads <= 1 ? OverflowOneKey : OverflowManyKey;

	//The four lamps for a reported pad count. Lamp i is lit while i < connected,
	//and the backend's name for pad i is asked only for a lit lamp - a dim port
	//has no pad to name. Extra pads never add a fifth lamp; the count beyond the
	//ports is reported back for the strip's note.
	public static PadPortStrip Build(uint connected, Func<uint, string?> nameOf)
	{
		List<PadPortLamp> lamps = new(Ports);
		for(int i = 0; i < Ports; i++) {
			bool lit = (uint)i < connected;
			lamps.Add(new PadPortLamp(i + 1, lit, lit ? nameOf((uint)i) ?? "" : ""));
		}
		int overflow = connected > Ports ? (int)Math.Min(connected - Ports, (uint)int.MaxValue) : 0;
		return new PadPortStrip(lamps, overflow);
	}

	//The state before the first poll: four dim lamps, which is also what a
	//machine with no pad shows.
	public static PadPortStrip Empty { get; } = Build(0, _ => "");
}
