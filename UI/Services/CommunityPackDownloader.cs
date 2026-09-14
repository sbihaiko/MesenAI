using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Services
{
	//Client-side download trust contract (ADR-0138 §50), the one HTTP GET primitive every
	//community-pack request in UI/Services goes through. Mirrors scripts/fetch_pack.py's
	//open_validated on the controls a client can apply: redirects are never followed
	//automatically - every hop is re-checked against the host allow-list (MatchHost, https
	//only) with the same 5-hop cap - and the body is capped in bytes before it is buffered,
	//so a hostile redirect from an allow-listed URL can neither leave the allow-list nor
	//exhaust memory. DNS/private-range checks stay CI-only: the allow-listed hosts are
	//public CDNs, and a client cannot pin DNS the way the runner does.
	//
	//A `kind: "google-drive"` entry (MEI-v1 §2.4) does NOT go through the plain GET loop:
	//the share URL is mapped to the two-step dance (uc?export=download, then the
	//drive.usercontent.google.com confirm hop when the first response is the virus-scan
	//HTML page) - a plain GET on a large Drive file returns that HTML, so the sha256
	//verification would never match. Both hops are still allow-listed and capped.
	//"mediafire", "dropbox" and "mega" are the same idea (ADR-0187): each is the
	//shape its host needs before the bytes are the pack's bytes. Every branch is a
	//mirror of the same-named function in scripts/fetch_pack.py - the CI validator
	//and this client must agree on what a given URL downloads to, or a pack passes
	//validation and then fails to auto-install (ADR-0146).
	public static class CommunityPackDownloader
	{
		public const int MaxRedirects = 5;
		//Same ceiling as the CI validator (community-pack-validate.yml, 300MB).
		public const long MaxArtifactBytes = 300L * 1024 * 1024;
		public const long MaxCatalogBytes = 16L * 1024 * 1024;

		//Default wall-clock budget for one GetAsync call (all hops + body read);
		//the catalog fetch passes a much shorter one.
		public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

		//One process-wide client: sockets are pooled across ROM loads instead of
		//being torn down per request. Redirects are never followed automatically
		//(each hop is re-checked against the allow-list in FetchAsync). No
		//HttpClient.Timeout - the budget is a CancellationToken applied to
		//SendAsync and every body read, so a stalled transfer is bounded too.
		private static readonly HttpClient _client = new(new SocketsHttpHandler {
			AllowAutoRedirect = false,
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		}) { Timeout = Timeout.InfiniteTimeSpan };

		public sealed record Response(int StatusCode, string? ETag, byte[]? Body);

		//Returns null when the entry is outside the allow-list, the redirect chain is too
		//long, the body exceeds maxBytes, the timeout elapses, or any network/IO error
		//occurs - never throws. A google-drive-kind URL is downloaded via the two-step
		//dance; everything else via the redirect-checked GET loop.
		public static async Task<Response?> GetAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag = null, TimeSpan? timeout = null)
		{
			using CancellationTokenSource cts = new(timeout ?? DefaultTimeout);
			CancellationToken ct = cts.Token;
			CommunityPackHostEntry? matched = CommunityPackHostAllowlist.MatchHost(url, allowedHosts);
			if(matched == null) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] REJECTED (host not allow-listed): " + CommunityPackMega.Scrub(url));
				return null;
			}
			if(string.Equals(matched.Kind, "google-drive", StringComparison.OrdinalIgnoreCase)) {
				return await GetGoogleDriveAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			}
			if(string.Equals(matched.Kind, "mediafire", StringComparison.OrdinalIgnoreCase)) {
				return await GetMediaFireAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			}
			if(string.Equals(matched.Kind, "dropbox", StringComparison.OrdinalIgnoreCase)) {
				return await GetDropboxAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			}
			if(string.Equals(matched.Kind, "mega", StringComparison.OrdinalIgnoreCase)) {
				return await GetMegaAsync(url, allowedHosts, maxBytes, ct);
			}
			return await GetDirectAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
		}

		private static async Task<Response?> GetDirectAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag, CancellationToken ct)
		{
			FetchResult? result = await FetchAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			return result == null ? null : new Response(result.StatusCode, result.ETag, result.Body);
		}

		//Mirror of scripts/fetch_pack.py::fetch_google_drive: try the canonical
		//uc?export=download first; when the response is the large-file virus-scan HTML
		//page, one more hop to drive.usercontent.google.com with a confirm token fetches
		//the real bytes. Small files come straight back from the first request.
		private static async Task<Response?> GetGoogleDriveAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag, CancellationToken ct)
		{
			string? fileId = CommunityPackDrive.ExtractFileId(url);
			if(fileId == null) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] google-drive URL has no file id: " + url);
				return null;
			}
			string firstUrl = "https://drive.google.com/uc?export=download&id=" + fileId;
			FetchResult? first = await FetchAsync(firstUrl, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			if(first == null) {
				return null;
			}
			string contentType = first.ContentType ?? "";
			if(contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)) {
				string secondUrl = "https://drive.usercontent.google.com/download?id=" + fileId + "&export=download&confirm=t";
				FetchResult? second = await FetchAsync(secondUrl, allowedHosts, maxBytes, ifNoneMatchETag, ct);
				if(second == null) {
					return null;
				}
				return new Response(second.StatusCode, second.ETag, second.Body);
			}
			return new Response(first.StatusCode, first.ETag, first.Body);
		}

		//Mirror of scripts/fetch_pack.py::fetch_mediafire: the share page is HTML;
		//the zip is the downloadN.mediafire.com href. A CDN URL pasted directly
		//is kind "direct" (host_ends_with) and never lands here.
		private static async Task<Response?> GetMediaFireAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag, CancellationToken ct)
		{
			FetchResult? first = await FetchAsync(url, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			if(first == null) {
				return null;
			}
			string contentType = first.ContentType ?? "";
			if(!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)) {
				return new Response(first.StatusCode, first.ETag, first.Body);
			}
			string html = first.Body == null ? "" : System.Text.Encoding.UTF8.GetString(first.Body);
			string? href = CommunityPackMediaFire.ExtractDownloadUrl(html);
			if(href == null) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] mediafire share page has no downloadN.mediafire.com link");
				return null;
			}
			FetchResult? second = await FetchAsync(href, allowedHosts, maxBytes, ifNoneMatchETag, ct);
			return second == null ? null : new Response(second.StatusCode, second.ETag, second.Body);
		}

		//Mirror of scripts/fetch_pack.py::fetch_dropbox: a share link renders
		//an HTML preview unless dl=1 is set, after which the usual redirect
		//chain ends on *.dl.dropboxusercontent.com. HTML on the final hop is a
		//login wall or a dead link, not a pack - returning it would be
		//reported downstream as a corrupt zip.
		private static async Task<Response?> GetDropboxAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag, CancellationToken ct)
		{
			FetchResult? result = await FetchAsync(CommunityPackDropbox.ForceDownload(url), allowedHosts, maxBytes, ifNoneMatchETag, ct);
			if(result == null) {
				return null;
			}
			if((result.ContentType ?? "").Contains("text/html", StringComparison.OrdinalIgnoreCase)) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] dropbox link served HTML, not a file (deleted or access-restricted?)");
				return null;
			}
			return new Response(result.StatusCode, result.ETag, result.Body);
		}

		//Mirror of scripts/fetch_pack.py::fetch_mega (ADR-0187 §3): the key is
		//split off the URL fragment locally and never sent; the API is asked
		//(POST, ssl=1 so the answer is https) for a short-lived download URL;
		//the body arrives AES-CTR encrypted and is decrypted here. No ETag -
		//the download URL is regenerated per request, so conditional requests
		//would be meaningless.
		private static async Task<Response?> GetMegaAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, CancellationToken ct)
		{
			CommunityPackMega.FileRef? file = CommunityPackMega.Parse(url);
			if(file == null) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] not a usable MEGA file link: " + CommunityPackMega.Scrub(url));
				return null;
			}
			string? downloadUrl = await RequestMegaDownloadUrlAsync(file.Handle, allowedHosts, ct);
			if(downloadUrl == null) {
				return null;
			}
			FetchResult? result = await FetchAsync(downloadUrl, allowedHosts, maxBytes, null, ct);
			if(result == null || result.Body == null) {
				return null;
			}
			CommunityPackMega.DecryptCtr(file.AesKey, file.Nonce, result.Body);
			return new Response(result.StatusCode, null, result.Body);
		}

		private static async Task<string?> RequestMegaDownloadUrlAsync(string handle, IReadOnlyList<CommunityPackHostEntry> allowedHosts, CancellationToken ct)
		{
			const string api = "https://g.api.mega.co.nz/cs?id=0";
			if(CommunityPackHostAllowlist.MatchHost(api, allowedHosts) == null) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] MEGA API host not allow-listed");
				return null;
			}
			try {
				using HttpRequestMessage request = new(HttpMethod.Post, api) {
					Content = new StringContent(CommunityPackMega.RequestBody(handle), System.Text.Encoding.UTF8, "application/json")
				};
				using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
				if((int)response.StatusCode != (int)HttpStatusCode.OK) {
					EmuApi.WriteLogEntry("[CommunityPackDownloader] MEGA API returned " + (int)response.StatusCode);
					return null;
				}
				byte[]? body = await ReadCappedAsync(response, 64 * 1024, ct);
				if(body == null) {
					return null;
				}
				string? g = CommunityPackMega.ExtractDownloadUrl(System.Text.Encoding.UTF8.GetString(body));
				if(g == null) {
					EmuApi.WriteLogEntry("[CommunityPackDownloader] MEGA API response carried no download URL");
				}
				return g;
			} catch(OperationCanceledException) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] MEGA API timed out");
				return null;
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] MEGA API threw: " + CommunityPackMega.Scrub(ex.ToString()));
				return null;
			}
		}

		private sealed record FetchResult(int StatusCode, string? ContentType, string? ETag, byte[]? Body);

		private static async Task<FetchResult?> FetchAsync(string url, IReadOnlyList<CommunityPackHostEntry> allowedHosts, long maxBytes, string? ifNoneMatchETag, CancellationToken ct)
		{
			try {
				string current = url;
				for(int hop = 0; hop <= MaxRedirects; hop++) {
					CommunityPackHostEntry? matched = CommunityPackHostAllowlist.MatchHost(current, allowedHosts);
					if(matched == null) {
						EmuApi.WriteLogEntry("[CommunityPackDownloader] hop " + hop + " REJECTED (host not allow-listed): " + current);
						return null;
					}
					using HttpRequestMessage request = new(HttpMethod.Get, current);
					if(ifNoneMatchETag != null) {
						request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatchETag);
					}
					using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
					int status = (int)response.StatusCode;
					EmuApi.WriteLogEntry("[CommunityPackDownloader] hop " + hop + " GET " + current + " -> " + status);
					if(CommunityPackHttpStatus.IsFollowableRedirect(status)) {
						Uri? location = response.Headers.Location;
						if(location == null) {
							EmuApi.WriteLogEntry("[CommunityPackDownloader] redirect with no Location header");
							return null;
						}
						current = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(current), location).ToString();
						EmuApi.WriteLogEntry("[CommunityPackDownloader] redirect -> " + current);
						continue;
					}
					if(response.Content.Headers.ContentLength is long declared && declared > maxBytes) {
						EmuApi.WriteLogEntry("[CommunityPackDownloader] declared Content-Length " + declared + " exceeds cap " + maxBytes);
						return null;
					}
					byte[]? body = status == (int)HttpStatusCode.OK ? await ReadCappedAsync(response, maxBytes, ct) : null;
					if(status == (int)HttpStatusCode.OK && body == null) {
						EmuApi.WriteLogEntry("[CommunityPackDownloader] body read failed or exceeded cap mid-transfer");
						return null;
					}
					EmuApi.WriteLogEntry("[CommunityPackDownloader] final response status=" + status + " bodyBytes=" + (body?.Length.ToString() ?? "null"));
					return new FetchResult(status, response.Content.Headers.ContentType?.MediaType, response.Headers.ETag?.Tag, body);
				}
				EmuApi.WriteLogEntry("[CommunityPackDownloader] too many redirects (>" + MaxRedirects + ")");
				return null; //too many redirects
			} catch(OperationCanceledException) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] timed out: " + url);
				return null;
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[CommunityPackDownloader] GetAsync threw: " + ex);
				return null;
			}
		}

		private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
		{
			using Stream stream = await response.Content.ReadAsStreamAsync(ct);
			using MemoryStream buffer = new();
			byte[] chunk = new byte[81920];
			int read;
			while((read = await stream.ReadAsync(chunk, ct)) > 0) {
				if(buffer.Length + read > maxBytes) {
					return null;
				}
				buffer.Write(chunk, 0, read);
			}
			return buffer.ToArray();
		}
	}
}
