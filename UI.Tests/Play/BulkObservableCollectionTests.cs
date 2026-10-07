using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1038 review finding 2 (ADR-0264 Decision 1): the grid is re-sorted when the
	//canonical titles land, and the grid listening to the collection is a
	//NON-virtualized WrapPanel - every change notification rebuilds a container and
	//invalidates layout. A reorder expressed as one `Move` per out-of-place tile is
	//therefore up to MaxEntries notifications in a single turn: a library of 20 000
	//ROMs whose order changed posts 20 000 of them, and the grid the player is
	//looking at freezes. "Hashing never blocks the grid" cannot hold that way.
	//
	//So the reorder is one change, and this class pins that: the rule is host-free
	//(System.Collections.ObjectModel, no Avalonia), which is why it is pinned here
	//rather than through the sheet. Both cases assert the SAME instances come out,
	//because the tiles are what the grid's template binds to and what the focus
	//ring is read back from - a reorder that rebuilt the items would lose both.
	public class BulkObservableCollectionTests
	{
		[Fact]
		public void A_reorder_reaches_the_grid_as_one_change()
		{
			object first = new();
			object second = new();
			object third = new();
			BulkObservableCollection<object> tiles = new() { first, second, third };
			List<NotifyCollectionChangedEventArgs> changes = new();
			tiles.CollectionChanged += (_, change) => changes.Add(change);

			tiles.ReplaceAll(new[] { third, first, second });

			NotifyCollectionChangedEventArgs change = Assert.Single(changes);
			Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
			Assert.Equal(new[] { third, first, second }, tiles.ToArray());
			Assert.Equal(3, tiles.Count);
		}

		//The scale the finding is about. Twenty thousand tiles is MaxEntries - the
		//cap ADR-0264 Decision 9 names - and a reorder of that library has to be one
		//notification, not one per tile: the count is asserted rather than the
		//duration, so the case says why it would freeze the grid instead of timing
		//whatever machine it ran on.
		[Fact]
		public void A_librarys_worth_of_tiles_is_still_one_change()
		{
			BulkObservableCollection<int> tiles = new();
			for(int i = 0; i < GameLibrary.MaxEntries; i++) {
				tiles.Add(i);
			}
			int changes = 0;
			tiles.CollectionChanged += (_, _) => changes++;

			tiles.ReplaceAll(Enumerable.Range(0, GameLibrary.MaxEntries).Reverse().ToList());

			Assert.Equal(1, changes);
			Assert.Equal(GameLibrary.MaxEntries, tiles.Count);
			Assert.Equal(GameLibrary.MaxEntries - 1, tiles[0]);
			Assert.Equal(0, tiles[GameLibrary.MaxEntries - 1]);
		}

		//The order presented is the order held - the whole list, not a patch of it.
		//A reorder that left an item behind would be a library with a game missing,
		//which no notification count would catch.
		[Fact]
		public void The_reorder_holds_exactly_the_items_it_was_given()
		{
			BulkObservableCollection<string> tiles = new() { "a", "b", "c" };

			tiles.ReplaceAll(new[] { "c", "b" });

			Assert.Equal(new[] { "c", "b" }, tiles.ToArray());
			//And an empty order is an empty grid rather than the old one kept.
			tiles.ReplaceAll(new string[0]);

			Assert.Empty(tiles);
		}
	}
}
