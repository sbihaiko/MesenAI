using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Mesen.Logic;

//A collection that can be handed a whole new order at once, so a reorder costs
//the grid ONE change notification instead of one per tile that changed place
//(#1038 review finding 2, ADR-0264 Decision 1).
//
//The grid this is the source of is a NON-virtualized WrapPanel: every
//`CollectionChanged` it receives rebuilds a container and invalidates layout, on
//the thread that is drawing it. A reorder written as a `Move` per out-of-place
//tile is therefore up to MaxEntries notifications in a single turn - 20 000 of
//them for the library Decision 9 caps at that size - and the grid the player is
//looking at freezes while the canonical titles land. `ReplaceAll` makes the same
//reorder one `Reset`.
//
//The ITEMS are the ones the caller passed and are never rebuilt: they are what
//the grid's template binds to (`PlayerLibraryTile.Title` is a notifying property
//for exactly this reason) and what the focus ring is read back from, so a reorder
//that replaced them would lose both the titles and the ring.
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
	//The list, in this order, as one change. The caller decides WHETHER to call
	//this - a grid already in this order has nothing to tell the panel, and
	//signalling a Reset it did not need would rebuild every container and drop the
	//ring off the game the player is on.
	public void ReplaceAll(IEnumerable<T> ordered)
	{
		//Materialized first: `ordered` may be a lazy query over this very
		//collection, and clearing the items out from under it would answer with the
		//grid this call is replacing.
		List<T> replacement = ordered as List<T> ?? new List<T>(ordered);

		//The items are swapped underneath and the change is announced ONCE, at the
		//end: Remove-then-Insert per item is the notification storm this class
		//exists to avoid, and `Reset` is the one action that tells a listener "read
		//the whole list again" without naming a place.
		Items.Clear();
		foreach(T item in replacement) {
			Items.Add(item);
		}

		//The two property changes `ObservableCollection` raises for a count that
		//moved, plus the one collection change above. A listener that reads Count or
		//indexes the list is told the same thing a Clear-then-Add would have told
		//it, without the per-item traffic in between.
		OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
		OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
