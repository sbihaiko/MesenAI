using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//#1039 review finding 2: the cache and the SHA-1 cache are built on first use,
	//and the first use is the first screenful's worth of tiles asking at once from
	//worker threads. One session means ONE of each (one semaphore, one in-flight
	//table), however many threads race for it.
	public class BoxArtSessionTests : IDisposable
	{
		private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("mesen-boxart-session-");

		public void Dispose()
		{
			try {
				_root.Delete(true);
			} catch {
			}
		}

		[Fact]
		public void Concurrent_first_use_builds_one_cache_and_one_hash_cache()
		{
			int cacheBuilds = 0;
			int hashBuilds = 0;
			BoxArtSession session = new(
				() => {
					Interlocked.Increment(ref cacheBuilds);
					Thread.Sleep(20);
					return new BoxArtCache((_, _, _) => Task.FromResult(BoxArtHttpResponse.NotFound()), _root.FullName);
				},
				() => {
					Interlocked.Increment(ref hashBuilds);
					Thread.Sleep(20);
					return new RomHashCache(Path.Combine(_root.FullName, "hashes"));
				});

			using Barrier start = new(24);
			List<(BoxArtCache, RomHashCache)> seen = Enumerable.Range(0, 24)
				.Select(_ => Task.Run(() => {
					start.SignalAndWait();
					return (session.Cache, session.Hashes);
				}))
				.ToList()
				.Select(t => t.GetAwaiter().GetResult())
				.ToList();

			Assert.Equal(1, cacheBuilds);
			Assert.Equal(1, hashBuilds);
			Assert.Single(seen.Select(s => s.Item1).Distinct());
			Assert.Single(seen.Select(s => s.Item2).Distinct());
		}
	}
}
