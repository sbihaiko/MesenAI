using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//P.10 (ADR-0245 §1-§3): the W-P11 Cheats sheet's rules, host-free. The
	//sheet lists the bundled database entries for the loaded copy, filters
	//them by description, falls back to a search by game name when the copy
	//is not in the list, and stores every toggle in the same CheatCodes list
	//the classic cheat window edits (rule 12).
	public class CheatSheetTests
	{
		private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

		private static readonly CheatDbGame Contra = new("Contra (USA)", ContraSha1, new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
			new CheatDbCode("Start with 30 lives", "AAUZGZAP;PEUZTZTP"),
			new CheatDbCode("Invincibility (star effect)", "00B0:FF"),
		});

		private static readonly CheatDbGame ContraJapan = new("Gryzor (Japan)", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", new[] {
			new CheatDbCode("Infinite lives", "0032:03"),
		});

		private static readonly CheatDbGame[] Db = { Contra, ContraJapan, new("Super Contra (USA)", "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", new[] { new CheatDbCode("Rapid fire", "AEUSGZAP") }) };

		private static IReadOnlyList<CheatSheetRow> Rows(IReadOnlyList<StoredCheat> stored, string search = "", bool recordingArt = false, CheatDbGame? game = null, bool anotherCopy = false)
		{
			return CheatSheet.BuildRows(ConsoleType.Nes, game ?? Contra, anotherCopy, stored, recordingArt, search);
		}

		[Fact]
		public void The_loaded_copy_is_found_by_its_cheat_hash()
		{
			Assert.Same(Contra, CheatSheet.FindGameForCopy(Db, ContraSha1));
			//EmuApi.GetRomHash returns upper-case hex, but a lower-case copy still matches.
			Assert.Same(Contra, CheatSheet.FindGameForCopy(Db, ContraSha1.ToLowerInvariant()));
			Assert.Null(CheatSheet.FindGameForCopy(Db, "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD"));
			Assert.Null(CheatSheet.FindGameForCopy(Db, ""));
		}

		[Fact]
		public void Each_database_entry_of_the_copy_is_one_row_with_its_type()
		{
			IReadOnlyList<CheatSheetRow> rows = Rows(Array.Empty<StoredCheat>());

			Assert.Equal(new[] { "Infinite lives - 1P game", "Start with 30 lives", "Invincibility (star effect)" }, rows.Select(r => r.Description));
			Assert.Equal(new[] { CheatType.NesGameGenie, CheatType.NesGameGenie, CheatType.NesCustom }, rows.Select(r => r.Type));
			Assert.All(rows, r => Assert.False(r.IsOn));
			Assert.All(rows, r => Assert.Equal(CheatRowSource.ThisCopy, r.Source));
			//A multi-part database code is stored the way the classic import stores it.
			Assert.Equal("AAUZGZAP" + Environment.NewLine + "PEUZTZTP", rows[1].Codes);
		}

		[Fact]
		public void The_search_filters_rows_by_every_word_of_the_description()
		{
			Assert.Equal(new[] { "Infinite lives - 1P game", "Start with 30 lives" }, Rows(Array.Empty<StoredCheat>(), "lives").Select(r => r.Description));
			Assert.Equal(new[] { "Infinite lives - 1P game" }, Rows(Array.Empty<StoredCheat>(), "  INFINITE   lives ").Select(r => r.Description));
			Assert.Empty(Rows(Array.Empty<StoredCheat>(), "weapon"));
			Assert.Equal(3, Rows(Array.Empty<StoredCheat>(), "   ").Count);
		}

		[Fact]
		public void Turning_a_row_on_adds_it_to_the_stored_list_and_off_keeps_it_disabled()
		{
			IReadOnlyList<StoredCheat> stored = Array.Empty<StoredCheat>();
			CheatSheetRow lives = Rows(stored)[0];

			stored = CheatSheet.Toggle(stored, lives, recordingArt: false);
			StoredCheat added = Assert.Single(stored);
			Assert.Equal(new StoredCheat("Infinite lives - 1P game", CheatType.NesGameGenie, "SZKGPAVG", true), added);
			Assert.True(Rows(stored)[0].IsOn);
			Assert.Equal(1, CheatSheet.CountOn(stored));

			stored = CheatSheet.Toggle(stored, Rows(stored)[0], recordingArt: false);
			//Off keeps the entry (disabled), the same as the classic window's
			//unchecked row, so the two lists never disagree.
			Assert.False(Assert.Single(stored).Enabled);
			Assert.False(Rows(stored)[0].IsOn);
			Assert.Equal(0, CheatSheet.CountOn(stored));
		}

		[Fact]
		public void A_code_imported_by_the_classic_window_is_the_same_row()
		{
			//CheatListWindowViewModel's database import: disabled, codes joined by NewLine.
			IReadOnlyList<StoredCheat> stored = new[] {
				new StoredCheat("Start with 30 lives", CheatType.NesGameGenie, "AAUZGZAP" + Environment.NewLine + "PEUZTZTP", false),
			};

			stored = CheatSheet.Toggle(stored, Rows(stored)[1], recordingArt: false);

			StoredCheat only = Assert.Single(stored);
			Assert.True(only.Enabled);
			Assert.True(Rows(stored)[1].IsOn);
		}

		[Fact]
		public void Codes_you_added_yourself_are_listed_after_the_database_rows()
		{
			IReadOnlyList<StoredCheat> stored = new[] {
				new StoredCheat("My code", CheatType.NesCustom, "0040:09", true),
				new StoredCheat("Infinite lives - 1P game", CheatType.NesGameGenie, "SZKGPAVG", true),
			};

			IReadOnlyList<CheatSheetRow> rows = Rows(stored);

			Assert.Equal(4, rows.Count);
			CheatSheetRow mine = rows[3];
			Assert.Equal("My code", mine.Description);
			Assert.Equal(CheatRowSource.Yours, mine.Source);
			Assert.True(mine.IsOn);
			Assert.Equal(2, CheatSheet.CountOn(stored));

			stored = CheatSheet.Toggle(stored, mine, recordingArt: false);
			Assert.False(stored[0].Enabled);
			Assert.True(stored[1].Enabled);
		}

		[Fact]
		public void A_copy_missing_from_the_list_is_said_so_and_offers_a_search_by_name()
		{
			Assert.Equal("This copy of the game isn't in the cheat list", CheatSheet.StatusLine(ConsoleType.Nes, null, false, 0));

			Assert.Equal(new[] { "Contra (USA)", "Super Contra (USA)" }, CheatSheet.SearchGamesByName(Db, "contra").Select(g => g.Name));
			Assert.Equal(new[] { "Super Contra (USA)" }, CheatSheet.SearchGamesByName(Db, "super CONTRA").Select(g => g.Name));
			//An empty query lists nothing rather than all 774 games.
			Assert.Empty(CheatSheet.SearchGamesByName(Db, " "));
		}

		[Fact]
		public void Entries_picked_by_name_are_marked_as_made_for_another_copy()
		{
			CheatSheetRow row = Assert.Single(Rows(Array.Empty<StoredCheat>(), game: ContraJapan, anotherCopy: true));

			Assert.Equal(CheatRowSource.AnotherCopy, row.Source);
			Assert.Contains(CheatSheet.AnotherCopyMark, row.Note);
			Assert.Equal("made for another copy — may not work", CheatSheet.AnotherCopyMark);
			Assert.Equal("1 on · codes from Gryzor (Japan) — made for another copy, may not work", CheatSheet.StatusLine(ConsoleType.Nes, ContraJapan, true, 1));
		}

		[Fact]
		public void The_status_line_names_the_matched_copy()
		{
			Assert.Equal("2 on · matched to your copy of Contra (USA)", CheatSheet.StatusLine(ConsoleType.Nes, Contra, false, 2));
		}

		[Theory]
		[InlineData(0, false, "none")]
		[InlineData(2, false, "2 on")]
		[InlineData(2, true, "off")]
		public void The_overlay_row_summarises_the_cheats_that_are_on(int on, bool disableAll, string expected)
		{
			Assert.Equal(expected, CheatSheet.OverlaySummary(on, disableAll));
		}

		[Fact]
		public void A_code_typed_by_hand_is_added_on_and_classified()
		{
			(IReadOnlyList<StoredCheat> stored, string error) = CheatSheet.AddCode(Array.Empty<StoredCheat>(), ConsoleType.Nes, "", "sxiopo", recordingArt: false);

			Assert.Equal("", error);
			StoredCheat added = Assert.Single(stored);
			Assert.Equal(CheatType.NesGameGenie, added.Type);
			Assert.Equal("SXIOPO", added.Codes);
			//With no description the code itself names the row.
			Assert.Equal("SXIOPO", added.Description);
			Assert.True(added.Enabled);
		}

		[Fact]
		public void A_code_the_console_does_not_understand_is_refused_with_its_reason()
		{
			(IReadOnlyList<StoredCheat> stored, string error) = CheatSheet.AddCode(Array.Empty<StoredCheat>(), ConsoleType.Nes, "x", "hello", recordingArt: false);

			Assert.Empty(stored);
			Assert.Equal(CheatConsoleScope.InvalidCodeReason, error);
		}
	}
}
