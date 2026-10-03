using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#674, #676 (PRD Part B §13.5.2 W-P14): which open a failure belongs to,
	//and what a recent game that did not open says.
	public class LoadPathsTests
	{
		[Fact]
		public void Only_the_latest_open_reports_its_failure()
		{
			Assert.True(PlayLoadFailure.IsCurrentOpen(3, 3));
			Assert.False(PlayLoadFailure.IsCurrentOpen(2, 3));
		}

		[Fact]
		public void RomInfo_names_the_rom_and_the_game_inside_a_zip()
		{
			RecentGameRom? plain = PlayRecentGameFailure.ParseRomInfo("Contra\n/roms/Contra (USA).nes\n\naspectratio=1.3\n");
			Assert.Equal(new RecentGameRom("/roms/Contra (USA).nes", ""), plain);
			Assert.Equal("Contra (USA).nes", plain!.ShownName);

			RecentGameRom? zipped = PlayRecentGameFailure.ParseRomInfo("Contra\r\n/roms/Contra.zip\u0001Contra (USA).nes\u00010\r\n\r\n");
			Assert.Equal(new RecentGameRom("/roms/Contra.zip", "Contra (USA).nes"), zipped);
			Assert.Equal("Contra (USA).nes", zipped!.ShownName);
		}

		[Theory]
		[InlineData("")]
		[InlineData("Contra")]
		[InlineData("Contra\n\n")]
		public void RomInfo_without_a_rom_path_names_nothing(string text)
		{
			Assert.Null(PlayRecentGameFailure.ParseRomInfo(text));
		}

		[Fact]
		public void A_recent_file_that_is_gone_is_missing_under_its_card_name()
		{
			Assert.Equal((LoadFailureCause.Missing, "Contra"), PlayRecentGameFailure.Classify("/recent/Contra.rgd", false, null, false, false, false));
		}

		[Fact]
		public void A_recent_file_that_cannot_be_read_is_damaged()
		{
			Assert.Equal((LoadFailureCause.Damaged, "Contra"), PlayRecentGameFailure.Classify("/recent/Contra.rgd", true, null, false, false, false));
		}

		[Fact]
		public void A_rom_that_was_moved_or_deleted_is_missing_under_its_file_name()
		{
			RecentGameRom rom = new("/roms/Contra (USA).nes", "");
			Assert.Equal((LoadFailureCause.Missing, "Contra (USA).nes"), PlayRecentGameFailure.Classify("/recent/Contra.rgd", true, rom, false, true, false));
		}

		[Fact]
		public void A_rom_that_is_there_fails_like_any_open()
		{
			RecentGameRom rom = new("/roms/Contra (USA).nes", "");
			Assert.Equal((LoadFailureCause.Damaged, "Contra (USA).nes"), PlayRecentGameFailure.Classify("/recent/Contra.rgd", true, rom, true, true, false));
			RecentGameRom zipped = new("/roms/Contra.zip", "Contra (USA).nes");
			Assert.Equal((LoadFailureCause.Damaged, "Contra (USA).nes"), PlayRecentGameFailure.Classify("/recent/Contra.rgd", true, zipped, true, false, true));
		}
	}
}
