using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//#1039 / ADR-0265 sections 3, 4 and 6: the key one tile's call is made under.
	//A tile is a file and a console; the collection is keyed by the dump, so the
	//chain is ROM file -> No-Intro SHA-1 (#1038's cache) -> the name the table
	//gives back -> the cache's own console tag and SHA-1. Every test here is about
	//that chain, and the sender's request count is what says whether a request was
	//made at all - the same observable the ADR's "no name, no request" rule turns
	//on.
	//
	//What a tile then does with the answer is the sheet's business and is proved in
	//UI.HeadlessTests/PlayerLibraryBoxArtTests.
	public class BoxArtLibraryTests : IDisposable
	{
		private const string Name = "Contra (USA)";

		private readonly DirectoryInfo _root;
		private readonly DirectoryInfo _cache;
		private readonly DirectoryInfo _hashes;
		private readonly string _rom;

		public BoxArtLibraryTests()
		{
			_root = Directory.CreateTempSubdirectory("mesen-boxart-lib-");
			_cache = Directory.CreateDirectory(Path.Combine(_root.FullName, "cache"));
			_hashes = Directory.CreateDirectory(Path.Combine(_root.FullName, "hashes"));
			_rom = Path.Combine(_root.FullName, "Contra (U) [!].nes");
			File.WriteAllBytes(_rom, Nrom());
		}

		public void Dispose()
		{
			try {
				_root.Delete(true);
			} catch(IOException) {
			}
		}

		//A file the No-Intro contract has a range for: the iNES header and its
		//payload, so the hash comes from the same rule the shipped app uses.
		private static byte[] Nrom()
		{
			byte[] rom = new byte[16 + 64];
			rom[0] = (byte)'N';
			rom[1] = (byte)'E';
			rom[2] = (byte)'S';
			rom[3] = 0x1A;
			rom[4] = 1;
			rom[5] = 1;
			for(int i = 16; i < rom.Length; i++) {
				rom[i] = (byte)i;
			}
			return rom;
		}

		private LibraryEntry Entry() => new(_rom, RomConsole.Nes, "Contra");

		private BoxArtLibrary Library(FakeBoxArtSender sender, Func<string, string?> names, BoxArtCacheOptions? options = null) =>
			new(new BoxArtCache(sender.Send, _cache.FullName, options), new RomHashCache(_hashes.FullName), names);

		//The SHA-1 the collection is keyed by, read from the same contract the
		//shipped chain reads it from - and lowercased, which is the spelling the
		//cache's own file name uses (BoxArtCache normalises the caller's case
		//before it becomes a path).
		private Task<string> Sha1() =>
			new RomHashCache(_hashes.FullName).GetSha1Async(_rom, RomConsole.Nes).ContinueWith(t => t.Result.ToLowerInvariant());

		//A cached cover for this ROM, written where the cache's layout says a box
		//art lives. Nothing here goes through the cache's writer on purpose: a test
		//that seeds the disk by calling the code under test would only prove the two
		//halves agree with each other.
		private async Task<string> CacheBoxArt()
		{
			string folder = Path.Combine(_cache.FullName, "nes");
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, await Sha1() + ".boxart.png");
			File.WriteAllBytes(path, FakeImages.Png());
			return path;
		}

		[Fact]
		public async Task A_rom_the_table_does_not_know_is_asked_for_nothing()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());

			BoxArtCover? cover = await Library(sender, _ => null).GetCover(Entry(), CancellationToken.None);

			Assert.Null(cover);
			//ADR-0265 section 3: with no name there is nothing to ask for, and the
			//tile falls straight to its generic cover.
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task A_cached_box_art_is_served_from_the_disk_and_the_network_is_left_alone()
		{
			string cached = await CacheBoxArt();
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());

			BoxArtCover? cover = await Library(sender, _ => Name).GetCover(Entry(), CancellationToken.None);

			Assert.NotNull(cover);
			Assert.Equal(BoxArtCoverKind.Boxart, cover!.Kind);
			//The very file that was on the disk, under the console tag and SHA-1
			//this chain had to produce for it to be found at all.
			Assert.Equal(cached, cover.FilePath);
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task A_known_name_is_asked_for_and_a_box_art_beats_a_title_screen()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png(), FakeImages.Jpeg());

			BoxArtCover? cover = await Library(sender, _ => Name).GetCover(Entry(), CancellationToken.None);

			Assert.NotNull(cover);
			Assert.Equal(BoxArtCoverKind.Boxart, cover!.Kind);
			Assert.EndsWith(".boxart.png", cover.FilePath);
			//The box art answered, so the title screen was never asked for: the
			//priority is a request saved, not only a draw order.
			Assert.Equal(1, sender.RequestCount);
			Uri request = sender.Requests[0];
			Assert.Equal("raw.githubusercontent.com", request.Host);
			//The name the table returned, in the console's own repository and in the
			//box-art collection - the chain passes the table's name through and
			//invents nothing of its own (the escape is the URL's, so it is unescaped
			//before it is read back as a file name).
			Assert.Contains("/Nintendo_-_Nintendo_Entertainment_System/", request.AbsolutePath);
			Assert.Contains("/Named_Boxarts/", request.AbsolutePath);
			Assert.EndsWith("/Contra (USA).png", Uri.UnescapeDataString(request.AbsolutePath));
		}

		[Fact]
		public async Task A_console_the_collection_does_not_carry_is_asked_for_nothing()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			LibraryEntry unknown = new(_rom, RomConsole.Unknown, "Contra");

			BoxArtCover? cover = await Library(sender, _ => Name).GetCover(unknown, CancellationToken.None);

			Assert.Null(cover);
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task A_rom_that_is_no_longer_on_the_disk_answers_null_rather_than_throwing()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			LibraryEntry gone = new(Path.Combine(_root.FullName, "deleted.nes"), RomConsole.Nes, "Contra");

			BoxArtCover? cover = await Library(sender, _ => Name).GetCover(gone, CancellationToken.None);

			Assert.Null(cover);
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task With_the_switch_off_a_known_name_still_makes_no_request()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			BoxArtCacheOptions off = new() { DownloadEnabled = false };

			BoxArtCover? cover = await Library(sender, _ => Name, off).GetCover(Entry(), CancellationToken.None);

			Assert.Null(cover);
			//Not a lighter request, not a quieter one: none at all (ADR-0265
			//section 8), which is the promise the Settings switch makes.
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task With_the_switch_off_a_cover_already_on_the_disk_is_still_served()
		{
			string cached = await CacheBoxArt();
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			BoxArtCacheOptions off = new() { DownloadEnabled = false };

			BoxArtCover? cover = await Library(sender, _ => Name, off).GetCover(Entry(), CancellationToken.None);

			//Reading the player's own disk is not a request (ADR-0265 section 8),
			//so turning the switch off never throws away art they already have.
			Assert.NotNull(cover);
			Assert.Equal(cached, cover!.FilePath);
			Assert.Equal(0, sender.RequestCount);
		}
	}
}
