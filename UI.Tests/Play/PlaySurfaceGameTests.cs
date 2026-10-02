using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#639: the pause overlay and every sheet opened from it belong to the game
	//they were opened for. Opening another ROM directly (A → B, no
	//EmulationStopped) changes the loaded game without passing through "no
	//game", so the surfaces close on any change of game, not only when it is
	//gone. A reload of the same file (power cycle, an in-place pack change)
	//keeps them.
	public class PlaySurfaceGameTests
	{
		[Fact]
		public void Another_rom_opened_directly_closes_the_surfaces()
		{
			Assert.True(PlaySurfaceGame.ClosesSurfaces(true, "/roms/a.nes", true, "/roms/b.nes"));
		}

		[Fact]
		public void The_game_going_away_closes_the_surfaces()
		{
			Assert.True(PlaySurfaceGame.ClosesSurfaces(true, "/roms/a.nes", false, ""));
		}

		[Fact]
		public void A_first_load_closes_whatever_the_home_had()
		{
			Assert.True(PlaySurfaceGame.ClosesSurfaces(false, "", true, "/roms/a.nes"));
		}

		[Fact]
		public void A_reload_of_the_same_rom_keeps_the_surfaces()
		{
			Assert.False(PlaySurfaceGame.ClosesSurfaces(true, "/roms/a.nes", true, "/roms/a.nes"));
		}

		[Fact]
		public void Another_file_inside_the_same_archive_is_another_game()
		{
			Assert.True(PlaySurfaceGame.ClosesSurfaces(true, "/roms/set.zip\u0001a.nes", true, "/roms/set.zip\u0001b.nes"));
		}
	}
}
