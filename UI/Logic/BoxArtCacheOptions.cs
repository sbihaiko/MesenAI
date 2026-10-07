using System;

namespace Mesen.Logic
{
	//Everything BoxArtCache's behaviour is decided by, in one place, so that the
	//numbers are named rather than buried in an implementation and a test can
	//shrink every one of them.
	public sealed record BoxArtCacheOptions
	{
		//The player's master switch ("Download box art", Settings › System). False
		//means no request is made at all - not a smaller, quieter request, none -
		//so a player who turns it off stops telling a server which games they own.
		//A cover already in the cache is still served: it is their own file, and
		//reading a disk is not a request.
		public bool DownloadEnabled { get; init; } = true;

		//The most bytes a body may be and still be cached. The collection's box
		//art is a few hundred KB at most; anything past this is not the picture
		//that was asked for.
		public int MaxImageBytes { get; init; } = 4 * 1024 * 1024;

		//How long one request (one collection) may take before the tile gives up
		//on it. Bounded on purpose: a library opens and plays exactly as fast
		//offline as online, so no tile may sit on the network.
		public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

		//How long a miss is remembered. A game the collection does not have is not
		//re-asked on every visit, and a game added to the collection later is
		//picked up once this expires.
		public TimeSpan MissExpiry { get; init; } = TimeSpan.FromDays(30);

		//How many requests may be in flight at once, whatever the grid decides to
		//ask for. A library scrolled quickly asks for dozens of tiles; this is the
		//ceiling on how many of them reach the network together.
		public int MaxConcurrentRequests { get; init; } = 4;

		//The clock the miss expiry is read against. A test moves it; nothing in the
		//shipped app needs it to be anything but the wall clock.
		public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
	}
}
