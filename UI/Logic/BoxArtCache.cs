using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Logic
{
	//Downloaded box art, cached on the player's own machine (#1039). One call per
	//visible tile; what a tile does with the answer is the sheet's business, and
	//this class never draws anything.
	//
	//Host-free by construction (ADR-0138 §53): no Avalonia, no EmuApi, no
	//HttpClient. The transport arrives as the BoxArtHttpSender delegate and the
	//cache directory as a constructor argument, so UI.Tests drives the whole thing
	//with a fake and a temp folder, and the shipped HttpClient adapter lives in
	//UI/Services/*.cs.
	//
	//One call, in order:
	//
	//  1. **The key must exist.** An unknown console, a sha1 that is not the
	//     table's own 40 hex characters, or an empty No-Intro name is answered
	//     null with no request: the collection is keyed by the dump (ADR-0003),
	//     there is no name to ask for without a table hit, and the sha1 is a file
	//     name here, so a value that is not hex can only be a mistake.
	//  2. **A cover already on disk is returned.** This runs before the switch on
	//     purpose: turning *Download box art* off stops the app talking to a
	//     server, and does not throw away art the player already has. Reading a
	//     disk is not a request.
	//  3. **With the switch off, nothing else happens.** No request, ever.
	//  4. **A fresh miss is answered null with no request.** A game the collection
	//     does not have is not re-asked on every visit; the record expires
	//     (MissExpiry) so a game that is added later is picked up.
	//  5. **Otherwise the collection is asked**: Named_Boxarts first, then
	//     Named_Titles, HTTPS only, at most MaxImageBytes, and the body is cached
	//     only after its own first bytes say it is a PNG or a JPEG.
	//  6. **Every failure becomes a recorded miss**: a 404, a transport exception
	//     (offline, DNS, TLS), a timeout, an oversized body, a body that is not an
	//     image, a cache directory that cannot be written. A tile whose art is
	//     unavailable is a tile that falls back to its generic cover, and no
	//     exception from here ever reaches the sheet.
	public sealed class BoxArtCache
	{
		private readonly BoxArtHttpSender _sender;
		private readonly string _cacheDirectory;
		private readonly BoxArtCacheOptions _options;
		private readonly SemaphoreSlim _inFlight;

		public BoxArtCache(BoxArtHttpSender sender, string cacheDirectory, BoxArtCacheOptions? options = null)
		{
			_sender = sender ?? throw new ArgumentNullException(nameof(sender));
			_cacheDirectory = string.IsNullOrEmpty(cacheDirectory)
				? throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory))
				: cacheDirectory;
			_options = options ?? new BoxArtCacheOptions();
			_inFlight = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRequests));
		}

		//The cache directory this instance writes to - the app-support folder the
		//caller resolved, never a repo-relative path.
		public string CacheDirectory => _cacheDirectory;

		//The cover for one game, or null when there is none to show. Never throws
		//for anything a network or a disk can do.
		public async Task<BoxArtCover?> GetCover(BoxArtConsole console, string sha1, string noIntroName, CancellationToken cancellationToken = default)
		{
			string consoleTag = BoxArtSystems.CacheTag(console);
			if(consoleTag.Length == 0 || !IsSha1(sha1) || string.IsNullOrEmpty(noIntroName)) {
				return null;
			}

			BoxArtCover? cached = BoxArtCacheStore.Find(_cacheDirectory, consoleTag, sha1);
			if(cached != null) {
				return cached;
			}

			if(!_options.DownloadEnabled) {
				return null;
			}

			string folder = BoxArtCacheStore.ConsoleFolder(_cacheDirectory, consoleTag);
			if(BoxArtCacheStore.IsMissFresh(folder, sha1, _options.MissExpiry, _options.Clock())) {
				return null;
			}

			BoxArtCover? cover = await Download(console, sha1, noIntroName, folder, cancellationToken).ConfigureAwait(false);
			if(cover != null) {
				BoxArtCacheStore.ClearMiss(folder, sha1);
				return cover;
			}

			//A call the caller itself cancelled (the sheet closed, the tile
			//scrolled away) says nothing about the game, so it is not recorded:
			//thirty days of "no art" written because a player moved the focus
			//would be a bug they cannot see and cannot clear.
			if(!cancellationToken.IsCancellationRequested) {
				BoxArtCacheStore.WriteMiss(folder, sha1, _options.Clock());
			}
			return null;
		}

		private async Task<BoxArtCover?> Download(BoxArtConsole console, string sha1, string noIntroName, string folder, CancellationToken cancellationToken)
		{
			(BoxArtCoverKind Kind, Uri? Url)[] attempts = {
				(BoxArtCoverKind.Boxart, BoxArtUrl.Boxarts(console, noIntroName)),
				(BoxArtCoverKind.Title, BoxArtUrl.Titles(console, noIntroName))
			};

			foreach((BoxArtCoverKind kind, Uri? url) in attempts) {
				if(url == null) {
					continue;
				}

				byte[]? body = await TryFetch(url, cancellationToken).ConfigureAwait(false);
				if(body == null) {
					continue;
				}

				BoxArtImageFormat format = BoxArtImage.Detect(body);
				if(format == BoxArtImageFormat.Unknown) {
					continue;
				}

				string? path = BoxArtCacheStore.WriteImage(folder, sha1, kind, format, body);
				if(path != null) {
					return new BoxArtCover(path, kind);
				}
			}
			return null;
		}

		//One request under the concurrency ceiling and the per-request timeout. Null
		//means "not this one" - a non-200 answer, an oversized body, or a transport
		//that failed - and the caller moves on to the next collection.
		private async Task<byte[]?> TryFetch(Uri url, CancellationToken cancellationToken)
		{
			await _inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
			try {
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(_options.RequestTimeout);

				BoxArtHttpResponse response = await _sender(url, timeout.Token).ConfigureAwait(false);
				if(response.StatusCode != 200 || response.Body.Length > _options.MaxImageBytes) {
					return null;
				}
				return response.Body;
			} catch(Exception) {
				//Offline, DNS, TLS, a timeout, a proxy that answered with nonsense:
				//from the sheet's point of view these are one thing - this picture
				//is not available now - and the caller records the miss.
				return null;
			} finally {
				_inFlight.Release();
			}
		}

		//The table's own key shape (40 hex characters, the ROM payload's SHA-1 per
		//ADR-0003). Checked rather than trusted because the value becomes a file
		//name.
		private static bool IsSha1(string? value)
		{
			if(value == null || value.Length != 40) {
				return false;
			}
			foreach(char c in value) {
				bool hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
				if(!hex) {
					return false;
				}
			}
			return true;
		}
	}
}
