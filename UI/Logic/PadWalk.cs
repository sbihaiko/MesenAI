using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1107 (spec #1102 slice 2, ADR-0256 stop rule, PRD Part B §13.3 rule 9): what
//one pad-only walk over a Play surface observed, and the judgement over it.
//The live walk (UI.HeadlessTests/PlayPadWalkTests) produces the observation
//from a real MainWindow; the rule is pure so UI.Tests proves each failure
//fails with a doctored observation, no host or core needed.
//A control the walk saw, by identity: Key is the control itself (compared by
//reference), Label only names it in a sentence. Two controls with one Name are
//two entries, so an unreachable twin cannot hide behind a reached one.
public sealed record PadWalkControl(object Key, string Label);

public sealed record PadWalkObservation(
	string Surface,
	bool IsRoot,
	IReadOnlyCollection<PadWalkControl> Interactive,
	IReadOnlyCollection<PadWalkControl> Reached,
	bool? BackLeft,
	IReadOnlyList<(string Focus, IReadOnlyList<PlayBarEntry>? Declared, bool CoverFocused)> BarByFocus,
	IReadOnlySet<PlayAction> Available,
	//Labels of the controls a pad press moved the focus to outside the surface root.
	IReadOnlyCollection<string>? FocusOutside = null);

public static class PadWalk
{
	//The four failures, as sentences that name the surface and the culprit.
	//An off-bar surface (Declared null) is not a failure here: it is a known gap
	//that PlayPadWalkTests.KnownBarGaps lists by name.
	public static List<string> Judge(PadWalkObservation o)
	{
		List<string> problems = new();
		foreach(string name in o.Interactive.Select(c => c.Label).Except(o.Reached.Select(c => c.Label))) {
			problems.Add($"{o.Surface}: {name} is not reachable from the pad (reached: {string.Join(", ", o.Reached.Select(c => c.Label))})");
		}
		bool declaresBack = o.BarByFocus.Any(b => b.Declared?.Any(e => e.Action == PlayAction.Back) == true);
		if(!o.IsRoot && o.BackLeft != true) {
			problems.Add($"{o.Surface}: Back does not leave the surface");
		}
		if(declaresBack && o.BackLeft != true) {
			problems.Add($"{o.Surface}: the action bar names Back but B does not leave");
		}
		foreach((string focus, IReadOnlyList<PlayBarEntry>? declared, bool cover) in o.BarByFocus) {
			foreach(PlayBarEntry entry in declared ?? Array.Empty<PlayBarEntry>()) {
				//Favorite is per focus (a cover has it, a search box does not); the rest
				//is a property of the surface.
				bool has = entry.Action == PlayAction.Favorite ? cover : o.Available.Contains(entry.Action);
				if(entry.Action != PlayAction.Back && !has) {
					problems.Add($"{o.Surface}: with {focus} focused the bar names {entry.Action} ({entry.LabelKey}) but the surface has no such action");
				}
			}
		}
		return problems;
	}
}
