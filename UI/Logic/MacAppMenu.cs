using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//Issue #1008 (PRD §13.5 W-S2, ADR-0250 Decisions 3 and 4): the macOS app menu
//is titled after the product and follows the HIG order — About first,
//Settings… near the top, Quit last. Avalonia appends its standard items
//(Services, Hide <Name>, Hide Others, Show All, Quit) to the same menu, and
//when that happens before App adds About and Settings… they land after Quit.
//Generic over the item type so the rule stays host-free.
public static class MacAppMenu
{
	//Application.Name: Avalonia builds "Hide <Name>" from it.
	public const string AppName = "MesenAI";

	public static IEnumerable<T> Arrange<T>(IEnumerable<T> current, T about, T settings, Func<T> separator, Func<T, bool> isSeparator)
	{
		EqualityComparer<T> eq = EqualityComparer<T>.Default;
		List<T> rest = new();
		foreach(T item in current) {
			if(eq.Equals(item, about) || eq.Equals(item, settings)) {
				continue;
			}
			//No leading or doubled separators: one is added below, between Settings… and the rest.
			if(isSeparator(item) && (rest.Count == 0 || isSeparator(rest[^1]))) {
				continue;
			}
			rest.Add(item);
		}
		if(rest.Count > 0 && isSeparator(rest[^1])) {
			rest.RemoveAt(rest.Count - 1);
		}

		List<T> result = new() { about, separator(), settings };
		if(rest.Count > 0) {
			result.Add(separator());
			result.AddRange(rest);
		}
		return result;
	}
}
