using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1033 (ADR-0264 Decision 4): the match rule behind the flat library's search
	//box. The rule is what the player gets, and the player's whole question is
	//"which games does this text keep" - so every test here asks that question of
	//LibrarySearch and nothing else. No Avalonia, no disk, no native core
	//(ADR-0123): the rule is pure text in, entries out.
	//
	//The examples are the ones the parent spec #1030 names: `zel` finds *The
	//Legend of Zelda*, and a ROM named `Castlevania (U) [!].nes` is found by its
	//name and NOT by `U` or `!`, because the tags are not part of the game's name.
	public class LibrarySearchTests
	{
		private static LibrarySearchItem<string> Item(string title)
		{
			return new LibrarySearchItem<string>(title, title);
		}

		//The titles the query kept, in the order they came back. Asking for the
		//titles rather than for indices keeps each test reading as the question the
		//player asks.
		private static IReadOnlyList<string> Kept(string? query, params string[] titles)
		{
			return LibrarySearch.Filter(titles.Select(Item).ToList(), query)
				.Select(item => item.Payload)
				.ToList();
		}

		private static void AssertKept(string? query, string[] titles, params string[] expected)
		{
			Assert.Equal(expected, Kept(query, titles));
		}

		[Fact]
		public void A_title_matches_itself()
		{
			AssertKept("Castlevania", new[] { "Castlevania", "Contra" }, "Castlevania");
		}

		[Fact]
		public void A_word_from_the_middle_of_the_title_matches()
		{
			//The spec's own example: the search box matches anywhere in the title,
			//not from its first letter.
			AssertKept("zel", new[] { "The Legend of Zelda", "Super Mario Bros. 3" }, "The Legend of Zelda");
			AssertKept("mario", new[] { "The Legend of Zelda", "Super Mario Bros. 3" }, "Super Mario Bros. 3");
		}

		[Fact]
		public void The_match_ignores_case()
		{
			string[] titles = { "The Legend of Zelda" };
			AssertKept("ZELDA", titles, "The Legend of Zelda");
			AssertKept("zElDa", titles, "The Legend of Zelda");
			AssertKept("the legend", titles, "The Legend of Zelda");
		}

		[Fact]
		public void A_query_without_accents_finds_an_accented_title()
		{
			AssertKept("pokemon", new[] { "Pokémon Red", "Contra" }, "Pokémon Red");
		}

		[Fact]
		public void A_query_with_accents_finds_a_plain_title()
		{
			AssertKept("pokémon", new[] { "Pokemon Red", "Contra" }, "Pokemon Red");
		}

		[Fact]
		public void A_region_tag_in_parentheses_cannot_be_searched_for()
		{
			string[] titles = { "Castlevania (USA)", "Contra (Japan)" };
			AssertKept("usa", titles);
			AssertKept("japan", titles);
		}

		[Fact]
		public void A_dump_tag_in_brackets_cannot_be_searched_for()
		{
			string[] titles = { "Sonic the Hedgehog [T-Port]" };
			AssertKept("port", titles);
			AssertKept("t-port", titles);
		}

		[Fact]
		public void A_title_is_found_by_its_name_whatever_tags_it_carries()
		{
			//The other half of tag stripping: the tags being invisible must not make
			//the game itself harder to find.
			AssertKept("castlevania", new[] { "Castlevania (U) [!]", "Contra (J)" }, "Castlevania (U) [!]");
			AssertKept("sonic", new[] { "Sonic the Hedgehog [T-Port]" }, "Sonic the Hedgehog [T-Port]");
		}

		[Fact]
		public void A_query_that_is_only_a_tag_matches_nothing()
		{
			//`(U)` and `[!]` are what the file name says about the dump, not the
			//game's name. A player typing a region does not want the whole region:
			//ADR-0264 Decision 4 leaves that to the console filter.
			string[] titles = { "Castlevania (U) [!]", "Contra (U)" };
			AssertKept("u", titles);
			AssertKept("(u)", titles);
			AssertKept("[!]", titles);
			AssertKept("!", titles);
		}

		[Fact]
		public void An_empty_query_matches_every_item()
		{
			AssertKept("", new[] { "Castlevania", "Contra" }, "Castlevania", "Contra");
		}

		[Fact]
		public void A_whitespace_only_query_matches_every_item()
		{
			AssertKept("   ", new[] { "Castlevania", "Contra" }, "Castlevania", "Contra");
			AssertKept("\t\n", new[] { "Castlevania", "Contra" }, "Castlevania", "Contra");
			AssertKept(null, new[] { "Castlevania", "Contra" }, "Castlevania", "Contra");
		}

		[Fact]
		public void An_empty_library_filters_to_nothing()
		{
			Assert.Empty(LibrarySearch.Filter(Array.Empty<LibrarySearchItem<string>>(), "zel"));
			Assert.Empty(LibrarySearch.Filter(Array.Empty<LibrarySearchItem<string>>(), ""));
		}

		[Fact]
		public void The_filtered_list_keeps_the_order_it_was_given()
		{
			//A library is scanned in whatever order the disk answers; the search box
			//must not reshuffle it, or the tile the player was about to press A on
			//moves under the cursor as a letter is typed.
			AssertKept("a", new[] { "Zanac", "Contra", "Abadox", "Mario" }, "Zanac", "Contra", "Abadox", "Mario");
			AssertKept("o", new[] { "Zanac", "Contra", "Abadox", "Mario" }, "Contra", "Abadox", "Mario");
		}

		[Fact]
		public void Every_surviving_payload_comes_through_unchanged()
		{
			//The caller's own objects, not copies: the #1032 sheet hands in the
			//entries it will draw, and a game that matched comes back as the same
			//entry it handed over.
			Game zelda = new("The Legend of Zelda (U) [!].nes");
			Game contra = new("Contra (J).nes");
			List<LibrarySearchItem<Game>> items = new() {
				new LibrarySearchItem<Game>(zelda.Path, zelda),
				new LibrarySearchItem<Game>(contra.Path, contra)
			};

			IReadOnlyList<LibrarySearchItem<Game>> kept = LibrarySearch.Filter(items, "zelda");

			Assert.Single(kept);
			Assert.Same(zelda, kept[0].Payload);
			Assert.Equal("The Legend of Zelda (U) [!].nes", kept[0].Title);
		}

		[Fact]
		public void The_boolean_rule_is_the_one_the_filter_uses()
		{
			//The sheet asks the same question per visible tile when it decides which
			//ones to hide, so the one-word answer and the filtered list cannot
			//disagree.
			Assert.True(LibrarySearch.Matches("Castlevania (U) [!]", "castlevania"));
			Assert.True(LibrarySearch.Matches("Pokémon Red", "pokemon"));
			Assert.True(LibrarySearch.Matches("Contra", "   "));
			Assert.False(LibrarySearch.Matches("Castlevania (U) [!]", "u"));
			Assert.False(LibrarySearch.Matches("Contra", "zelda"));
			Assert.False(LibrarySearch.Matches(null, "zelda"));
		}

		private sealed class Game
		{
			public Game(string path)
			{
				Path = path;
			}

			public string Path { get; }
		}
	}
}
