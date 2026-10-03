using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.2 (PRD Part B §8, §13.5.2 W-P1/W-P2): which home shows, what the Recent
	//grid lists, and the Continue card's "last played" wording.
	public class PlayHomeTests
	{
		[Fact]
		public void No_recents_is_the_first_run_home()
		{
			Assert.Equal(PlayHomeKind.FirstRun, PlayHome.Classify(0));
			Assert.Equal(PlayHomeKind.WithRecents, PlayHome.Classify(1));
			Assert.Equal(PlayHomeKind.WithRecents, PlayHome.Classify(40));
		}

		[Fact]
		public void Recent_grid_lists_every_game_but_the_continue_card()
		{
			Assert.Equal(new List<string> { "Zelda", "Metroid" }, PlayHome.RecentGrid(new[] { "Contra", "Zelda", "Metroid" }));
			Assert.Empty(PlayHome.RecentGrid(new[] { "Contra" }));
			Assert.Empty(PlayHome.RecentGrid(Array.Empty<string>()));
		}

		[Fact]
		public void Last_played_uses_calendar_days()
		{
			DateTime now = new(2026, 10, 2, 0, 10, 0);
			Assert.Equal((LastPlayedKind.Today, 0), PlayHome.LastPlayed(now.AddMinutes(-5), now));
			Assert.Equal((LastPlayedKind.Yesterday, 1), PlayHome.LastPlayed(now.AddMinutes(-20), now));
			Assert.Equal((LastPlayedKind.DaysAgo, 3), PlayHome.LastPlayed(now.AddDays(-3), now));
			Assert.Equal((LastPlayedKind.DaysAgo, PlayHome.MaxDaysAgo), PlayHome.LastPlayed(now.AddDays(-PlayHome.MaxDaysAgo), now));
			Assert.Equal((LastPlayedKind.OnDate, 30), PlayHome.LastPlayed(now.AddDays(-30), now));
			Assert.Equal((LastPlayedKind.Today, 0), PlayHome.LastPlayed(now.AddHours(2), now));
		}

		[Theory]
		[InlineData(true, true, PlayHomeOrientation.AudioAndPacks)]
		[InlineData(true, false, PlayHomeOrientation.AudioOnly)]
		[InlineData(false, true, PlayHomeOrientation.PacksOnly)]
		[InlineData(false, false, PlayHomeOrientation.None)]
		public void First_run_sentence_only_says_what_the_settings_make_true(bool audio, bool autoPacks, PlayHomeOrientation expected)
		{
			Assert.Equal(expected, PlayHome.Orientation(audio, autoPacks));
		}
	
		//W-P2's pack badge: a recent game whose HdPacks/<game name> folder holds
		//a hires.txt (the folder HdPackLoader opens for that ROM).
		[Fact]
		public void A_recent_game_has_a_pack_when_its_hd_pack_folder_has_hires_txt()
		{
			string packs = Path.Combine(Path.GetTempPath(), "mesen-playhome-" + Guid.NewGuid().ToString("N"));
			try {
				Directory.CreateDirectory(Path.Combine(packs, "Contra (USA)"));
				File.WriteAllText(Path.Combine(packs, "Contra (USA)", "hires.txt"), "<ver>106");
				Directory.CreateDirectory(Path.Combine(packs, "Metroid (USA)"));
				Assert.True(PlayHome.HasHdPack(packs, "Contra (USA)"));
				Assert.False(PlayHome.HasHdPack(packs, "Metroid (USA)"));
				Assert.False(PlayHome.HasHdPack(packs, "Zelda"));
				Assert.False(PlayHome.HasHdPack(packs, ""));
				Assert.False(PlayHome.HasHdPack("", "Contra (USA)"));
			} finally {
				Directory.Delete(packs, true);
			}
		}

		//W-P2's Continue picture: the Screenshot.png inside the recent-game file.
		[Fact]
		public void The_continue_preview_is_the_recent_files_screenshot()
		{
			string folder = Path.Combine(Path.GetTempPath(), "mesen-playhome-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(folder);
			try {
				byte[] png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
				string withShot = Path.Combine(folder, "Contra (USA).rgd");
				using(ZipArchive zip = ZipFile.Open(withShot, ZipArchiveMode.Create)) {
					using Stream s = zip.CreateEntry("Screenshot.png").Open();
					s.Write(png, 0, png.Length);
				}
				string withoutShot = Path.Combine(folder, "Metroid (USA).rgd");
				using(ZipArchive zip = ZipFile.Open(withoutShot, ZipArchiveMode.Create)) {
					zip.CreateEntry("RomInfo.txt");
				}
				string notAZip = Path.Combine(folder, "Mega Man (USA).rgd");
				File.WriteAllText(notAZip, "");

				Assert.Equal(png, PlayHome.ReadScreenshot(withShot));
				Assert.Null(PlayHome.ReadScreenshot(withoutShot));
				Assert.Null(PlayHome.ReadScreenshot(notAZip));
				Assert.Null(PlayHome.ReadScreenshot(Path.Combine(folder, "missing.rgd")));

				//An entry that decompresses past the cap keeps the placeholder.
				string huge = Path.Combine(folder, "Huge (USA).rgd");
				using(ZipArchive zip = ZipFile.Open(huge, ZipArchiveMode.Create)) {
					using Stream s = zip.CreateEntry("Screenshot.png").Open();
					s.Write(new byte[PlayHome.MaxScreenshotBytes + 1], 0, PlayHome.MaxScreenshotBytes + 1);
				}
				Assert.Null(PlayHome.ReadScreenshot(huge));
			} finally {
				Directory.Delete(folder, true);
			}
		}
}
}
