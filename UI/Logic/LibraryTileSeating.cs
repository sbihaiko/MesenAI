using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#1065 (ADR-0264 Decision 9: the sheet stays responsive): seating the grid's
//tiles in the order the library wants, at the cost the library's size allows.
//
//GameLibrary.MaxEntries is 20000 and the canonical-title pass re-seats the grid
//once per 16-title batch, so a pass that costs a walk of the grid PER GAME is
//400 million steps a batch and a frozen sheet. Host-free (ADR-0123), so the
//complexity is pinned by a test that counts the calls rather than by a stopwatch.
public static class LibraryTileSeating
{
	//Walks `order` once, putting each path's tile at the seat it belongs in and
	//creating the ones the grid does not hold yet. A tile already on its seat -
	//the common case, a re-seat that changed one title - costs one read and no
	//search; one that is not is looked for FORWARD from the seat, because every
	//tile behind it is already placed.
	public static void Seat<T>(IList<T> grid, IReadOnlyList<string> order, Func<string, T?> find, Func<string, T> create, Action<int, int> move) where T : class
	{
		for(int at = 0; at < order.Count; at++) {
			T? tile = find(order[at]);
			if(tile is null) {
				grid.Insert(at, create(order[at]));
				continue;
			}
			if(ReferenceEquals(grid[at], tile)) {
				continue;
			}
			int from = at + 1;
			while(from < grid.Count && !ReferenceEquals(grid[from], tile)) {
				from++;
			}
			move(from, at);
		}
	}

	//Whether a tile whose title just changed from `oldTitle` still sits between
	//its neighbours, in log(n) reads of a grid that was in order a moment ago.
	//False also when the grid is not in the order it should be (the tile is not
	//where its old title says): the caller then re-seats the whole grid, which is
	//always right and only sometimes needed.
	public static bool KeepsSeat<T>(IList<T> grid, T tile, string oldTitle, Func<T, string> titleOf, Func<T, string> pathOf, StringComparer pathComparer) where T : class
	{
		string path = pathOf(tile);
		int low = 0;
		int high = grid.Count - 1;
		int seat = -1;
		while(low <= high) {
			int mid = (low + high) / 2;
			T item = grid[mid];
			if(ReferenceEquals(item, tile)) {
				seat = mid;
				break;
			}
			if(LibraryGridOrder.Compare(titleOf(item), pathOf(item), oldTitle, path, pathComparer) < 0) {
				low = mid + 1;
			} else {
				high = mid - 1;
			}
		}
		if(seat < 0) {
			return false;
		}
		string title = titleOf(tile);
		bool afterPrevious = seat == 0 || LibraryGridOrder.Compare(titleOf(grid[seat - 1]), pathOf(grid[seat - 1]), title, path, pathComparer) < 0;
		bool beforeNext = seat == grid.Count - 1 || LibraryGridOrder.Compare(title, path, titleOf(grid[seat + 1]), pathOf(grid[seat + 1]), pathComparer) < 0;
		return afterPrevious && beforeNext;
	}
}
