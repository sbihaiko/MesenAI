using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//#1039 / ADR-0265 section 9: the four ways one call to the cache could still
	//outlive its timeout or answer twice for one ROM. Same fake transport and same
	//temp folder as the rest of the box-art tests; each test shrinks exactly the
	//number it is about and drives the cache through its public surface only.
	public class BoxArtCacheReviewTests : IDisposable
	{
		//The table's key shape: 40 hex characters, the ROM payload's SHA-1.
		private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
		private const string OtherSha1 = "fedcba9876543210fedcba9876543210fedcba98";
		private const string Name = "Super Mario Bros. 3 (USA)";

		private readonly DirectoryInfo _cache;

		public BoxArtCacheReviewTests()
		{
			_cache = Directory.CreateTempSubdirectory("mesen-boxart-review-");
		}

		public void Dispose()
		{
			try {
				_cache.Delete(true);
			} catch(IOException) {
			}
		}

		private BoxArtCache Cache(FakeBoxArtSender sender, BoxArtCacheOptions? options = null) =>
			new(sender.Send, _cache.FullName, options);

		private string ConsoleFolder => Path.Combine(_cache.FullName, "nes");

		//ADR-0265 section 9, "offline is not a wait": the deadline covers the queue.
		//A tile waiting behind MaxConcurrentRequests is holding no socket - it has not
		//sent anything - so the only bound on it is the one the call itself carries.
		[Fact]
		public async Task A_tile_queued_behind_the_ceiling_gives_up_within_the_request_timeout()
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

			//A transport that answers only when the test says so and ignores the token
			//it was handed: the request ahead of the queued tile never ends on its own.
			FakeBoxArtSender sender = new(async (_, _, _) => {
				started.TrySetResult();
				await gate.Task;
				return BoxArtHttpResponse.Ok(FakeImages.Png());
			});

			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions {
				MaxConcurrentRequests = 1,
				RequestTimeout = TimeSpan.FromMilliseconds(250)
			});

			Task<BoxArtCover?> holder = cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			await started.Task;

			//The second tile never gets a turn: the ceiling is one and the tile ahead
			//of it is stuck in the transport.
			Task<BoxArtCover?> queued = cache.GetCover(BoxArtConsole.Nes, OtherSha1, Name);
			Task finished = await Task.WhenAny(queued, Task.Delay(TimeSpan.FromSeconds(5)));

			//The queued tile falls back to its generic cover within the per-request
			//timeout - it does not inherit the request ahead of it as its deadline.
			Assert.Same(queued, finished);
			Assert.Null(await queued);
			//A tile that never gets its turn asks nothing about the game, so nothing
			//is recorded for it either.
			Assert.False(File.Exists(Path.Combine(ConsoleFolder, OtherSha1 + ".miss")));

			gate.TrySetResult();
			await holder;
		}

		//ADR-0265 section 9 again, one level down: the delegation contract says the
		//transport must end its read when the token says so, but a bug in the shipped
		//adapter is not a reason for a library to never draw. The bound has to hold
		//even when the other side of the delegate ignores it.
		[Fact]
		public async Task A_transport_that_ignores_its_cancellation_token_cannot_hang_the_call()
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

			FakeBoxArtSender sender = new(async (_, _, _) => {
				await gate.Task;
				return BoxArtHttpResponse.Ok(FakeImages.Png());
			});

			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions { RequestTimeout = TimeSpan.FromMilliseconds(150) });

			Stopwatch watch = Stopwatch.StartNew();
			Task<BoxArtCover?> cover = cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			Task finished = await Task.WhenAny(cover, Task.Delay(TimeSpan.FromSeconds(5)));
			watch.Stop();

			Assert.Same(cover, finished);
			Assert.Null(await cover);
			//Bounded by the two timeouts - box art, then the title screen - and not by
			//the transport.
			Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the call took {watch.Elapsed}");
			//A request nobody answered is not evidence about the game.
			Assert.False(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));

			gate.TrySetResult();
		}

		//The key is the ROM's identity (ADR-0265 section 3), and the table spells it
		//in lower case. The same dump reached through an upper-case spelling is the
		//same game, so it is answered from the entry the lower-case call wrote rather
		//than by a second download into a second entry beside it.
		[Fact]
		public async Task An_upper_case_hash_is_the_same_cache_entry_as_a_lower_case_one()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			BoxArtCache cache = Cache(sender);

			BoxArtCover? upper = await cache.GetCover(BoxArtConsole.Nes, Sha1.ToUpperInvariant(), Name);

			Assert.NotNull(upper);
			Assert.Equal(1, sender.RequestCount);

			BoxArtCover? lower = await cache.GetCover(BoxArtConsole.Nes, Sha1, Name);

			Assert.NotNull(lower);
			Assert.Equal(upper!.FilePath, lower!.FilePath);
			Assert.Equal(1, sender.RequestCount);
			//One game, one file - and it is the name the key normalises to.
			Assert.Equal(
				new[] { Path.Combine(ConsoleFolder, Sha1 + ".boxart.png") },
				Directory.GetFiles(ConsoleFolder));
		}

		//The same rule on the negative entry: the two spellings share one miss, so the
		//second visit to the tile is a disk read and not a third pair of requests.
		[Fact]
		public async Task An_upper_case_hash_records_the_miss_the_lower_case_hash_reads()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Missing();
			BoxArtCache cache = Cache(sender);

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1.ToUpperInvariant(), Name));
			Assert.Equal(2, sender.RequestCount);

			//One miss, under the name the key normalises to. The assertion is on the
			//name the entry was written with and not on File.Exists, because the cache
			//directory sits on whatever volume the player has: on a case-insensitive
			//one a second entry differing only in case is invisible to existence.
			Assert.Equal(
				new[] { Path.Combine(ConsoleFolder, Sha1 + ".miss") },
				Directory.GetFiles(ConsoleFolder));

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(2, sender.RequestCount);
		}

		//Two visible tiles of the same ROM are two calls to this method, and both may
		//be in the transport at once. The one thing they must not do is fetch the same
		//picture twice and then finish together: the cover is published by one rename
		//onto one name, so the tile that loses that race reads its own failure as "the
		//collection has no art" and leaves a miss behind for a game whose cover is
		//right there.
		[Fact]
		public async Task Two_tiles_of_the_same_rom_neither_collide_nor_record_a_stray_miss()
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

			FakeBoxArtSender sender = new(async (url, _, _) => {
				started.TrySetResult();
				await gate.Task;
				return url.AbsolutePath.Contains("/Named_Boxarts/", StringComparison.Ordinal)
					? BoxArtHttpResponse.Ok(FakeImages.Png())
					: BoxArtHttpResponse.NotFound();
			});

			BoxArtCache cache = Cache(sender);

			Task<BoxArtCover?> first = cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			await started.Task;

			//The second tile of the same ROM, drawn while the first one's request is
			//still open.
			Task<BoxArtCover?> second = cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			await Task.Delay(200);

			//One ROM is one download: the second tile waits on the first tile's request
			//rather than opening a second one beside it.
			Assert.Equal(1, sender.RequestCount);

			gate.SetResult();
			BoxArtCover?[] covers = await Task.WhenAll(first, second);

			Assert.All(covers, cover => Assert.NotNull(cover));
			Assert.False(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));
			//The cover and nothing else: no negative entry left behind by the tile
			//that lost the race.
			Assert.Equal(
				new[] { Path.Combine(ConsoleFolder, Sha1 + ".boxart.png") },
				Directory.GetFiles(ConsoleFolder));
		}
	}
}
