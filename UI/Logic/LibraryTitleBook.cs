using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#1038 review findings 1 and 2 (ADR-0264 Decisions 4 and 7): the title each
//library game is shown AND searched by. The scan names a game by its cleaned
//file name; the canonical-title pass later learns the database's own name for
//it. Both live here, keyed by path, so the grid, the search box and the order
//read ONE answer instead of each holding a copy the pass would have to chase.
public sealed class LibraryTitleBook
{
	private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

	//Records the title the pass resolved for one path.
	public void Resolve(string path, string title) => _resolved[path] = title;

	//The title the player reads: the canonical one once resolved, the cleaned
	//file name until then.
	public string TitleOf(LibraryEntry entry) =>
		_resolved.TryGetValue(entry.Path, out string? title) ? title : entry.Title;

	//The items the query keeps, in their given order, matched against the title
	//the player reads and never against the file name behind it.
	public IReadOnlyList<T> Search<T>(IEnumerable<T> items, Func<T, LibraryEntry> entryOf, string? query)
	{
		List<LibrarySearchItem<T>> searchable = new();
		foreach(T item in items) {
			searchable.Add(new LibrarySearchItem<T>(TitleOf(entryOf(item)), item));
		}
		List<T> kept = new();
		foreach(LibrarySearchItem<T> match in LibrarySearch.Filter(searchable, query)) {
			kept.Add(match.Payload);
		}
		return kept;
	}
}
