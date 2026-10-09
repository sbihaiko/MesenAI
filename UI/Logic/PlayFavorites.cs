using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mesen.Logic;

//#1110 (spec #1102, model half): the Favorites list behind Home's Favorites
//shelf. A favorite is a library path, newest first, persisted with the other
//Play settings (PlayerEnhancementsConfig.Favorites).
//
//Nothing here draws or binds a control: the shelf and the X key read this
//model. The list never drops an entry whose ROM is gone - the player moved the
//file, not the preference - it only stops handing it out for drawing, and the
//entry comes back with the file (ForShelf takes the existence check, so the
//rule is unit-tested without a disk).
//
//Host-free (ADR-0123): no Avalonia, no core, no ConfigManager.
public sealed class PlayFavorites
{
	//Library paths, newest favorite first. Public and settable so the settings
	//file round-trips it as a plain array; a file without it loads empty.
	public List<string> Paths { get; set; } = new();

	//The case rule library paths follow on this machine; a property so tests can
	//pin either behaviour. Never written to the settings file.
	[JsonIgnore]
	public StringComparison PathComparison { get; set; } = RecentCoverIndex.PlatformPathComparison;

	public bool IsFavorite(string? path)
	{
		return !string.IsNullOrEmpty(path) && Paths.Any(p => string.Equals(p, path, PathComparison));
	}

	//X on a library cover or a Home tile. Returns whether the game is a favorite
	//afterwards; a blank path changes nothing and answers false.
	public bool Toggle(string? path)
	{
		if(string.IsNullOrEmpty(path)) {
			return false;
		}
		int index = Paths.FindIndex(p => string.Equals(p, path, PathComparison));
		if(index >= 0) {
			Paths.RemoveAt(index);
			return false;
		}
		Paths.Insert(0, path);
		return true;
	}

	//X on the Continue tile: the Continue game is its recent record's ROM, matched
	//to the library entry by that path. A record that names no ROM changes nothing.
	public bool ToggleContinue(RecentGameRom? continueGame)
	{
		return continueGame != null && Toggle(continueGame.Path);
	}

	//What the shelf may draw: the favorites whose ROM file is there, newest first.
	public IReadOnlyList<string> ForShelf(Func<string, bool> romExists)
	{
		return Paths.Where(romExists).ToList();
	}

	//False when nothing could be drawn, so Home stays W-P2 as it is today.
	public bool ShelfVisible(Func<string, bool> romExists)
	{
		return Paths.Any(romExists);
	}
}
