using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1107 (spec #1102 slice 2, ADR-0256 stop rule, PRD Part B §13.3 rule 9): what
//one pad-only walk over a Play surface observed, and the judgement over it.
//The live walk (UI.HeadlessTests/PlayPadWalkTests) produces the observation
//from a real MainWindow; the rule is pure so UI.Tests proves each failure
//fails with a doctored observation, no host or core needed.
public sealed record PadWalkObservation(
	string Surface,
	bool IsRoot,
	IReadOnlyCollection<string> Interactive,
	IReadOnlyCollection<string> Reached,
	bool? BackLeft,
	IReadOnlyList<(string Focus, IReadOnlyList<PlayBarEntry>? Declared)> BarByFocus,
	IReadOnlySet<PlayAction> Available);

public static class PadWalk
{
	//The three failures, as sentences that name the surface and the culprit.
	//An off-bar surface (Declared null) is not a failure here: it is a known gap
	//that PlayPadWalkTests.KnownBarGaps lists by name.
	public static List<string> Judge(PadWalkObservation o)
	{
		List<string> problems = new();
		foreach(string name in o.Interactive.Except(o.Reached)) {
			problems.Add($"{o.Surface}: {name} is not reachable from the pad (reached: {string.Join(", ", o.Reached)})");
		}
		bool declaresBack = o.BarByFocus.Any(b => b.Declared?.Any(e => e.Action == PlayAction.Back) == true);
		if(!o.IsRoot && o.BackLeft != true) {
			problems.Add($"{o.Surface}: Back does not leave the surface");
		}
		if(declaresBack && o.BackLeft != true) {
			problems.Add($"{o.Surface}: the action bar names Back but B does not leave");
		}
		foreach((string focus, IReadOnlyList<PlayBarEntry>? declared) in o.BarByFocus) {
			foreach(PlayBarEntry entry in declared ?? Array.Empty<PlayBarEntry>()) {
				if(entry.Action != PlayAction.Back && !o.Available.Contains(entry.Action)) {
					problems.Add($"{o.Surface}: with {focus} focused the bar names {entry.Action} ({entry.LabelKey}) but the surface has no such action");
				}
			}
		}
		return problems;
	}
}
