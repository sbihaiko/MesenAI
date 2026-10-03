using System;

namespace Mesen.Logic;

//What opening an archive does with the games inside it.
public enum ArchiveRomRoute
{
	//No game found inside: the archive itself goes to the Core.
	WholeFile,
	//One game: opened without asking.
	OnlyGame,
	//Two or more: the player picks one.
	Ask
}

//ADR-0249 (W-P5 sheet shape): an archive holding several games. In Player mode
//the list is a sheet inside the main window (PlaySelectRomSheetViewModel);
//Advanced, or an owner that is not the main window, keeps SelectRomWindow.
//Both share the route, the search and the inner-file index rule.
public static class ArchiveRomPick
{
	public static ArchiveRomRoute Route(int gameCount)
	{
		return gameCount switch {
			0 => ArchiveRomRoute.WholeFile,
			1 => ArchiveRomRoute.OnlyGame,
			_ => ArchiveRomRoute.Ask
		};
	}

	//The Core finds a UTF-8 name by name (index 0); any other name by its
	//one-based position in the archive's game list.
	public static int InnerFileIndex(bool isUtf8, int position)
	{
		return isUtf8 ? 0 : position + 1;
	}

	public static bool Matches(string name, string search)
	{
		return string.IsNullOrWhiteSpace(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase);
	}

	public static bool UsesSheet(bool playerMode, bool ownerIsMainWindow)
	{
		return playerMode && ownerIsMainWindow;
	}
}
