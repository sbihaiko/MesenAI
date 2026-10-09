using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1065 (ADR-0264 Decision 9): re-seating the grid is linear in the library,
	//not quadratic. The cost is counted in calls on the grid, which is the only
	//thing that does not depend on the machine running the test.
	public class LibraryTileSeatingTests
	{
		//GameLibrary.MaxEntries, the size the sheet has to survive.
		private const int Entries = 20000;

		private sealed class Tile
		{
			public Tile(string title, string path) { Title = title; Path = path; }
			public string Title { get; set; }
			public string Path { get; }
		}

		//A list that counts what a walk asks of it.
		private sealed class CountingGrid : IList<Tile>
		{
			private readonly List<Tile> _items;
			public int Reads;
			public int IndexOfCalls;
			public int Moves;

			public CountingGrid(IEnumerable<Tile> items) { _items = new List<Tile>(items); }

			public Tile this[int index] { get { Reads++; return _items[index]; } set => _items[index] = value; }
			public int Count => _items.Count;
			public bool IsReadOnly => false;
			public int IndexOf(Tile item) { IndexOfCalls++; return _items.IndexOf(item); }
			public void Insert(int index, Tile item) => _items.Insert(index, item);
			public void Move(int from, int to) { Moves++; Tile tile = _items[from]; _items.RemoveAt(from); _items.Insert(to, tile); }
			public void RemoveAt(int index) => _items.RemoveAt(index);
			public void Add(Tile item) => _items.Add(item);
			public void Clear() => _items.Clear();
			public bool Contains(Tile item) => _items.Contains(item);
			public void CopyTo(Tile[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
			public bool Remove(Tile item) => _items.Remove(item);
			public IEnumerator<Tile> GetEnumerator() => _items.GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
			public List<Tile> Snapshot() => new(_items);
		}

		private static List<Tile> Library() =>
			Enumerable.Range(0, Entries).Select(i => new Tile($"Game {i:D5}", $"/roms/{i:D5}.nes")).ToList();

		private static List<string> OrderOf(IEnumerable<Tile> tiles) =>
			tiles.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Path, StringComparer.Ordinal).Select(t => t.Path).ToList();

		private static void Seat(CountingGrid grid, List<Tile> all, List<string> order)
		{
			Dictionary<string, Tile> byPath = all.ToDictionary(t => t.Path, StringComparer.Ordinal);
			LibraryTileSeating.Seat(grid, order, p => byPath.GetValueOrDefault(p), p => throw new InvalidOperationException(p), grid.Move);
		}

		[Fact]
		public void A_grid_already_in_order_is_seated_without_a_move_or_a_search()
		{
			List<Tile> all = Library();
			CountingGrid grid = new(all);

			Seat(grid, all, OrderOf(all));

			Assert.Equal(0, grid.Moves);
			Assert.Equal(0, grid.IndexOfCalls);
			Assert.True(grid.Reads <= Entries, $"{grid.Reads} reads for {Entries} seats");
		}

		[Fact]
		public void One_title_moving_costs_one_move_and_a_linear_walk()
		{
			List<Tile> all = Library();
			CountingGrid grid = new(all);
			all[15000].Title = "A first";

			Seat(grid, all, OrderOf(all));

			Assert.Equal(1, grid.Moves);
			Assert.Equal(0, grid.IndexOfCalls);
			Assert.Same(all[15000], grid.Snapshot()[0]);
			Assert.True(grid.Reads <= 3 * Entries, $"{grid.Reads} reads for {Entries} seats");
		}

		[Fact]
		public void A_rename_that_keeps_its_seat_is_found_in_log_n_reads()
		{
			List<Tile> all = Library();
			CountingGrid grid = new(all);
			string old = all[12345].Title;
			all[12345].Title = "Game 12345 Deluxe";

			bool keeps = LibraryTileSeating.KeepsSeat(grid, all[12345], old, t => t.Title, t => t.Path, StringComparer.Ordinal);

			Assert.True(keeps);
			Assert.True(grid.Reads <= 40, $"{grid.Reads} reads");
		}

		[Fact]
		public void A_rename_that_leaves_its_seat_is_reported()
		{
			List<Tile> all = Library();
			CountingGrid grid = new(all);
			string old = all[12345].Title;
			all[12345].Title = "A first";

			Assert.False(LibraryTileSeating.KeepsSeat(grid, all[12345], old, t => t.Title, t => t.Path, StringComparer.Ordinal));
		}
	}
}
