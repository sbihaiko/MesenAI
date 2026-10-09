using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mesen.Logic;

//One row the library search box reads: the text it matches against - a ROM's
//title - and the caller's own object, which the filter never touches. The
//payload is generic because the search rule has no opinion about what a game is:
//the caller hands in its own entries and gets the same ones back.
public readonly record struct LibrarySearchItem<T>(string Title, T Payload);

//#1033 (ADR-0264 Decision 4): the match rule behind the flat library's search
//box. `zel` finds *The Legend of Zelda*, `pokemon` finds *Pokémon Red*, and a
//file name's dump tags cannot be searched for at all.
//
//Host-free (ADR-0123): BCL text only - no disk, no Avalonia, no native core - so
//the rule the player gets is the rule UI.Tests runs (LibrarySearchTests).
//
//The rule, in one place, because a search box is only as good as the answer it
//gives on one keystroke:
//
// 1. **Tags are not the game's name.** ADR-0264 Decision 7 has the title cleaner
//    strip the extension and the region / revision / dump tags carried in
//    parentheses and brackets, and Decision 4 says those tags therefore cannot
//    match. This rule does that stripping itself rather than trusting its caller:
//    the same question is asked of a raw file name and of an already-clean
//    title, and the answer has to be the same either way - a search box that
//    finds `Castlevania (U) [!]` by `u` on one code path and not on another is
//    how a player ends up with the whole region on screen.
//
//    The FILE EXTENSION is not stripped here, and that is deliberate: it is the
//    title cleaner's job (Decision 7), and a rule that guessed it would eat
//    titles - `Dr. Mario` split on its last dot loses `Mario`, which is the only
//    word a player would search for.
// 2. **Case and accents are typing, not meaning.** Both sides are folded to
//    lowercase with the combining marks removed, so a player on a keyboard
//    without `é` finds the same games as one with it.
// 3. **Whitespace is not meaning either.** Runs of whitespace collapse to one
//    space and the ends are trimmed, on both sides: `the  legend` is a typo, not
//    a different game.
// 4. **An empty query is not a filter.** A blank query (or none at all) keeps
//    everything - the sheet opens with the whole library, which is what "no
//    search yet" means.
//
//The order and the payloads are the caller's: the surviving entries come back in
//the order they went in, and the payload object is the one that was handed over,
//not a copy. A grid whose tiles move under the cursor as a letter is typed is
//the failure this rule exists to prevent.
public static class LibrarySearch
{
	//Does this title answer this query? The one-word form of `Filter`, for a
	//caller that already has the title in hand (the #1032 sheet deciding whether
	//to draw a tile it has just scanned).
	public static bool Matches(string? title, string? query)
	{
		string needle = Fold(query);
		if(needle.Length == 0) {
			//Nothing was typed yet. A blank query is the absence of a search, so
			//every title is still in it - including a title this rule cannot read.
			return true;
		}
		return Fold(StripTags(title)).Contains(needle, StringComparison.Ordinal);
	}

	//The items this query keeps, in the order they were given. The query is
	//folded once rather than once per item: this runs on every keystroke over a
	//library that ADR-0264 Decision 9 lets grow to twenty thousand entries.
	public static IReadOnlyList<LibrarySearchItem<T>> Filter<T>(IEnumerable<LibrarySearchItem<T>> items, string? query)
	{
		string needle = Fold(query);
		List<LibrarySearchItem<T>> kept = new();
		foreach(LibrarySearchItem<T> item in items) {
			if(needle.Length == 0 || Fold(StripTags(item.Title)).Contains(needle, StringComparison.Ordinal)) {
				kept.Add(item);
			}
		}
		return kept;
	}

	//A title reduced to the only thing the match cares about: lowercase, no
	//combining marks, no tags, single spaces.
	//
	//A title and a query go through the same folding but NOT through the same tag
	//stripping, and the asymmetry is load-bearing. A title carries tags - they are
	//a property of the file it was read from - so they come off before the match.
	//The query does not: the player typed it, and `(u)` typed into a search box is
	//a query that matches nothing, not a query that means "everything". Stripping
	//the query as well would empty it and hand back the whole library, which is
	//the opposite of what the player asked for.
	private static string Fold(string? text)
	{
		if(string.IsNullOrWhiteSpace(text)) {
			return "";
		}

		//FormD splits `é` into `e` + a combining acute, so dropping the
		//non-spacing marks leaves the plain letter. Done on the whole string at
		//once rather than per character: an accented letter typed on some
		//keyboards already arrives decomposed, and a per-character pass would
		//keep the mark that follows it.
		string decomposed = text.Normalize(NormalizationForm.FormD);
		StringBuilder folded = new(decomposed.Length);
		bool spacePending = false;
		foreach(char c in decomposed) {
			if(CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) {
				continue;
			}
			if(char.IsWhiteSpace(c)) {
				//Collapsed, not kept: `Super  Mario` and `Super Mario` are one
				//title, and a query typed with a double space still finds it.
				spacePending = folded.Length > 0;
				continue;
			}
			if(spacePending) {
				folded.Append(' ');
				spacePending = false;
			}
			folded.Append(char.ToLowerInvariant(c));
		}
		return folded.ToString();
	}

	//The title with every parenthesised and bracketed run removed.
	//
	//Written as a scan rather than a regular expression because the rule is small
	//and its edge cases are the point: `(U)`, `[!]`, `(Rev A)`, `[T-Port]`,
	//`(Japan) (En)` and an unbalanced `(U` a truncated file name can carry all
	//have to come out the same way, and a scan says so in one place. Scanning
	//also keeps the file free of a runtime-compiled pattern, which the
	//AOT-compatible UI build (IsAotCompatible, UI.csproj) has no room for.
	//
	//An unclosed tag runs to the end of the title: there is no `)` coming, and
	//the alternative - treating `(` as a letter - would let a truncated dump tag
	//answer a query, which is the one thing tags must never do.
	private static string StripTags(string? text)
	{
		if(string.IsNullOrEmpty(text)) {
			return "";
		}
		StringBuilder kept = new(text.Length);
		char closer = ')';
		bool inTag = false;
		foreach(char c in text) {
			if(inTag) {
				if(c == closer) {
					inTag = false;
				}
				continue;
			}
			switch(c) {
				case '(':
					inTag = true;
					closer = ')';
					break;
				case '[':
					inTag = true;
					closer = ']';
					break;
				default:
					kept.Append(c);
					break;
			}
		}
		return kept.ToString();
	}
}
