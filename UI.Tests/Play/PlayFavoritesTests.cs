using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1110 (spec #1102, model half): the Favorites list the Home shelf reads. A
	//favorite is a library path, newest first; the list never drops an entry whose
	//ROM is gone, it only stops handing it out for drawing.
	public partial class PlayFavoritesTests
	{
		//Source-generated like the app's MesenSerializerContext, which reaches this
		//type through Configuration.PlayerEnhancements.
		[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyProperties = true)]
		[JsonSerializable(typeof(PlayFavorites))]
		private partial class FavoritesJson : JsonSerializerContext { }

		private static PlayFavorites NewList()
		{
			return new PlayFavorites { PathComparison = StringComparison.Ordinal };
		}

		private static Func<string, bool> Present(params string[] files)
		{
			HashSet<string> set = new(files, StringComparer.Ordinal);
			return set.Contains;
		}

		[Fact]
		public void Favorites_are_listed_newest_first()
		{
			PlayFavorites list = NewList();
			list.Toggle("/roms/a.nes");
			list.Toggle("/roms/b.nes");
			list.Toggle("/roms/c.nes");

			Assert.Equal(new[] { "/roms/c.nes", "/roms/b.nes", "/roms/a.nes" }, list.Paths);
		}

		[Fact]
		public void Toggle_adds_then_removes_and_says_which()
		{
			PlayFavorites list = NewList();

			Assert.True(list.Toggle("/roms/a.nes"));
			Assert.True(list.IsFavorite("/roms/a.nes"));
			Assert.False(list.Toggle("/roms/a.nes"));
			Assert.False(list.IsFavorite("/roms/a.nes"));
			Assert.Empty(list.Paths);
		}

		[Fact]
		public void Toggling_a_library_entry_twice_matches_it_by_its_path_only()
		{
			PlayFavorites list = NewList();
			list.Toggle("/roms/a.nes");

			Assert.False(list.IsFavorite("/roms/a.NES"));
			Assert.False(list.IsFavorite(""));
			Assert.False(list.Toggle(""));
			Assert.Single(list.Paths);
		}

		[Fact]
		public void Path_case_follows_the_comparison_rule()
		{
			PlayFavorites list = new() { PathComparison = StringComparison.OrdinalIgnoreCase };
			list.Toggle("/roms/Mario.nes");

			Assert.True(list.IsFavorite("/ROMS/mario.nes"));
			Assert.False(list.Toggle("/ROMS/mario.nes"));
			Assert.Empty(list.Paths);
		}

		[Fact]
		public void Toggling_the_continue_game_favorites_the_library_entry_with_the_same_path()
		{
			PlayFavorites list = NewList();
			RecentGameRom continueGame = new("/roms/a.nes", "");

			Assert.True(list.ToggleContinue(continueGame));
			Assert.True(list.IsFavorite("/roms/a.nes"));

			//The same game toggled from its library cover is the same entry.
			Assert.False(list.Toggle("/roms/a.nes"));
			Assert.False(list.IsFavorite(continueGame.Path));
		}

		[Fact]
		public void Toggling_a_continue_game_that_names_no_rom_changes_nothing()
		{
			PlayFavorites list = NewList();
			list.Toggle("/roms/a.nes");

			Assert.False(list.ToggleContinue(null));
			Assert.Equal(new[] { "/roms/a.nes" }, list.Paths);
		}

		[Fact]
		public void A_favorite_whose_rom_vanished_stays_listed_but_is_not_drawn_and_returns_with_the_file()
		{
			PlayFavorites list = NewList();
			list.Toggle("/roms/a.nes");
			list.Toggle("/roms/b.nes");

			Assert.Equal(new[] { "/roms/a.nes" }, list.ForShelf(Present("/roms/a.nes")));
			Assert.Equal(new[] { "/roms/b.nes", "/roms/a.nes" }, list.Paths);

			Assert.Equal(new[] { "/roms/b.nes", "/roms/a.nes" }, list.ForShelf(Present("/roms/a.nes", "/roms/b.nes")));
		}

		[Fact]
		public void The_shelf_is_hidden_with_no_favorites_and_with_none_drawable()
		{
			PlayFavorites list = NewList();
			Assert.False(list.ShelfVisible(Present("/roms/a.nes")));

			list.Toggle("/roms/a.nes");
			Assert.True(list.ShelfVisible(Present("/roms/a.nes")));
			Assert.False(list.ShelfVisible(Present()));

			list.Toggle("/roms/a.nes");
			Assert.False(list.ShelfVisible(Present("/roms/a.nes")));
		}

		[Fact]
		public void The_list_survives_a_settings_round_trip_in_order()
		{
			PlayFavorites list = NewList();
			list.Toggle("/roms/a.nes");
			list.Toggle("/roms/b.nes");

			string json = JsonSerializer.Serialize(list, FavoritesJson.Default.PlayFavorites);
			PlayFavorites loaded = JsonSerializer.Deserialize(json, FavoritesJson.Default.PlayFavorites)!;

			Assert.Equal(new[] { "/roms/b.nes", "/roms/a.nes" }, loaded.Paths);
			Assert.DoesNotContain("PathComparison", json);
		}

		[Fact]
		public void A_settings_file_without_favorites_loads_an_empty_list()
		{
			PlayFavorites loaded = JsonSerializer.Deserialize("{}", FavoritesJson.Default.PlayFavorites)!;
			Assert.Empty(loaded.Paths);
		}
	}
}
