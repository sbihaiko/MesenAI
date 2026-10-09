using Mesen.Logic;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Services
{
	//ADR-0265 section 11: the shipped half of `BoxArtHttpSender`, and the only
	//place a box-art request is allowed to name `System.Net.Http`. UI/Logic is
	//host-free by construction (ADR-0138 §53) - a file there that so much as
	//mentions the namespace fails `scripts/verify-ui-logic-firewall.sh`, which
	//`make doc-checks` runs - so the cache takes this delegate and the adapter that
	//can actually open a socket lives here.
	//
	//The contract, from BoxArtTransport: answer a response for anything the server
	//answered and THROW for a transport failure (offline, DNS, TLS, a timeout, a
	//cancellation). The difference is the whole point - a 404 is the collection
	//saying "no art for this game", which is remembered as a miss for thirty days,
	//while a throw says something about the network and is remembered as nothing.
	public static class BoxArtFetcher
	{
		//One client for the process, like the pack downloader's: sockets are pooled
		//across opens instead of being torn down per request. No HttpClient.Timeout -
		//the cache's own deadline is the budget, and it covers the queue as well as
		//the read, so a second timer here would only be a second thing to reason
		//about.
		//
		//Redirects are not followed. The host is fixed and allow-listed for community
		//packs (ADR-0138 §41, the entry this ADR records a second purpose for), the
		//URL is built by BoxArtUrl rather than by anything a player types, and a hop
		//this adapter did not choose is not one it should take. A 3xx is answered as
		//the status it is, which the cache treats as a definitive "not this one".
		private static readonly HttpClient _client = new(new SocketsHttpHandler {
			AllowAutoRedirect = false,
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		}) { Timeout = Timeout.InfiniteTimeSpan };

		//`maxBytes` is enforced while the body is READ, not after it: the read stops
		//at `maxBytes + 1` bytes, so a body past the cap costs one byte of overshoot
		//instead of a whole response held in memory before anybody can measure it.
		//The cache rejects anything longer than the cap, which is a definitive answer
		//and costs a recorded miss rather than a retry on every visit.
		public static async Task<BoxArtHttpResponse> Send(Uri url, int maxBytes, CancellationToken cancellationToken)
		{
			if(url.Scheme != Uri.UriSchemeHttps) {
				//The scheme is HTTPS by construction (ADR-0265 section 4): the URL is
				//built by BoxArtUrl, and a value that reached here any other way is a
				//mistake this adapter refuses to turn into a request.
				throw new ArgumentException($"Box art is fetched over HTTPS only ({url.Scheme}).", nameof(url));
			}

			using HttpRequestMessage request = new(HttpMethod.Get, url);
			using HttpResponseMessage response = await _client
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			int status = (int)response.StatusCode;
			if(status != 200) {
				//A 404 is an answer like any other: no body is read, and the cache
				//moves on to the next collection.
				return new BoxArtHttpResponse(status, Array.Empty<byte>());
			}

			using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			using MemoryStream body = new();
			byte[] buffer = new byte[Math.Min(maxBytes + 1, 0x20000)];
			int remaining = maxBytes + 1;
			while(remaining > 0) {
				int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
				if(read <= 0) {
					break;
				}
				body.Write(buffer, 0, read);
				remaining -= read;
			}
			return new BoxArtHttpResponse(status, body.ToArray());
		}
	}
}
