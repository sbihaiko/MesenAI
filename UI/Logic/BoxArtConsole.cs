namespace Mesen.Logic
{
	//The console a box art belongs to, at the granularity the art collection is
	//laid out in: libretro-thumbnails keeps one repository per system, so a Game
	//Boy Color game lives in a different repository from a Game Boy one and a
	//Game Gear game in a different one from a Master System game.
	//
	//It is deliberately NOT the core's ConsoleType: that type has no Game Boy
	//Color member at all, and it files Game Gear and SG-1000 under its single
	//Sms member (a `.gg` or `.sg` file is read as the SMS console with a
	//different model inside), which would fold three repositories into one key.
	//The flat library's own console enum (#1038) draws exactly the same seven
	//lines; the members below are declared in that enum's order and with its
	//values on purpose, so that once it is on `main` the two can be unified by a
	//cast and this type deleted. Until then this slice cannot reference a type
	//that only exists on another branch, so it carries its own copy of the name.
	public enum BoxArtConsole
	{
		Unknown = 0,
		Nes,
		GameBoy,
		GameBoyColor,
		GameBoyAdvance,
		MasterSystem,
		//SG-1000 is the Master System's predecessor and shares its core, but not
		//its art collection: `Sega_-_SG-1000` and `Sega_-_Master_System_-_Mark_III`
		//are two repositories.
		Sg1000,
		GameGear
	}

	//`BoxArtConsole -> libretro-thumbnails repository`, plus the cache folder tag
	//for the same console. One table, because both answers are read from the same
	//enum and a second list would drift from the first.
	public static class BoxArtSystems
	{
		//The repository folder each console's art is published under. The seven
		//names were checked against the collection on 2026-10-07: every one of
		//these repositories exists and is spelled exactly this.
		public static string? RepoFolder(BoxArtConsole console) => console switch {
			BoxArtConsole.Nes => "Nintendo_-_Nintendo_Entertainment_System",
			BoxArtConsole.GameBoy => "Nintendo_-_Game_Boy",
			BoxArtConsole.GameBoyColor => "Nintendo_-_Game_Boy_Color",
			BoxArtConsole.GameBoyAdvance => "Nintendo_-_Game_Boy_Advance",
			BoxArtConsole.MasterSystem => "Sega_-_Master_System_-_Mark_III",
			BoxArtConsole.Sg1000 => "Sega_-_SG-1000",
			BoxArtConsole.GameGear => "Sega_-_Game_Gear",
			_ => null
		};

		//The cache's per-console folder: the same seven lines, short, and stable
		//because it is a directory name on the player's disk rather than a label
		//anyone reads. Empty for a console the collection does not carry, which is
		//the cache's own "no key, no fetch" answer.
		public static string CacheTag(BoxArtConsole console) => console switch {
			BoxArtConsole.Nes => "nes",
			BoxArtConsole.GameBoy => "gb",
			BoxArtConsole.GameBoyColor => "gbc",
			BoxArtConsole.GameBoyAdvance => "gba",
			BoxArtConsole.MasterSystem => "sms",
			BoxArtConsole.Sg1000 => "sg",
			BoxArtConsole.GameGear => "gg",
			_ => ""
		};
	}
}
