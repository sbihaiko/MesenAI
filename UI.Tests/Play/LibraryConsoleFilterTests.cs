using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests
{
	//#1034 (ADR-0264 Decision 5): the console filter of the Play library sheet.
	//The filter set is derived from the entries the scan actually found, so the
	//player can never cycle onto a filter that holds nothing, and it composes
	//with search rather than replacing it - `mario` under NES is Decision 5's
	//own worked example.
	//
	//The seam under test is the host-free module, not the sheet: the entries
	//here are plain titles and consoles, and no folder, window or core is
	//involved (ADR-0123).
	public class LibraryConsoleFilterTests
	{
		private sealed record Entry(string Title, RomConsole Console);

		private static List<Entry> Entries(params (string Title, RomConsole Console)[] rows)
		{
			return rows.Select(row => new Entry(row.Title, row.Console)).ToList();
		}

		//The sheet's own call shape: the console filter is applied first and the
		//search predicate second, neither overriding the other.
		private static List<Entry> Filtered(List<Entry> entries, RomConsole? selected, string? search = null)
		{
			return LibraryConsoleFilter.Apply(
				entries,
				selected,
				entry => entry.Console,
				search is null ? null : entry => entry.Title.ToLowerInvariant().Contains(search));
		}

		[Fact]
		public void All_comes_first_and_the_consoles_present_follow()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.GameBoy });

			Assert.Equal(new RomConsole?[] { null, RomConsole.Nes, RomConsole.GameBoy }, options);
		}

		[Fact]
		public void A_console_with_no_entry_is_not_an_option()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.Nes });

			Assert.Equal(new RomConsole?[] { null, RomConsole.Nes }, options);
			Assert.DoesNotContain(RomConsole.GameBoy, options);
			Assert.DoesNotContain(RomConsole.GameGear, options);
		}

		//The order is the product's, not the entry list's: a library whose scan
		//happens to walk the Game Gear folder first still lists NES first.
		[Fact]
		public void The_product_order_holds_whatever_order_the_entries_arrived_in()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] {
				RomConsole.GameGear, RomConsole.Nes, RomConsole.MasterSystem, RomConsole.GameBoyColor
			});

			Assert.Equal(new RomConsole?[] {
				null, RomConsole.Nes, RomConsole.GameBoyColor, RomConsole.MasterSystem, RomConsole.GameGear
			}, options);
		}

		[Fact]
		public void Next_from_the_last_console_wraps_to_All()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.GameBoy });

			Assert.Null(LibraryConsoleFilter.Next(options, RomConsole.GameBoy));
		}

		[Fact]
		public void Previous_from_All_wraps_to_the_last_console()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.GameBoy });

			Assert.Equal(RomConsole.GameBoy, LibraryConsoleFilter.Previous(options, null));
		}

		//The same stale selection, one pad press later: the row holds a console the
		//set no longer lists, so the ring resolves it to All (index 0) and steps
		//off All - RB lands on the first console, LB wraps to the last. Both
		//directions are asserted, since the fallback is the only thing keeping a
		//removed folder's selection from walking off the row.
		[Fact]
		public void Next_from_a_console_the_set_no_longer_holds_steps_off_All()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.MasterSystem });

			Assert.DoesNotContain(RomConsole.GameBoy, options);
			Assert.Equal(RomConsole.Nes, LibraryConsoleFilter.Next(options, RomConsole.GameBoy));
			Assert.Equal(LibraryConsoleFilter.Next(options, null), LibraryConsoleFilter.Next(options, RomConsole.GameBoy));
		}

		[Fact]
		public void Previous_from_a_console_the_set_no_longer_holds_wraps_to_the_last_console()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.MasterSystem });

			Assert.DoesNotContain(RomConsole.GameBoy, options);
			Assert.Equal(RomConsole.MasterSystem, LibraryConsoleFilter.Previous(options, RomConsole.GameBoy));
			Assert.Equal(LibraryConsoleFilter.Previous(options, null), LibraryConsoleFilter.Previous(options, RomConsole.GameBoy));
		}

		//The rule itself, at its own seam: a selection outside the set is All.
		//Everything else here reads through it, so it is worth pinning directly.
		[Fact]
		public void Resolve_maps_a_selection_outside_the_set_to_All()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.MasterSystem });

			Assert.Null(LibraryConsoleFilter.Resolve(options, RomConsole.GameBoy));
			Assert.Null(LibraryConsoleFilter.Resolve(options, RomConsole.Unknown));
			Assert.Null(LibraryConsoleFilter.Resolve(options, null));
			Assert.Equal(RomConsole.Nes, LibraryConsoleFilter.Resolve(options, RomConsole.Nes));
			Assert.Equal(RomConsole.MasterSystem, LibraryConsoleFilter.Resolve(options, RomConsole.MasterSystem));
		}

		//A library of one console (and no games at all) still has the All row, and
		//cycling it is the pad pressing LB/RB on a filter that cannot change -
		//staying put, not walking off the end.
		[Fact]
		public void Cycling_a_single_option_set_stays_put()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(System.Array.Empty<RomConsole>());

			Assert.Equal(new RomConsole?[] { null }, options);
			Assert.Null(LibraryConsoleFilter.Next(options, null));
			Assert.Null(LibraryConsoleFilter.Previous(options, null));
		}

		[Fact]
		public void An_entry_naming_no_console_is_not_counted()
		{
			IReadOnlyList<RomConsole?> options = LibraryConsoleFilter.Options(new[] { RomConsole.Nes, RomConsole.Unknown, RomConsole.Unknown });

			Assert.Equal(new RomConsole?[] { null, RomConsole.Nes }, options);
			Assert.DoesNotContain(RomConsole.Unknown, options);
		}

		//Decision 5's worked example: `mario` under the NES filter finds Super
		//Mario Bros. 3 and not the Game Boy's Super Mario Land - both halves are
		//asserted, so a filter that ignored search (or a search that ignored the
		//filter) fails here.
		[Fact]
		public void The_console_filter_and_the_search_narrow_together()
		{
			List<Entry> entries = Entries(
				("Super Mario Bros. 3", RomConsole.Nes),
				("Super Mario Land", RomConsole.GameBoy),
				("Castlevania", RomConsole.Nes));

			Assert.Equal(new[] { "Super Mario Bros. 3" },
				Filtered(entries, RomConsole.Nes, "mario").Select(entry => entry.Title));
			Assert.Equal(new[] { "Super Mario Bros. 3", "Castlevania" },
				Filtered(entries, RomConsole.Nes).Select(entry => entry.Title));
			Assert.Equal(new[] { "Super Mario Bros. 3", "Super Mario Land" },
				Filtered(entries, null, "mario").Select(entry => entry.Title));
		}

		//A selection the rescan invalidated: the player selected Game Boy, the
		//folder was then removed and the library rescanned, so `Options` no longer
		//lists Game Boy. The stale selection must read as All - the row cannot even
		//show it, and Decision 5 forbids landing on a filter that holds nothing.
		[Fact]
		public void A_stale_selection_after_a_rescan_returns_every_entry()
		{
			List<Entry> entries = Entries(("Contra", RomConsole.Nes), ("Castlevania", RomConsole.Nes));

			Assert.DoesNotContain(RomConsole.GameBoy, LibraryConsoleFilter.Options(entries.Select(entry => entry.Console)));

			Assert.Equal(new[] { "Contra", "Castlevania" },
				Filtered(entries, RomConsole.GameBoy).Select(entry => entry.Title));
		}

		//All is the absence of the narrowing, not a claim that every entry names a
		//console: an archive's console is not knowable from its name
		//(RomFileKinds/RomConsoleKinds), and it is openable all the same - dropping
		//it under All would make a zipped game unreachable in the library.
		[Fact]
		public void All_keeps_an_entry_that_names_no_console()
		{
			List<Entry> entries = Entries(("contra (1988).zip", RomConsole.Unknown), ("Contra", RomConsole.Nes));

			Assert.Equal(2, Filtered(entries, null).Count);
			Assert.Equal(new[] { "Contra" }, Filtered(entries, RomConsole.Nes).Select(entry => entry.Title));
		}
	}
}
