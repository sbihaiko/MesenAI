using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//#1039 / ADR-0265: what one call to the cache does, driven by a fake transport
	//and a temp folder. Every number here is a decision (the size cap, the miss
	//expiry, the timeout, the concurrency ceiling) and each test shrinks exactly
	//the one it is about.
	public class BoxArtCacheTests : IDisposable
	{
		//The table's key shape: 40 hex characters, the ROM payload's SHA-1.
		private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
		private const string OtherSha1 = "fedcba9876543210fedcba9876543210fedcba98";
		private const string Name = "Super Mario Bros. 3 (USA)";

		private readonly DirectoryInfo _cache;

		public BoxArtCacheTests()
		{
			_cache = Directory.CreateTempSubdirectory("mesen-boxart-");
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

		[Fact]
		public async Task A_downloaded_box_art_is_cached_and_the_second_call_asks_for_nothing()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			BoxArtCache cache = Cache(sender);

			BoxArtCover? first = await cache.GetCover(BoxArtConsole.Nes, Sha1, Name);

			Assert.NotNull(first);
			Assert.Equal(BoxArtCoverKind.Boxart, first!.Kind);
			Assert.True(File.Exists(first.FilePath), $"{first.FilePath} should have been written");
			Assert.Equal(1, sender.RequestCount);

			BoxArtCover? second = await cache.GetCover(BoxArtConsole.Nes, Sha1, Name);

			Assert.NotNull(second);
			Assert.Equal(first.FilePath, second!.FilePath);
			Assert.Equal(BoxArtCoverKind.Boxart, second.Kind);
			//The point of the cache: the second visit to the tile is a disk read.
			Assert.Equal(1, sender.RequestCount);
		}

		[Fact]
		public async Task A_title_screen_is_used_when_the_collection_has_no_box_art()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.TitlesOnly(FakeImages.Jpeg());
			BoxArtCache cache = Cache(sender);

			BoxArtCover? cover = await cache.GetCover(BoxArtConsole.GameBoy, Sha1, Name);

			Assert.NotNull(cover);
			Assert.Equal(BoxArtCoverKind.Title, cover!.Kind);
			Assert.EndsWith(".title.jpg", cover.FilePath);
			//Box art first, then the title screen - in that order.
			Assert.Equal(2, sender.RequestCount);
			Assert.Contains("/Named_Boxarts/", sender.Requests[0].AbsolutePath);
			Assert.Contains("/Named_Titles/", sender.Requests[1].AbsolutePath);
		}

		[Fact]
		public async Task A_game_the_collection_does_not_have_is_remembered_as_a_miss()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Missing();
			BoxArtCache cache = Cache(sender);

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(2, sender.RequestCount);
			Assert.True(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));
			//Not re-asked on the next visit: a miss costs one pair of requests, not
			//one pair per look.
			Assert.Equal(2, sender.RequestCount);
		}

		[Fact]
		public async Task A_recorded_miss_expires_and_is_asked_again()
		{
			DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
			FakeBoxArtSender sender = FakeBoxArtSender.Missing();

			BoxArtCache first = Cache(sender, new BoxArtCacheOptions { Clock = () => now });
			Assert.Null(await first.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(2, sender.RequestCount);

			//Still inside MissExpiry (30 days): nothing is asked.
			BoxArtCache sameDay = Cache(sender, new BoxArtCacheOptions { Clock = () => now.AddDays(29) });
			Assert.Null(await sameDay.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(2, sender.RequestCount);

			//Past it: the game may have been added to the collection since.
			BoxArtCache later = Cache(sender, new BoxArtCacheOptions { Clock = () => now.AddDays(31) });
			Assert.Null(await later.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(4, sender.RequestCount);
		}

		[Fact]
		public async Task An_oversized_body_is_rejected_and_nothing_is_cached()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png(4096));
			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions { MaxImageBytes = 64 });

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));

			Assert.Empty(Directory.GetFiles(ConsoleFolder, "*.png"));
			Assert.Empty(Directory.GetFiles(ConsoleFolder, "*.jpg"));
			//Rejected, but recorded: the size of a game's art is not going to change
			//between two tiles.
			Assert.True(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));
		}

		[Fact]
		public async Task A_body_that_is_not_an_image_is_rejected()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.NotAnImage());
			BoxArtCache cache = Cache(sender);

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));

			Assert.Empty(Directory.GetFiles(ConsoleFolder, "*.png"));
			Assert.Empty(Directory.GetFiles(ConsoleFolder, "*.jpg"));
		}

		[Fact]
		public async Task The_switch_off_makes_no_request_at_all()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions { DownloadEnabled = false });

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));

			//The switch means what it says: not a quieter request, none.
			Assert.Equal(0, sender.RequestCount);
			Assert.False(Directory.Exists(ConsoleFolder));
		}

		[Fact]
		public async Task The_switch_off_still_serves_a_cover_already_on_disk()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());
			Assert.NotNull(await Cache(sender).GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(1, sender.RequestCount);

			//Off is a refusal to talk to a server, not a reason to hide the player's
			//own file.
			BoxArtCover? cover = await Cache(sender, new BoxArtCacheOptions { DownloadEnabled = false })
				.GetCover(BoxArtConsole.Nes, Sha1, Name);

			Assert.NotNull(cover);
			Assert.Equal(1, sender.RequestCount);
		}

		[Fact]
		public async Task An_unreachable_collection_returns_null_without_recording_a_miss()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Offline();
			BoxArtCache cache = Cache(sender);

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));
			Assert.Equal(2, sender.RequestCount);

			//A transport failure says something about the network and nothing about
			//the game: one offline session must not blank every visible cover for
			//thirty days.
			Assert.False(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));

			Assert.Null(await cache.GetCover(BoxArtConsole.Nes, Sha1, Name));
			//Nothing was recorded, so the tile asks again once the network is back.
			Assert.Equal(4, sender.RequestCount);
		}

		[Fact]
		public async Task A_transport_that_never_answers_gives_up_within_the_timeout()
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Hanging();
			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) });

			Stopwatch watch = Stopwatch.StartNew();
			BoxArtCover? cover = await cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			watch.Stop();

			Assert.Null(cover);
			//The library opens and plays as fast offline as online: no tile waits on
			//the network, so the whole call is bounded by the two timeouts and not by
			//the transport.
			Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the call took {watch.Elapsed}");
			//A timeout is a transport failure like any other: no answer, no miss.
			Assert.False(File.Exists(Path.Combine(ConsoleFolder, Sha1 + ".miss")));
		}

		[Theory]
		//An unknown console has no repository and so no URL.
		[InlineData(BoxArtConsole.Unknown, Sha1, Name)]
		//The table's keys are 40 hex characters; anything else is a mistake, and the
		//sha1 becomes a file name.
		[InlineData(BoxArtConsole.Nes, "not-a-sha1", Name)]
		[InlineData(BoxArtConsole.Nes, "0123456789abcdef0123456789abcdef0123456", Name)]
		[InlineData(BoxArtConsole.Nes, "0123456789abcdef0123456789abcdef0123456g", Name)]
		//No table hit means no name to ask for (#1030): there is no fetch from a
		//cleaned file name.
		[InlineData(BoxArtConsole.Nes, Sha1, "")]
		public async Task A_key_that_cannot_name_a_game_is_answered_without_a_request(BoxArtConsole console, string sha1, string name)
		{
			FakeBoxArtSender sender = FakeBoxArtSender.Images(FakeImages.Png());

			Assert.Null(await Cache(sender).GetCover(console, sha1, name));
			Assert.Equal(0, sender.RequestCount);
		}

		[Fact]
		public async Task The_concurrency_ceiling_holds_however_many_tiles_ask_at_once()
		{
			int inFlight = 0;
			int peak = 0;
			int requests = 0;
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

			FakeBoxArtSender sender = new(async (_, _) => {
				int now = Interlocked.Increment(ref inFlight);
				peak = Math.Max(peak, now);
				Interlocked.Increment(ref requests);
				started.TrySetResult();
				await gate.Task;
				Interlocked.Decrement(ref inFlight);
				return BoxArtHttpResponse.Ok(FakeImages.Png());
			});

			BoxArtCache cache = Cache(sender, new BoxArtCacheOptions { MaxConcurrentRequests = 1 });
			Task<BoxArtCover?> first = cache.GetCover(BoxArtConsole.Nes, Sha1, Name);
			await started.Task;

			//A second tile asks while the first request is still open. The ceiling is
			//one, so it must be waiting, not sending.
			Task<BoxArtCover?> second = cache.GetCover(BoxArtConsole.Nes, OtherSha1, Name);
			await Task.Delay(200);
			Assert.Equal(1, Volatile.Read(ref requests));

			gate.SetResult();
			Assert.NotNull(await first);
			Assert.NotNull(await second);
			Assert.Equal(1, Volatile.Read(ref peak));
			Assert.Equal(2, sender.RequestCount);
		}
	}
}
