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
