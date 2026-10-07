using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Logic
{
	//#1039 (ADR-0265 sections 3 and 4): the key one tile's call is made under. A
	//tile is a `LibraryEntry` - the path that opens it and the console it is for -
	//and the collection is keyed by the DUMP, so the entry is not enough on its
	//own: the ROM's No-Intro SHA-1 is computed first (#1038's cache) and the name
	//the table gives back for that SHA-1 is what the collection is asked for.
	//Nothing here reads a file name: a ROM the table does not know has no name to
	//ask for, and this answers null rather than guessing one (ADR-0003 - the
	//collection is keyed by dump, never by what the file is called).
	//
	//Host-free (ADR-0138 §53): BCL only, and the three collaborators arrive as
	//constructor arguments (the cache, the hash cache and the table), so UI.Tests
	//drives the whole chain with a fake transport and a temp folder.
	//
	//This is the caller ADR-0265 section 4 describes: one call per tile, driven by
	//whoever is drawing them, never an enumeration of a library from inside.
	public sealed class BoxArtLibrary
	{
		private readonly BoxArtCache _cache;
		private readonly RomHashCache _hashes;
		private readonly Func<string, string?> _nameOf;

		//`noIntroName` is the SHA1 -> name table (#1038). It answers null for a dump
		//it does not know, and that null is the whole answer: no request is made for
		//a game the collection cannot be asked about by name.
		public BoxArtLibrary(BoxArtCache cache, RomHashCache hashes, Func<string, string?> noIntroName)
		{
			_cache = cache ?? throw new ArgumentNullException(nameof(cache));
			_hashes = hashes ?? throw new ArgumentNullException(nameof(hashes));
			_nameOf = noIntroName ?? throw new ArgumentNullException(nameof(noIntroName));
		}

		//The cover for one tile, or null when there is none to draw. Never throws:
		//ADR-0265 section 9 makes every failure path - offline, a 404, a ROM that
		//is no longer on the disk, a console the collection does not carry - one
		//thing, and that thing is the generic cover.
		public async Task<BoxArtCover?> GetCover(LibraryEntry entry, CancellationToken cancellationToken = default)
		{
			//`RomConsole -> BoxArtConsole` is a cast rather than a table, and that is
			//ADR-0265's own record: the two enums are the same machines in the same
			//order with the same values, and the cast is what unifies them once both
			//are on `main`. The one thing a cast cannot say is "the collection does
			//not carry this machine" - CacheTag answers "" for that, and a tile with
			//no repository to ask is answered here, before anything is hashed.
			BoxArtConsole console = (BoxArtConsole)(int)entry.Console;
			if(BoxArtSystems.CacheTag(console).Length == 0) {
				return null;
			}

			string sha1;
			try {
				sha1 = await _hashes.GetSha1Async(entry.Path, entry.Console, cancellationToken).ConfigureAwait(false);
			} catch(Exception) {
				//A path that is not there is the caller's mistake, and a cancelled
				//call is the tile walking away: neither is a cover, and neither is
				//allowed to reach the sheet as an exception (ADR-0265 section 9).
				return null;
			}

			string? name = _nameOf(sha1);
			if(string.IsNullOrEmpty(name)) {
				return null;
			}

			//The cache is the last word on all of it: the switch (off means no
			//request at all, but a cover already on the disk is still served), the
			//remembered miss, the two collections in order, and the ceiling.
			return await _cache.GetCover(console, sha1, name, cancellationToken).ConfigureAwait(false);
		}
	}
}
