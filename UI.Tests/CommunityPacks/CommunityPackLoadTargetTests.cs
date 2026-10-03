using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//#657: an install or Restore captures the load it was started for before
	//the download; it acts only while that load is still the loaded one.
	public class CommunityPackLoadTargetTests
	{
		private static readonly CommunityPackLoadTarget GameA = new("AB12", "FF00", "/roms/A", "A", 3);

		[Fact]
		public void The_same_open_of_the_same_rom_is_still_loaded()
		{
			Assert.True(GameA.IsStillLoaded(GameA with { RomSha1 = "ab12" }));
		}

		[Fact]
		public void Another_game_opened_meanwhile_is_not()
		{
			Assert.False(GameA.IsStillLoaded(new CommunityPackLoadTarget("CD34", "EE11", "/roms/B", "B", 4)));
		}

		[Fact]
		public void Reopening_the_same_game_is_a_new_load()
		{
			Assert.False(GameA.IsStillLoaded(GameA with { OpenGeneration = 4 }));
		}

		[Fact]
		public void A_quit_game_or_an_empty_capture_is_never_still_loaded()
		{
			Assert.False(GameA.IsStillLoaded(GameA with { RomSha1 = "" }));
			CommunityPackLoadTarget empty = GameA with { RomSha1 = "" };
			Assert.False(empty.HasRom);
			Assert.False(empty.IsStillLoaded(empty));
		}
	}
}
