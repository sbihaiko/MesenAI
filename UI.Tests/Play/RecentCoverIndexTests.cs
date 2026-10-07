using System;
using System.IO;
using System.IO.Compression;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1035 (ADR-0264 Decision 6.3): the Recent list as a cover source. A game the
	//player has already run shows its own screenshot, found by the ROM's full path
	//in the Recent record's RomInfo.txt.
	public class RecentCoverIndexTests
	{
		//A .rgd as the Core writes it: a zip with Screenshot.png and RomInfo.txt,
		//whose second line is the ROM path (PlayRecentGameFailure.ParseRomInfo).
		private static string WriteRecent(string folder, string recentName, string? romInfo, byte[]? screenshot)
		{
			string file = Path.Combine(folder, recentName + ".rgd");
			using ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create);
			if(romInfo != null) {
				using Stream s = zip.CreateEntry("RomInfo.txt").Open();
				using StreamWriter w = new(s);
				w.Write(romInfo);
			}
			if(screenshot != null) {
				using Stream s = zip.CreateEntry("Screenshot.png").Open();
				s.Write(screenshot, 0, screenshot.Length);
			}
			return file;
		}

		//RomInfo.txt as the Core writes it: the game's name, the ROM path, the patch.
		private static string RomInfo(string romPath)
		{
			return "Castlevania (USA)\n" + romPath + "\n";
		}

		private static string NewTempFolder()
		{
			string folder = Path.Combine(Path.GetTempPath(), "mesen-recentcover-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(folder);
			return folder;
		}

		[Fact]
		public void A_played_games_cover_is_the_screenshot_of_its_recent_file()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
				string rom = Path.Combine(folder, "roms", "Castlevania (USA).nes");
				WriteRecent(folder, "Castlevania (USA)", RomInfo(rom), png);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);
				Assert.Equal(png, index.FindCover(rom));

				//A game with no Recent entry keeps the generic cover.
				Assert.Null(index.FindCover(Path.Combine(folder, "roms", "Metroid (USA).nes")));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//A game inside a zip is recorded as "path\x1inner": the ROM's own path is
		//what the library matched, so that is the key (ParseRomInfo).
		[Fact]
		public void A_rom_inside_a_zip_matches_by_its_own_path()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 9, 9 };
				string archive = Path.Combine(folder, "collection.zip");
				WriteRecent(folder, "Contra", "Contra (USA)\n" + archive + "\x1" + "Contra (USA).nes" + "\n", png);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);
				Assert.Equal(png, index.FindCover(archive));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//#1035: the comparison is normalised, and the case rule is a parameter so
		//both behaviours are testable - a caller that wants to ignore case gets it,
		//and a caller that does not, does not.
		[Fact]
		public void The_path_comparison_is_taken_as_a_parameter_in_both_directions()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 4, 4, 4 };
				string rom = Path.Combine(folder, "roms", "Castlevania (USA).nes");
				string otherCase = Path.Combine(folder, "ROMS", "CASTLEVANIA (USA).NES");
				WriteRecent(folder, "Castlevania (USA)", RomInfo(rom), png);

				RecentCoverIndex exact = new(folder, StringComparison.Ordinal);
				Assert.Null(exact.FindCover(otherCase));

				RecentCoverIndex folding = new(folder, StringComparison.OrdinalIgnoreCase);
				Assert.Equal(png, folding.FindCover(otherCase));
				Assert.Equal(png, folding.FindCover(rom));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//Two .rgd files can name the same ROM path: two games opened out of one
		//collection.zip both record the archive's path. The winner follows the clock,
		//not the name - the order Directory.GetFiles hands files over is unspecified.
		//The timestamps are set explicitly, so the test cannot flake on the
		//filesystem's own mtime granularity.
		[Fact]
		public void The_later_written_recent_file_wins_for_the_same_rom_path()
		{
			string folder = NewTempFolder();
			try {
				byte[] older = { 0x89, 0x50, 0x4E, 0x47, 1, 1 };
				byte[] newer = { 0x89, 0x50, 0x4E, 0x47, 2, 2 };
				string rom = Path.Combine(folder, "roms", "Contra (USA).nes");

				//Named and created the wrong way round on purpose: the newer file is
				//written first and sorts first, so name order and creation order both
				//point at the older file. Only the timestamp picks the newer one.
				string newerFile = WriteRecent(folder, "a-newer", RomInfo(rom), newer);
				string olderFile = WriteRecent(folder, "z-older", RomInfo(rom), older);
				DateTime olderAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
				File.SetLastWriteTimeUtc(newerFile, olderAt.AddHours(1));
				File.SetLastWriteTimeUtc(olderFile, olderAt);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);

				Assert.Equal(newer, index.FindCover(rom));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//A `.rgd` is written the moment the game is opened, and the screenshot is
		//added to it afterwards: the newest entry for a ROM path can name the path
		//and hold no Screenshot.png at all. The tile then keeps the older game's own
		//screenshot instead of falling back to the generic cover - the newest entry
		//*with a cover* wins, which is not the same as the newest entry.
		[Fact]
		public void A_newer_recent_file_without_a_screenshot_falls_back_to_an_older_one()
		{
			string folder = NewTempFolder();
			try {
				byte[] cover = { 0x89, 0x50, 0x4E, 0x47, 6, 6, 6 };
				string rom = Path.Combine(folder, "roms", "Contra (USA).nes");

				//The older file is the one with the picture; the newer one records the
				//same path with no Screenshot.png. Timestamps are set explicitly, so
				//only the fallback - not the filesystem's own ordering - can find it.
				string newerFile = WriteRecent(folder, "newer-no-shot", RomInfo(rom), null);
				string olderFile = WriteRecent(folder, "older-with-shot", RomInfo(rom), cover);
				DateTime olderAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
				File.SetLastWriteTimeUtc(newerFile, olderAt.AddHours(1));
				File.SetLastWriteTimeUtc(olderFile, olderAt);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);

				Assert.Equal(cover, index.FindCover(rom));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//#1035: the `.rgd` is named after the ROM's basename, and the Core keeps one
		//archive per name, shared by every folder - playing /B/Game.nes overwrites
		//the `Game.rgd` the index recorded for /A/Game.nes. The ROM path and the
		//screenshot have to come out of the same read of the file, or A is served
		//B's cover.
		[Fact]
		public void A_recent_file_overwritten_by_a_namesake_does_not_serve_the_wrong_cover()
		{
			string folder = NewTempFolder();
			try {
				byte[] coverA = { 0x89, 0x50, 0x4E, 0x47, 1, 1, 1 };
				byte[] coverB = { 0x89, 0x50, 0x4E, 0x47, 2, 2, 2, 2 };
				string romA = Path.Combine(folder, "A", "Game.nes");
				string romB = Path.Combine(folder, "B", "Game.nes");

				//A was played first, so the index is built while `Game.rgd` records A.
				string recent = WriteRecent(folder, "Game", RomInfo(romA), coverA);
				File.SetLastWriteTimeUtc(recent, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
				RecentCoverIndex index = new(folder, StringComparison.Ordinal);

				//B is played next: the Core writes B's RomInfo and B's Screenshot.png
				//over the same file, which keeps its name.
				File.Delete(recent);
				WriteRecent(folder, "Game", RomInfo(romB), coverB);
				File.SetLastWriteTimeUtc(recent, new DateTime(2026, 1, 2, 4, 5, 6, DateTimeKind.Utc));

				//A is not in that archive anymore: the generic cover, never B's. The
				//miss is asked twice, so a mismatched cover is not cached either.
				Assert.Null(index.FindCover(romA));
				Assert.Null(index.FindCover(romA));

				//The direction is pinned: an index built now finds B's own screenshot,
				//so the null above is the identity check and not a missing archive.
				Assert.Equal(coverB, new RecentCoverIndex(folder, StringComparison.Ordinal).FindCover(romB));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//The default factory derives the rule from the platform: Windows and macOS
		//compare paths without case, the rest of the world does not.
		[Fact]
		public void The_default_index_follows_the_platform_case_rule()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 7 };
				string rom = Path.Combine(folder, "roms", "Castlevania (USA).nes");
				WriteRecent(folder, "Castlevania (USA)", RomInfo(rom), png);

				bool caseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
				RecentCoverIndex index = RecentCoverIndex.Open(folder);
				byte[]? cover = index.FindCover(Path.Combine(folder, "RomS", "castlevania (usa).NES"));
				if(caseInsensitive) {
					Assert.Equal(png, cover);
				} else {
					Assert.Null(cover);
				}
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//A relative path is normalised to the full one before comparing, so a
		//caller that hands over a relative path still finds the Recent entry.
		[Fact]
		public void The_compared_path_is_normalised_to_the_full_path()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 5, 5 };
				string rom = Path.Combine(folder, "roms", "Castlevania (USA).nes");
				WriteRecent(folder, "Castlevania (USA)", RomInfo(rom), png);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);
				string withDot = Path.Combine(folder, "roms", ".", "Castlevania (USA).nes");
				Assert.Equal(png, index.FindCover(withDot));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//#1035: a corrupt or incomplete entry is ignored - never a throw, never a
		//partial image. Every one of these answers null.
		[Fact]
		public void Corrupt_and_incomplete_recent_files_are_ignored()
		{
			string folder = NewTempFolder();
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 1 };

				//No Screenshot.png: nothing to show.
				string noShot = Path.Combine(folder, "a.nes");
				WriteRecent(folder, "no-shot", RomInfo(noShot), null);

				//No RomInfo.txt: the entry names no ROM.
				string noInfo = Path.Combine(folder, "b.nes");
				using(ZipArchive zip = ZipFile.Open(Path.Combine(folder, "no-info.rgd"), ZipArchiveMode.Create)) {
					using Stream s = zip.CreateEntry("Screenshot.png").Open();
					s.Write(png, 0, png.Length);
				}

				//RomInfo.txt with fewer than two lines: no ROM path.
				string oneLine = Path.Combine(folder, "c.nes");
				WriteRecent(folder, "one-line", "Castlevania (USA)", png);
				WriteRecent(folder, "empty-line", "Castlevania (USA)\n\n", png);

				//Not a zip at all, and a zip that is truncated.
				string notAZip = Path.Combine(folder, "d.nes");
				File.WriteAllText(Path.Combine(folder, "not-a-zip.rgd"), notAZip);
				string truncated = Path.Combine(folder, "e.nes");
				byte[] whole = File.ReadAllBytes(WriteRecent(folder, "whole", RomInfo(truncated), png));
				File.Delete(Path.Combine(folder, "whole.rgd"));
				File.WriteAllBytes(Path.Combine(folder, "truncated.rgd"), whole[..(whole.Length / 2)]);

				RecentCoverIndex index = new(folder, StringComparison.Ordinal);
				Assert.Null(index.FindCover(noShot));
				Assert.Null(index.FindCover(noInfo));
				Assert.Null(index.FindCover(oneLine));
				Assert.Null(index.FindCover(notAZip));
				Assert.Null(index.FindCover(truncated));
				Assert.Null(index.FindCover(""));
				Assert.Null(index.FindCover(null));
			} finally {
				Directory.Delete(folder, true);
			}
		}

		//A folder that is not there (or was never configured) answers nothing and
		//does not throw: the library still opens with generic covers.
		[Fact]
		public void A_missing_folder_answers_nothing()
		{
			string missing = Path.Combine(Path.GetTempPath(), "mesen-recentcover-absent-" + Guid.NewGuid().ToString("N"));

			RecentCoverIndex index = new(missing, StringComparison.Ordinal);
			Assert.Null(index.FindCover(Path.Combine(missing, "Castlevania (USA).nes")));

			Assert.Null(new RecentCoverIndex("", StringComparison.Ordinal).FindCover("Castlevania (USA).nes"));
			Assert.Null(new RecentCoverIndex(null!, StringComparison.Ordinal).FindCover("Castlevania (USA).nes"));
		}
	}
}
