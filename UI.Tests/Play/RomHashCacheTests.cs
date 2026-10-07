using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1038, the host-free half: the ROM's own identity hash (ADR-0003/ADR-0039),
	//computed off the caller's thread and cached on the player's disk by path +
	//size + mtime, so a library of a few hundred ROMs is not re-hashed on every
	//visit (ADR-0264 Decisions 7 and 10).
	//
	//Every ROM here is a real file in a temp folder: the contract is about bytes
	//on a disk - a header, a trainer, trailing garbage - and a fake byte reader
	//would only test the fake. The mtimes are set explicitly with
	//File.SetLastWriteTimeUtc; a test that sleeps waiting for the clock to move
	//is a CI flake, so nothing here waits for time to pass.
	//
	//The expected hashes come from System.Security.Cryptography over bytes the
	//test built itself (the payload blocks it concatenated), never from the
	//implementation's own slice.
	public class RomHashCacheTests
	{
		//A fixed instant, off the wall clock: the filesystem holds it exactly and
		//re-setting it is idempotent, which is what makes "unchanged" testable at
		//all. The second stamp is hours away, far outside any filesystem's
		//rounding.
		private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		private static readonly DateTime OtherStamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		[Fact]
		public async Task An_iNES_rom_hashes_only_the_PRG_and_CHR_its_header_declares()
		{
			using Workspace workspace = new();
			byte[] prg = Bytes(0x4000, 0x11);
			byte[] chr = Bytes(0x2000, 0x22);
			string path = workspace.Write("game.nes", INes(prg, chr, trailer: Bytes(64, 0x33)));

			string sha1 = await workspace.NewCache().GetSha1Async(path);

			Assert.Equal(Sha1Of(Concatenate(prg, chr)), sha1);
			//The dump is not the file: trailing garbage after the declared payload
			//must not move the identity of the game (ADR-0044 item 1).
			Assert.NotEqual(Sha1Of(File.ReadAllBytes(path)), sha1);
		}

		[Fact]
		public async Task An_iNES_trainer_is_skipped_before_the_payload()
		{
			using Workspace workspace = new();
			byte[] prg = Bytes(0x4000, 0x44);
			byte[] chr = Bytes(0x2000, 0x55);
			byte[] trainer = Bytes(512, 0x66);
			string path = workspace.Write("trainered.nes", INes(prg, chr, trainer: trainer));

			string sha1 = await workspace.NewCache().GetSha1Async(path);

			Assert.Equal(Sha1Of(Concatenate(prg, chr)), sha1);
		}

		[Fact]
		public async Task A_nes_file_without_the_iNES_magic_hashes_the_whole_file()
		{
			using Workspace workspace = new();
			//A headerless dump (the shape a UNIF or a flat dump has): the magic is
			//what selects the rule, so with no magic there is no payload to find.
			byte[] rom = Bytes(0x6000, 0x77);
			string path = workspace.Write("headerless.nes", rom);

			string sha1 = await workspace.NewCache().GetSha1Async(path);

			Assert.Equal(Sha1Of(rom), sha1);
		}

		[Fact]
		public async Task A_game_boy_rom_hashes_the_whole_file_even_with_the_iNES_magic_in_front()
		{
			using Workspace workspace = new();
			//Byte-for-byte a valid iNES image, named `.gb`: the rule is chosen by
			//the file's extension (ADR-0039), so a Game Boy ROM that happens to
			//start with `NES\x1A` is still hashed whole.
			byte[] rom = INes(Bytes(0x4000, 0x88), Bytes(0x2000, 0x99));
			string path = workspace.Write("game.gb", rom);

			string sha1 = await workspace.NewCache().GetSha1Async(path);

			Assert.Equal(Sha1Of(rom), sha1);
		}

		[Fact]
		public async Task A_rom_whose_path_size_and_mtime_are_unchanged_reuses_the_hash_on_disk()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0xAA);
			byte[] second = Bytes(0x2000, 0xBB);
			string path = workspace.Write("castlevania.nes", first);
			File.SetLastWriteTimeUtc(path, Stamp);

			string original = await workspace.NewCache().GetSha1Async(path);
			Assert.Equal(Sha1Of(first), original);

			//Same path, same size, same mtime: the cached hash is the answer. A
			//second instance over the same directory proves it is the file on
			//disk doing the answering, not something this instance remembers.
			File.WriteAllBytes(path, second);
			File.SetLastWriteTimeUtc(path, Stamp);

			string reused = await workspace.NewCache().GetSha1Async(path);
			Assert.Equal(original, reused);
		}

		[Fact]
		public async Task A_changed_mtime_recomputes_the_hash()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0xCC);
			byte[] second = Bytes(0x2000, 0xDD);
			string path = workspace.Write("contra.nes", first);
			File.SetLastWriteTimeUtc(path, Stamp);
			RomHashCache cache = workspace.NewCache();

			string original = await cache.GetSha1Async(path);

			File.WriteAllBytes(path, second);
			File.SetLastWriteTimeUtc(path, OtherStamp);

			string recomputed = await cache.GetSha1Async(path);
			Assert.Equal(Sha1Of(second), recomputed);
			Assert.NotEqual(original, recomputed);
		}

		[Fact]
		public async Task A_changed_size_recomputes_the_hash()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0xEE);
			byte[] second = Bytes(0x3000, 0xEE);
			string path = workspace.Write("gradius.nes", first);
			File.SetLastWriteTimeUtc(path, Stamp);
			RomHashCache cache = workspace.NewCache();

			string original = await cache.GetSha1Async(path);

			File.WriteAllBytes(path, second);
			//The mtime is put back where it was: the size alone must be enough to
			//invalidate the entry.
			File.SetLastWriteTimeUtc(path, Stamp);

			string recomputed = await cache.GetSha1Async(path);
			Assert.Equal(Sha1Of(second), recomputed);
			Assert.NotEqual(original, recomputed);
		}

		[Fact]
		public async Task The_caller_supplied_size_time_and_console_key_the_cache()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0x0F);
			byte[] second = Bytes(0x2000, 0xF0);
			string path = workspace.Write("mario.nes", first);
			File.SetLastWriteTimeUtc(path, Stamp);
			RomHashCache cache = workspace.NewCache();

			//The shape the library scan already has: it stat'ed the file while
			//walking the folder and classified it, so it hands those facts over
			//rather than making the cache ask the filesystem again.
			string original = await cache.GetSha1Async(path, RomConsole.Nes, first.Length, Stamp);
			Assert.Equal(Sha1Of(first), original);

			File.WriteAllBytes(path, second);
			File.SetLastWriteTimeUtc(path, Stamp);

			string reused = await cache.GetSha1Async(path, RomConsole.Nes, second.Length, Stamp);
			Assert.Equal(original, reused);

			string recomputed = await cache.GetSha1Async(path, RomConsole.Nes, second.Length, OtherStamp);
			Assert.Equal(Sha1Of(second), recomputed);
		}

		[Fact]
		public async Task A_stamp_recorded_as_local_and_the_same_instant_recorded_as_utc_are_one_entry()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0x2A);
			byte[] second = Bytes(0x2000, 0xD5);
			RomHashCache cache = workspace.NewCache();

			//One moment in time, written down twice: once with a Local kind (the
			//shape a caller that stat'ed the file with File.GetLastWriteTime has)
			//and once with a Utc kind (what FileInfo.LastWriteTimeUtc yields). The
			//two clocks name the same instant, so they must name the same entry.
			DateTime local = Stamp.ToLocalTime();
			DateTime utc = Stamp;
			Assert.Equal(DateTimeKind.Local, local.Kind);
			Assert.Equal(DateTimeKind.Utc, utc.Kind);

			string localFirst = workspace.Write("local-first.nes", first);
			Assert.Equal(Sha1Of(first), await cache.GetSha1Async(localFirst, RomConsole.Nes, first.Length, local));

			//A hit answers with the hash of `first`; a miss reads the file again
			//and answers with the hash of `second`.
			File.WriteAllBytes(localFirst, second);
			Assert.Equal(Sha1Of(first), await cache.GetSha1Async(localFirst, RomConsole.Nes, second.Length, utc));

			//And the same the other way round.
			string utcFirst = workspace.Write("utc-first.nes", second);
			Assert.Equal(Sha1Of(second), await cache.GetSha1Async(utcFirst, RomConsole.Nes, second.Length, utc));

			File.WriteAllBytes(utcFirst, first);
			Assert.Equal(Sha1Of(second), await cache.GetSha1Async(utcFirst, RomConsole.Nes, first.Length, local));
		}

		[Fact]
		public async Task An_entry_carrying_the_current_hashing_contract_version_is_reused()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0x3C);
			byte[] second = Bytes(0x2000, 0xC3);
			string path = workspace.Write("versioned.nes", first);
			RomHashCache cache = workspace.NewCache();

			string original = await cache.GetSha1Async(path, RomConsole.Nes, first.Length, Stamp);

			//Every entry names the hashing contract it was written under, on its
			//first line, before the facts: a later contract has to be able to tell
			//its own entries from an older run's, and a hash is only worth keeping
			//while the rule that produced it is the rule this build applies.
			string entry = Assert.Single(Directory.GetFiles(workspace.CacheDirectory));
			Assert.Equal("v1", File.ReadAllLines(entry)[0]);

			File.WriteAllBytes(path, second);
			Assert.Equal(original, await cache.GetSha1Async(path, RomConsole.Nes, second.Length, Stamp));
		}

		[Fact]
		public async Task An_entry_carrying_another_hashing_contract_version_is_a_miss()
		{
			using Workspace workspace = new();
			byte[] first = Bytes(0x2000, 0x4D);
			byte[] second = Bytes(0x2000, 0xD4);
			string path = workspace.Write("stale-contract.nes", first);
			RomHashCache cache = workspace.NewCache();

			await cache.GetSha1Async(path, RomConsole.Nes, first.Length, Stamp);

			//The same entry, relabelled with a contract this build does not hash
			//under: every other field still matches the ROM, so only the version
			//line can make it a miss.
			string entry = Assert.Single(Directory.GetFiles(workspace.CacheDirectory));
			string[] lines = File.ReadAllLines(entry);
			lines[0] = "v0";
			File.WriteAllLines(entry, lines);

			File.WriteAllBytes(path, second);
			Assert.Equal(Sha1Of(second), await cache.GetSha1Async(path, RomConsole.Nes, second.Length, Stamp));

			//The miss is answered and repaired: the entry on disk is rewritten
			//under the contract this build hashes with, so the next call reads it.
			Assert.Equal("v1", File.ReadAllLines(entry)[0]);
		}

		[Fact]
		public async Task A_cache_directory_that_cannot_be_written_still_yields_the_hash()
		{
			using Workspace workspace = new();
			byte[] rom = Bytes(0x2000, 0x12);
			string path = workspace.Write("battletoads.nes", rom);

			//A path that is a file, not a folder: every write under it fails. A
			//cache that cannot write must cost a recomputation, never an answer.
			string blocked = Path.Combine(workspace.Root, "not-a-folder");
			File.WriteAllText(blocked, "");

			string sha1 = await new RomHashCache(blocked).GetSha1Async(path);
			Assert.Equal(Sha1Of(rom), sha1);

			string again = await new RomHashCache(blocked).GetSha1Async(path);
			Assert.Equal(Sha1Of(rom), again);
		}

		[Fact]
		public async Task A_cancelled_call_throws_instead_of_reading_the_rom()
		{
			using Workspace workspace = new();
			string path = workspace.Write("zelda.nes", Bytes(0x2000, 0x34));

			await Assert.ThrowsAnyAsync<OperationCanceledException>(
				() => workspace.NewCache().GetSha1Async(path, new CancellationToken(canceled: true)));
		}

		private static byte[] Bytes(int count, byte value)
		{
			byte[] bytes = new byte[count];
			Array.Fill(bytes, value);
			return bytes;
		}

		private static byte[] Concatenate(byte[] first, byte[] second)
		{
			byte[] joined = new byte[first.Length + second.Length];
			first.CopyTo(joined, 0);
			second.CopyTo(joined, first.Length);
			return joined;
		}

		private static string Sha1Of(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));

		//A real iNES image: 16-byte header (`NES\x1A`), an optional 512-byte
		//trainer, then the PRG and CHR blocks the header's sizes declare, then
		//whatever the dump carries after them.
		private static byte[] INes(byte[] prg, byte[] chr, byte[]? trainer = null, byte[]? trailer = null)
		{
			if(prg.Length % 0x4000 != 0 || chr.Length % 0x2000 != 0) {
				throw new ArgumentException("PRG is counted in 16 KB units and CHR in 8 KB ones.");
			}

			byte[] header = Bytes(16, 0);
			header[0] = (byte)'N';
			header[1] = (byte)'E';
			header[2] = (byte)'S';
			header[3] = 0x1A;
			header[4] = (byte)(prg.Length / 0x4000);
			header[5] = (byte)(chr.Length / 0x2000);
			//flags6 bit 2: a 512-byte trainer sits between the header and the PRG.
			header[6] = (byte)(trainer == null ? 0x00 : 0x04);

			byte[] image = header;
			if(trainer != null) {
				image = Concatenate(image, trainer);
			}
			image = Concatenate(image, prg);
			image = Concatenate(image, chr);
			if(trailer != null) {
				image = Concatenate(image, trailer);
			}
			return image;
		}

		//A scratch folder under the system temp directory - never the user's
		//home, and never a shared name: the cache, the ROMs and the blocked path
		//of one test all live and die inside it.
		private sealed class Workspace : IDisposable
		{
			public string Root { get; }
			public string RomDirectory { get; }
			public string CacheDirectory { get; }

			public Workspace()
			{
				Root = Path.Combine(Path.GetTempPath(), "mesen-romhash-" + Guid.NewGuid().ToString("N"));
				RomDirectory = Path.Combine(Root, "roms");
				CacheDirectory = Path.Combine(Root, "cache");
				Directory.CreateDirectory(RomDirectory);
				Directory.CreateDirectory(CacheDirectory);
			}

			public string Write(string name, byte[] bytes)
			{
				string path = Path.Combine(RomDirectory, name);
				File.WriteAllBytes(path, bytes);
				return path;
			}

			public RomHashCache NewCache() => new(CacheDirectory);

			public void Dispose()
			{
				try {
					Directory.Delete(Root, true);
				} catch(IOException) {
				} catch(UnauthorizedAccessException) {
				}
			}
		}
	}
}
