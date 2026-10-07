using System;
using System.Collections.Concurrent;
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
	//  6. **A failure is answered with null, and only a definitive one is
	//     remembered.** A 404, an oversized body, a body that is not an image, a
	//     cache directory that cannot be written: those say the collection has no
	//     picture for this game, so the miss is recorded. A transport failure
	//     (offline, DNS, TLS) or a timeout says something about the network and
	//     nothing about the game, so it records nothing at all - one offline
	//     session must not blank every cover for thirty days. Either way no
	//     exception from here ever reaches the sheet.
	public sealed class BoxArtCache
	{
		private readonly BoxArtHttpSender _sender;
		private readonly string _cacheDirectory;
		private readonly BoxArtCacheOptions _options;
		private readonly SemaphoreSlim _inFlight;
		private readonly ConcurrentDictionary<string, Task<BoxArtAttempt>> _downloads = new();

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

			//One spelling of the key, whatever case the caller hands in. Two spellings
			//of one SHA-1 are one ROM, so they must share one cache entry and one miss;
			//left as given they would write two files for one game and pay for the
			//download twice. Normalised here, after the check, because the value
			//becomes a file name.
			sha1 = sha1.ToLowerInvariant();

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

			BoxArtAttempt attempt = await DownloadOnce(consoleTag, sha1, console, noIntroName, folder, cancellationToken).ConfigureAwait(false);
			if(attempt.Cover != null) {
				BoxArtCacheStore.ClearMiss(folder, sha1);
				return attempt.Cover;
			}

			//Only an answer the collection actually gave is worth remembering; a
			//network that was down when the tile was drawn is not evidence that the
			//game has no cover. A call the caller itself cancelled (the sheet closed,
			//the tile scrolled away) says nothing about the game either, so it is not
			//recorded: thirty days of "no art" written because a player moved the
			//focus would be a bug they cannot see and cannot clear.
			if(attempt.Definitive && !cancellationToken.IsCancellationRequested) {
				BoxArtCacheStore.WriteMiss(folder, sha1, _options.Clock());
			}
			return null;
		}

		//What one call to the collection decided. `Definitive` is the whole point of
		//the pair: a cover makes it moot, and without one it says whether the
		//collection actually answered (a 404, an oversized body, a body that is not
		//an image) or the transport failed - which is the difference between
		//recording a miss and not.
		private readonly record struct BoxArtAttempt(BoxArtCover? Cover, bool Definitive);

		//One download per game, however many tiles ask for it at once. Two visible
		//tiles of the same ROM are two calls that overlap, and on their own they would
		//be two requests finishing together and then fighting over the one name a
		//cover is published from - the tile that lost that fight reads its own failure
		//as "the collection has no art" and records a miss for a cover that is on disk.
		//The second tile joins the first tile's request instead, so both answer the
		//same thing and the collection is asked once.
		private async Task<BoxArtAttempt> DownloadOnce(string consoleTag, string sha1, BoxArtConsole console, string noIntroName, string folder, CancellationToken cancellationToken)
		{
			string key = $"{consoleTag}/{sha1}";
			while(true) {
				if(_downloads.TryGetValue(key, out Task<BoxArtAttempt>? running)) {
					//The join carries the same deadline as the request it joined: a tile
					//that arrives late still falls back to its generic cover within
					//RequestTimeout (ADR-0265 section 9) rather than waiting out somebody
					//else's download, and a caller that walked away while waiting is a
					//null like every other cancellation.
					try {
						return await running.WaitAsync(_options.RequestTimeout, cancellationToken).ConfigureAwait(false);
					} catch(Exception) {
						return new BoxArtAttempt(null, false);
					}
				}

				TaskCompletionSource<BoxArtAttempt> mine = new(TaskCreationOptions.RunContinuationsAsynchronously);
				if(!_downloads.TryAdd(key, mine.Task)) {
					//Another tile added the entry between the read and the write: take
					//the turn again and join it.
					continue;
				}

				try {
					BoxArtAttempt attempt = await Download(console, sha1, noIntroName, folder, cancellationToken).ConfigureAwait(false);
					mine.SetResult(attempt);
					return attempt;
				} catch {
					//Download answers for everything a network and a disk can do, so this
					//cannot be reached from those - but a joined tile must never be left
					//waiting on a task that never completes.
					mine.SetResult(new BoxArtAttempt(null, false));
					throw;
				} finally {
					_downloads.TryRemove(key, out _);
				}
			}
		}

		private async Task<BoxArtAttempt> Download(BoxArtConsole console, string sha1, string noIntroName, string folder, CancellationToken cancellationToken)
		{
			(BoxArtCoverKind Kind, Uri? Url)[] attempts = {
				(BoxArtCoverKind.Boxart, BoxArtUrl.Boxarts(console, noIntroName)),
				(BoxArtCoverKind.Title, BoxArtUrl.Titles(console, noIntroName))
			};

			//A miss may only be recorded when the collection answered. One attempt
			//that failed for transport reasons, with no cover from the rest, is not
			//evidence that the game has no art - it is evidence about the network.
			bool answered = false;
			bool transportFailed = false;

			foreach((BoxArtCoverKind kind, Uri? url) in attempts) {
				if(url == null) {
					continue;
				}

				(byte[]? body, bool thisAnswered) = await TryFetch(url, cancellationToken).ConfigureAwait(false);
				if(!thisAnswered) {
					transportFailed = true;
					continue;
				}
				answered = true;

				if(body == null) {
					continue;
				}

				BoxArtImageFormat format = BoxArtImage.Detect(body);
				if(format == BoxArtImageFormat.Unknown) {
					continue;
				}

				string? path = BoxArtCacheStore.WriteImage(folder, sha1, kind, format, body);
				if(path != null) {
					return new BoxArtAttempt(new BoxArtCover(path, kind), true);
				}
			}
			return new BoxArtAttempt(null, answered && !transportFailed);
		}

		//One request under the concurrency ceiling and the per-request timeout.
		//`Answered` is false only when the transport itself failed - offline, DNS,
		//TLS, a timeout, a proxy that answered with nonsense - and that is the one
		//outcome the caller may not turn into a recorded miss. A null body with
		//`Answered` true is a definitive "not this one": a non-200 answer or a body
		//past the cap, and the caller moves on to the next collection.
		private async Task<(byte[]? Body, bool Answered)> TryFetch(Uri url, CancellationToken cancellationToken)
		{
			//The queue is inside the try, and the release is conditional on having
			//taken a permit: a tile cancelled while it waits for the ceiling must
			//leave with a null like every other failure (ADR-0265 section 9), and an
			//OperationCanceledException thrown by the wait itself would reach the
			//sheet. Waiting for a turn is part of the call that never throws.
			bool acquired = false;
			try {
				//The deadline is created before the wait, not after it, because the
				//wait is part of the call: a tile queued behind the ceiling is holding
				//nothing but a place in line, and it must fall back to its generic
				//cover within RequestTimeout rather than inherit whichever request
				//ahead of it happens to end. The timer starts with the queueing.
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(_options.RequestTimeout);

				await _inFlight.WaitAsync(timeout.Token).ConfigureAwait(false);
				acquired = true;

				//The cap travels with the request: the adapter stops reading at it, and
				//what comes back longer than the cap is its own overflow, which the
				//check below turns into a definitive "no".
				//
				//The deadline is enforced here as well, and not only through the token
				//the sender was handed: honouring that token is the adapter's side of
				//the delegate's contract, and a bug on that side would otherwise become
				//a library that never draws. The tile gives up on the task either way.
				BoxArtHttpResponse response = await _sender(url, _options.MaxImageBytes, timeout.Token)
					.WaitAsync(_options.RequestTimeout, cancellationToken)
					.ConfigureAwait(false);
				if(response.StatusCode != 200 || response.Body.Length > _options.MaxImageBytes) {
					return (null, true);
				}
				return (response.Body, true);
			} catch(Exception) {
				//Offline, DNS, TLS, a timeout, a proxy that answered with nonsense, a
				//cancellation while queued: from the sheet's point of view these are
				//one thing - this picture is not available now - and the caller
				//answers null while recording nothing.
				return (null, false);
			} finally {
				if(acquired) {
					_inFlight.Release();
				}
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
