using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Windows
{
	//#1110 (ADR-0268 Decision 1): X on a focused cover. The pad bridge asks this
	//which library path the focus is on (a library tile, a Home tile, the Continue
	//button) and toggles the Favorites list with it; PlayFavorites owns the rule,
	//this file only turns a focused control into the path the rule takes. Where no
	//cover has the focus the answer is null: X does nothing and the bar has no X.
	internal static class PlayFavoriteCover
	{
		//The library path of the cover the focus is on, or null.
		public static string? PathOf(MainWindowViewModel model, Control? focused)
		{
			if(focused is null) {
				return null;
			}
			if(model.RomPicker.IsVisible) {
				if(model.RomPicker.Mode != RomPickerMode.Library) {
					return null;
				}
				return focused.GetSelfAndVisualAncestors().OfType<StyledElement>()
					.Select(e => e.DataContext).OfType<PlayerLibraryTile>().FirstOrDefault()?.Path;
			}
			if(!model.RecentGames.ShowRecentsHome) {
				return null;
			}
			if(focused.Name == "PlayHomeContinueButton") {
				return ContinuePath(model.RecentGames);
			}
			RecentGameInfo? entry = focused.GetSelfAndVisualAncestors().OfType<StateGridEntry>().FirstOrDefault()?.Entry;
			if(entry is null || entry.StateIndex > 0) {
				return null;
			}
			return entry.RomPath.Length > 0 ? entry.RomPath : RomOf(entry.FileName)?.Path;
		}

		//The Continue game is the first recent entry, matched to the library by the
		//ROM path its recent-game file names.
		private static string? ContinuePath(RecentGamesViewModel home)
		{
			return home.GameEntries.Count > 0 ? RomOf(home.GameEntries[0].FileName)?.Path : null;
		}

		private static RecentGameRom? RomOf(string recentFile)
		{
			return recentFile.EndsWith(".rgd") ? LoadRomHelper.ReadRecentGameRom(recentFile) : null;
		}

		//The bar's declaration with X named for this cover, or the surface's own
		//when no cover has the focus.
		public static IReadOnlyList<PlayBarEntry> Declare(IReadOnlyList<PlayBarEntry> declared, MainWindowViewModel model, Control? focused)
		{
			string? path = PathOf(model, focused);
			return path is null ? declared : PlayBarDeclarations.WithFavorite(declared, Favorites().IsFavorite(path));
		}

		//X's whole effect. Returns whether the press toggled anything.
		public static bool Toggle(MainWindowViewModel model, Control? focused)
		{
			string? path = PathOf(model, focused);
			if(path is null) {
				return false;
			}
			Favorites().Toggle(path);
			ConfigManager.Config.Save();
			model.RecentGames.RefreshFavorites();
			return true;
		}

		private static PlayFavorites Favorites() => ConfigManager.Config.PlayerEnhancements.Favorites;
	}
}
