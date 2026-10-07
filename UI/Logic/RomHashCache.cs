using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Logic
{
	//A ROM's own identity hash - the No-Intro SHA-1 the library matches a game by
	//(ADR-0003, ADR-0039) - computed off the caller's thread and kept on the
	//player's disk, so a library of a few hundred ROMs is hashed once instead of on
	//every visit (#1038, ADR-0264 Decisions 7 and 10).
	//
	//**The byte range is not decided here.** It is Core's own contract, ported
	//verbatim from `MepPackManager::ComputeNoIntroSha1`
	//(Core/Shared/EnhancementPacks/MepPackManager.cpp:136-176) and cited rule by
	//rule at PayloadRange below. Nothing in this file invents a header heuristic: a
	//rule the C++ source does not state does not exist here, and a ROM that the
	//contract does not cover is hashed whole, which is what the source does too.
	//
	//**The cache key is path + size + mtime, and only that.** Same path, same size,
	//same last-write time: the entry on disk is the answer. A file rewritten with
	//the same length and the same stamp is served the old hash - that is the
	//contract the issue asks for, and it is also why the mtime is part of the key
	//rather than the size alone: a patched ROM usually keeps its length.
	//
	//**A cache is never allowed to be a failure mode.** An entry that cannot be
	//read, one whose numbers do not parse, a cache directory that cannot be
	//written: each costs a recomputation and never an answer, and no exception from
	//the disk below reaches the caller.
	//
	//Host-free (ADR-0123/ADR-0138 §53): BCL only - the file system and
	//System.Security.Cryptography - so it dual-compiles into UI.Tests and the sheet
	//can call it without a window. The cache directory is a constructor argument
	//the caller resolves (the app-support folder in the shipped app, a temp folder
	//in the tests): this class knows nothing about where a player's files live.
	public sealed class RomHashCache
	{
		//The cache directory this instance reads and writes - the folder the caller
		//resolved, never a repo-relative path.
		private readonly string _cacheDirectory;

		public RomHashCache(string cacheDirectory)
		{
			_cacheDirectory = string.IsNullOrEmpty(cacheDirectory)
				? throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory))
				: cacheDirectory;
		}

		//The ROM's No-Intro SHA-1, 40 uppercase hex digits (ADR-0039: the format
		//`SHA1::GetHash` already produces). Never blocks the caller's thread: the
		//stat, the read and the hash all happen on the thread pool, and the returned
		//task is cancellable between read blocks.
		//
		//An unreadable path throws - a file the caller named but that is not there
		//is a caller's mistake, not a cache miss - and so does a cancelled token.
		public Task<string> GetSha1Async(string romPath, CancellationToken cancellationToken = default) =>
			Task.Run(() => {
				FileInfo info = new(romPath);
				return GetOrCompute(romPath, info.Length, info.LastWriteTimeUtc, cancellationToken);
			}, cancellationToken);

		//The same hash for a caller that already classified the file (RomConsoleKinds
		//is the one table of what a ROM file is), so the library does not classify it
		//twice. **The console is carried, not consulted**: ADR-0039 derives the byte
		//range from the file's own extension and magic, which is exactly what the
		//C++ source does, so a console-keyed rule here would be a rule the contract
		//does not state.
		public Task<string> GetSha1Async(string romPath, RomConsole console, CancellationToken cancellationToken = default) =>
			Task.Run(() => {
				FileInfo info = new(romPath);
				return GetOrCompute(romPath, info.Length, info.LastWriteTimeUtc, cancellationToken);
			}, cancellationToken);

		//The same hash for a caller that already holds the file's facts - the shape
		//the library scan has, since it stat'ed the file while walking the folder and
		//would otherwise make this class ask the file system a second time. The two
		//values key the cache; the bytes are still read from the path, so a size the
		//caller got wrong cannot change what is hashed.
		public Task<string> GetSha1Async(string romPath, RomConsole console, long size, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default) =>
			Task.Run(() => GetOrCompute(romPath, size, lastWriteTimeUtc, cancellationToken), cancellationToken);

		private string GetOrCompute(string romPath, long size, DateTime lastWriteTimeUtc, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			//The full path is the key, and the entry's own name is a hash of it: a
			//ROM's name is arbitrary (a colon on one file system, a quote on the
			//next), and a path used as a file name would be a portability bug rather
			//than a cache.
			string entryPath = Path.Combine(_cacheDirectory, CacheEntryName(Path.GetFullPath(romPath)));

			string? cached = ReadEntry(entryPath, size, lastWriteTimeUtc.Ticks);
			if(cached != null) {
				return cached;
			}

			string sha1 = ComputeNoIntroSha1(romPath, cancellationToken);
			WriteEntry(entryPath, size, lastWriteTimeUtc.Ticks, sha1);
			return sha1;
		}

		//The SHA-1 of the ROM payload, per ADR-0003/ADR-0039. The file is streamed,
		//never loaded whole: a library scan hashes whatever an archive's member or a
		//homebrew dump happens to be, and the payload is the only part of it that is
		//read.
		private static string ComputeNoIntroSha1(string path, CancellationToken cancellationToken)
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000, FileOptions.SequentialScan);

			long fileSize = stream.Length;
			//The rule reads at most the header (the iNES one is 16 bytes), so that is
			//all that is buffered before the range is known.
			byte[] head = new byte[(int)Math.Min(16, fileSize)];
			ReadExactly(stream, head);
			(long offset, long length) = PayloadRange(head, fileSize, Path.GetExtension(path));

			stream.Seek(offset, SeekOrigin.Begin);
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
			byte[] buffer = new byte[0x10000];
			long remaining = length;
			while(remaining > 0) {
				cancellationToken.ThrowIfCancellationRequested();
				int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
				if(read <= 0) {
					break;
				}
				hash.AppendData(buffer, 0, read);
				remaining -= read;
			}
			//Uppercase, matching `SHA1::GetHash` (ADR-0039: "Output is 40 uppercase
			//hex digits"). Comparison with a table is case-insensitive, but the cached
			//value is stored in the one format the contract names.
			return Convert.ToHexString(hash.GetHashAndReset());
		}

		//The byte range ADR-0039 hashes for one file - **ported verbatim** from
		//`MepPackManager::ComputeNoIntroSha1`
		//(Core/Shared/EnhancementPacks/MepPackManager.cpp:136-176), rule for rule:
		//
		//  - `.nes` whose first four bytes are the iNES magic `NES\x1A`: skip the
		//    16-byte header and, when flags6 bit 2 is set, the 512-byte trainer; then
		//    clamp to the PRG+CHR the header declares (ADR-0044 item 1), including the
		//    NES 2.0 size MSBs, so a dump with trailing garbage still matches its
		//    clean No-Intro entry. A `.nes` file without the magic is hashed whole
		//    (the source's own note: UNIF is unsupported until No-Intro publishes a
		//    rule for it).
		//  - `.sfc/.smc/.swc/.fig/.bs/.st` whose size is `n * 1024 + 512`: skip the
		//    512-byte copier header. No console of the library's seven reaches this
		//    branch, but the contract does and Core applies it, so it is ported rather
		//    than dropped - a hash that disagreed with Core's would make the same ROM
		//    two different games.
		//  - everything else (the Game Boy, Game Boy Color, Game Boy Advance, Master
		//    System, SG-1000 and Game Gear rules ADR-0039 names): the whole file.
		//
		//`head` holds the file's first `min(16, size)` bytes, so `head.Length >= 16`
		//is the source's own `size >= 16` guard.
		private static (long Offset, long Length) PayloadRange(ReadOnlySpan<byte> head, long size, string extension)
		{
			long offset = 0;
			string ext = extension.ToLowerInvariant();
			if(ext == ".nes") {
				//iNES: 16-byte header, optional 512-byte trainer (flags6 bit 2)
				if(head.Length >= 16 && head[0] == (byte)'N' && head[1] == (byte)'E' && head[2] == (byte)'S' && head[3] == 0x1A) {
					offset = 16;
					if((head[6] & 0x04) != 0) {
						offset += 512;
					}
					//ADR-0044: hash only the PRG+CHR the header declares, so a dump
					//with trailing garbage still matches its clean No-Intro entry
					long prgUnits = head[4];
					long chrUnits = head[5];
					if((head[7] & 0x0C) == 0x08 && (head[9] & 0x0F) != 0x0F && (head[9] >> 4) != 0x0F) {
						//NES 2.0 size MSBs (exponent-multiplier form left alone)
						prgUnits |= (long)(head[9] & 0x0F) << 8;
						chrUnits |= (long)(head[9] >> 4) << 8;
					}
					long declared = offset + prgUnits * 0x4000 + chrUnits * 0x2000;
					if(declared > offset && declared < size) {
						size = declared;
					}
				}
			} else if(ext is ".sfc" or ".smc" or ".swc" or ".fig" or ".bs" or ".st") {
				//SNES copier header
				if(size % 1024 == 512) {
					offset = 512;
				}
			}

			if(offset > size) {
				offset = size;
			}
			return (offset, size - offset);
		}

		//The key's file name: the SHA-1 of the full path, hex. Two ROMs with the same
		//name in different folders are two entries, and no path can name a file that
		//its own file system rejects.
		private static string CacheEntryName(string fullPath) =>
			Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(fullPath))) + ".sha1";

		//The hash this path+size+mtime already has on disk, or null when there is
		//none to trust. A truncated entry, a stamp that does not parse, a hash that is
		//not 40 hex characters: all answered null, so the ROM is re-hashed rather
		//than served a value nobody can vouch for.
		private static string? ReadEntry(string entryPath, long size, long lastWriteTicks)
		{
			try {
				string[] lines = File.ReadAllLines(entryPath);
				if(lines.Length < 3) {
					return null;
				}
				if(!long.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out long cachedSize) || cachedSize != size) {
					return null;
				}
				if(!long.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out long cachedTicks) || cachedTicks != lastWriteTicks) {
					return null;
				}
				string sha1 = lines[2].Trim();
				return IsSha1(sha1) ? sha1 : null;
			} catch(IOException) {
				return null;
			} catch(UnauthorizedAccessException) {
				return null;
			}
		}

		//The entry lands on a scratch name and appears in one rename, the same
		//publish rule the box art cache uses (ADR-0265 §7): a crash mid-write leaves
		//scratch that nothing reads, never a half-written entry that a later run
		//would take for a hash.
		private static void WriteEntry(string entryPath, long size, long lastWriteTicks, string sha1)
		{
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
				string scratch = entryPath + ".tmp";
				File.WriteAllText(
					scratch,
					size.ToString(CultureInfo.InvariantCulture) + "\n"
						+ lastWriteTicks.ToString(CultureInfo.InvariantCulture) + "\n"
						+ sha1 + "\n");
				File.Move(scratch, entryPath, overwrite: true);
			} catch(IOException) {
				//A cache the player's disk will not take costs a recomputation next
				//time and nothing else - the caller already has its hash.
			} catch(UnauthorizedAccessException) {
			}
		}

		private static bool IsSha1(string value)
		{
			if(value.Length != 40) {
				return false;
			}
			foreach(char c in value) {
				bool hex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
				if(!hex) {
					return false;
				}
			}
			return true;
		}

		//Reads exactly `buffer.Length` bytes, or as many as the stream holds: only
		//ever called with a buffer sized to `min(16, fileLength)`, so a short read
		//means the file shrank under us and the range logic sees a shorter header
		//rather than uninitialised bytes.
		private static void ReadExactly(Stream stream, byte[] buffer)
		{
			int total = 0;
			while(total < buffer.Length) {
				int read = stream.Read(buffer, total, buffer.Length - total);
				if(read <= 0) {
					break;
				}
				total += read;
			}
		}
	}
}
