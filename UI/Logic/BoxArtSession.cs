using System;
using System.Threading;

namespace Mesen.Logic
{
	//#1039: the one cache and the one SHA-1 cache of a session, built on first use.
	//The first use is a screenful of tiles asking from worker threads at the same
	//moment, so the build is thread-safe: a second BoxArtCache would carry its own
	//semaphore and in-flight table, which is more than ADR-0265 section 4's four
	//requests in flight and the same dump paid for twice.
	public sealed class BoxArtSession
	{
		private readonly Lazy<BoxArtCache> _cache;
		private readonly Lazy<RomHashCache> _hashes;

		public BoxArtSession(Func<BoxArtCache> createCache, Func<RomHashCache> createHashes)
		{
			_cache = new Lazy<BoxArtCache>(createCache, LazyThreadSafetyMode.ExecutionAndPublication);
			_hashes = new Lazy<RomHashCache>(createHashes, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public BoxArtCache Cache => _cache.Value;

		public RomHashCache Hashes => _hashes.Value;
	}
}
