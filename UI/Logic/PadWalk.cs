using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//#1107 (spec #1102 slice 2, ADR-0256 stop rule, PRD Part B §13.3 rule 9): what
//one pad-only walk over a Play surface observed, and the judgement over it.
//The live walk (UI.HeadlessTests/PlayPadWalkTests) produces the observation
//from a real MainWindow; the rule is pure so UI.Tests proves each failure
//fails with a doctored observation, no host or core needed.
//A control the walk saw, by the label a player would point at. #1154: the label
//IS its identity, and the only one the rule reads. The walk lists a control per
//INSTANCE, and a surface that rebuilds its page under the pad (the Settings strip
//replaces the page a press moves away from) hands it the same control again as a
//new instance - so an identity compare reports that rebuild as a pad gap while
//the pad did land on that control. Two controls with one label are one answer to
//"can the pad reach this", which is what the walk is for.
public sealed record PadWalkControl(string Label);

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
	//
	//#1154: reachability is asked by LABEL - the set of control kinds the surface
	//shows against the set the pad landed on - and by nothing else, for the reason
	//PadWalkControl carries. That is why this rule belongs here and not in the
	//headless suite: it needs no window, so UI.Tests hands it a doctored
	//observation and CI runs it (PadWalkJudgeTests).
	public static List<string> Judge(PadWalkObservation o)
	{
		List<string> problems = new();
		HashSet<string> reached = new(o.Reached.Select(c => c.Label));
		foreach(PadWalkControl control in o.Interactive.Where(c => !reached.Contains(c.Label))) {
			problems.Add($"{o.Surface}: {control.Label} is not reachable from the pad (reached: {string.Join(", ", o.Reached.Select(c => c.Label))})");
		}
		foreach(string label in o.FocusOutside ?? Array.Empty<string>()) {
			problems.Add($"{o.Surface}: the pad moved the focus outside the surface to {label}");
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
