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

		private static readonly byte[] Zip = { (byte)'P', (byte)'K', 0x03, 0x04, 0x14 };

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

		//#986: a pack is told by its manifest, found by the existing discovery
		//rules (ADR-0040, ADR-0120): pack.json or hires.txt at the root of a zip
		//or of its single top-level folder, or at the root of a dropped folder.
		[Fact]
		public void A_pack_archive_goes_to_the_pack_install_not_the_rom_loader()
		{
			string[] entries = { "Contra Remastered/", "Contra Remastered/pack.json", "Contra Remastered/textures/hires.txt" };
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra Remastered.zip", true, Zip, false, false, entries));
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra Remastered.zip", true, Zip, false, false, new[] { "pack.json", "audio/hires.txt" }));
		}

		[Fact]
		public void A_pack_folder_is_installed_not_reported_missing()
		{
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra Remastered", false, System.Array.Empty<byte>(), false, true, new[] { "pack.json", "textures" }));
		}

		[Fact]
		public void A_zip_with_no_manifest_still_goes_to_the_rom_loader()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/games/Contra (USA).zip", true, Zip, false, false, new[] { "Contra (USA).nes" }));
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/games/Contra (USA).zip", true, Zip, false, false, new[] { "Contra/Contra (USA).nes", "Contra/readme.txt" }));
		}

		[Fact]
		public void A_legacy_hd_pack_is_a_pack_at_the_root_or_in_the_single_top_level_folder()
		{
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra HD.zip", true, Zip, false, false, new[] { "hires.txt", "1.png" }));
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra HD.zip", true, Zip, false, false, new[] { "Contra HD/hires.txt", "Contra HD/1.png" }));
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Contra HD", false, System.Array.Empty<byte>(), false, true, new[] { "hires.txt", "1.png" }));
		}

		//Deeper than the single top-level folder, or with two top-level folders,
		//is not found by the rule: the zip keeps the ROM loader's route.
		[Fact]
		public void A_manifest_outside_the_discovery_rule_does_not_make_a_pack()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/downloads/a.zip", true, Zip, false, false, new[] { "A/B/pack.json" }));
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/downloads/a.zip", true, Zip, false, false, new[] { "A/pack.json", "B/hires.txt" }));
		}

		//#993 review: Finder's "Compress" adds __MACOSX/ (and folders carry
		//.DS_Store) - neither is a second top-level folder, so the pack in the
		//one real folder is still found, as MepZipValidator finds it.
		[Fact]
		public void A_finder_zipped_pack_is_still_a_pack()
		{
			string[] finder = { "Pack/", "Pack/pack.json", "__MACOSX/", "__MACOSX/Pack/._pack.json", ".DS_Store", "Pack/.DS_Store" };
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/Pack.zip", true, Zip, false, false, finder));
			Assert.Equal(PackManifest.HdLegacy, DropRoute.FindPackManifest(new[] { "HD/hires.txt", "HD/1.png", "__MACOSX/HD/._hires.txt" }, false));
		}

		//#993 review: a loose file beside the pack's folder (readme.txt) does
		//not hide it - the validator's ADR-0120 fallback accepts that zip.
		[Fact]
		public void A_loose_file_beside_the_pack_folder_does_not_hide_it()
		{
			Assert.Equal(DropAction.InstallPack, DropRoute.Decide("/downloads/a.zip", true, Zip, false, false, new[] { "readme.txt", "A/pack.json" }));
			Assert.Equal(PackManifest.Mep, DropRoute.FindPackManifest(new[] { "pack.json", "A/readme.txt" }, false));
		}

		//#993 review: hires.txt makes a pack only with a PNG beside it - the
		//rule MepZipValidator applies (#161), so a lone manifest is not
		//classified as a pack the installer then rejects.
		[Fact]
		public void A_hires_txt_with_no_image_beside_it_is_not_a_pack()
		{
			Assert.Equal(DropAction.LoadRom, DropRoute.Decide("/downloads/a.zip", true, Zip, false, false, new[] { "hires.txt", "game.nes" }));
			Assert.Equal(PackManifest.None, DropRoute.FindPackManifest(new[] { "HD/hires.txt", "HD/notes.txt" }, false));
		}

		//#993 review: what the window does once a dropped pack's install
		//returns - the running-game branch offers the power cycle.
		[Fact]
		public void After_a_pack_install_the_running_game_is_offered_a_power_cycle()
		{
			Assert.Equal(PackInstallOutcome.ShowError, DropRoute.AfterPackInstall("InstallMepPackInvalidPack", true));
			Assert.Equal(PackInstallOutcome.ShowError, DropRoute.AfterPackInstall("InstallMepPackInvalidPack", false));
			Assert.Equal(PackInstallOutcome.ShowInstalled, DropRoute.AfterPackInstall("", false));
			Assert.Equal(PackInstallOutcome.OfferPowerCycle, DropRoute.AfterPackInstall("", true));
		}

		[Fact]
		public void A_folder_is_a_pack_only_by_the_manifest_at_its_root()
		{
			Assert.Equal(DropAction.FileNotFound, DropRoute.Decide("/downloads/stuff", false, System.Array.Empty<byte>(), false, true, new[] { "notes.txt" }));
		}

		[Fact]
		public void Pack_json_wins_over_hires_txt()
		{
			Assert.Equal(PackManifest.Mep, DropRoute.FindPackManifest(new[] { "hires.txt", "pack.json" }, false));
			Assert.Equal(PackManifest.HdLegacy, DropRoute.FindPackManifest(new[] { "P/hires.txt", "P/1.png" }, false));
			Assert.Equal(PackManifest.None, DropRoute.FindPackManifest(new[] { "P/hires.txt" }, true));
		}
	}
}
