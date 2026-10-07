using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#953: what a file dropped on the main window opens (MainWindow.OnDrop,
	//and LoadRomHelper.LoadFile behind it), decided without the host. The
	//headless PlayDragDropRomTests only checks the wiring.
	public class DropRouteTests
	{
		private static readonly byte[] RomHeader = { (byte)'N', (byte)'E', (byte)'S', 0x1A, 0x02 };

		private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

		[Fact]
		public void A_drop_that_carries_no_file_does_nothing()
		{
			Assert.Equal(DropAction.Ignore, DropRoute.Decide(null, false, RomHeader, false));
			Assert.Equal(DropAction.Ignore, DropRoute.Decide("", false, RomHeader, false));
		}

		[Fact]
		public void A_rom_opens_as_a_rom()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/games/Contra (USA).nes", true, RomHeader, false));
		}

		[Fact]
		public void An_ips_or_bps_patch_is_applied_whatever_its_extension()
		{
			Assert.Equal(DropAction.ApplyPatch, DropRoute.Decide("/games/Contra.ips", true, Ascii("PATCH"), false));
			Assert.Equal(DropAction.ApplyPatch, DropRoute.Decide("/games/Contra.bps", true, Ascii("BPS1\0"), false));
			Assert.Equal(DropAction.ApplyPatch, DropRoute.Decide("/games/Contra.ups", true, Ascii("UPS1\0"), false));
			Assert.Equal(DropAction.ApplyPatch, DropRoute.Decide("/games/renamed.bin", true, Ascii("PATCH"), false));
		}

		[Fact]
		public void A_file_named_like_a_patch_without_its_header_opens_as_a_rom()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/games/Contra.ips", true, RomHeader, false));
		}

		[Fact]
		public void A_file_shorter_than_a_patch_header_is_not_a_patch()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/games/tiny.ips", true, Ascii("PAT"), false));
		}

		[Fact]
		public void A_save_state_is_loaded_with_or_without_a_game_running()
		{
			Assert.Equal(DropAction.LoadState, DropRoute.Decide("/states/Contra_1.mss", true, RomHeader, false));
			Assert.Equal(DropAction.LoadState, DropRoute.Decide("/states/Contra_1.MSS", true, RomHeader, true));
		}

		[Fact]
		public void A_movie_plays_only_while_a_game_runs()
		{
			Assert.Equal(DropAction.PlayMovie, DropRoute.Decide("/movies/run.mmo", true, RomHeader, true));
			Assert.Equal(DropAction.PlayMovie, DropRoute.Decide("/movies/run.bk2", true, RomHeader, true));
			Assert.Equal(DropAction.PlayMovie, DropRoute.Decide("/movies/run.gbmv", true, RomHeader, true));
			//No game: the movie falls through to the ROM loader, as before #953.
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/movies/run.mmo", true, RomHeader, false));
		}

		[Fact]
		public void A_missing_file_says_so()
		{
			Assert.Equal(DropAction.FileNotFound, DropRoute.Decide("/games/gone.nes", false, RomHeader, false));
		}

		//There is no pack branch: a pack archive goes to the ROM loader like any
		//other archive, and a pack folder is not a file, so it is reported missing.
		[Fact]
		public void A_pack_archive_goes_to_the_rom_loader_not_a_pack_install()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/downloads/Contra Remastered.zip", true, Ascii("PK\u0003\u0004\u0014"), false));
		}

		[Fact]
		public void A_pack_folder_is_reported_missing_not_installed()
		{
			Assert.Equal(DropAction.FileNotFound, DropRoute.Decide("/downloads/Contra Remastered", false, System.Array.Empty<byte>(), false));
		}
	}
}
