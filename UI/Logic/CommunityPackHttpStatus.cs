namespace Mesen.Logic
{
	//Host-free HTTP status helpers for CommunityPackDownloader (ADR-0138
	//§50). The downloader follows a hop only when it is a real redirect
	//(301/302/303/307/308). HTTP 304 Not Modified is not a redirect - it
	//is the catalog cache-reuse status CommunityCatalogCacheDecision
	//already models - so treating the whole 3xx class as "follow Location"
	//makes a 304 look like "redirect with no Location" and collapses it
	//into a network failure. BCL only; dual-compiled into UI.Tests.
	public static class CommunityPackHttpStatus
	{
		public static bool IsFollowableRedirect(int statusCode) =>
			statusCode is 301 or 302 or 303 or 307 or 308;

		//A refusal, not a network fault: 403 is GitHub/raw and the CDNs'
		//rate-limit, abuse and permission answer, 429 is the explicit one. The
		//client must keep them apart from the "no answer at all" bucket - the
		//download primitive returns a Response for every status it reaches, and
		//only a null Response means the allow-list, the redirect cap, the size
		//cap, the timeout or an exception.
		public static bool IsRateLimited(int statusCode) =>
			statusCode is 403 or 429;

		//One failed download, as mesen.log names it. The routine auto-install
		//path is silent by contract (ADR-0138 SS42), so this line is the whole
		//diagnosis a user or maintainer gets; a status code must therefore be
		//reported as itself. Status 0 is "there was no HTTP answer".
		public static string Describe(int statusCode) => statusCode switch {
			0 => "no HTTP answer (host not allow-listed, redirect refused, size cap, timeout, or network error)",
			403 => "HTTP 403 (the host refused the request: rate limited, or the URL is not permitted)",
			429 => "HTTP 429 (the host refused the request: rate limiting this client)",
			404 => "HTTP 404 (the file is not there any more)",
			410 => "HTTP 410 (the file is gone)",
			>= 500 and <= 599 => "HTTP " + statusCode + " (the host answered with a server error)",
			_ => "HTTP " + statusCode
		};
	}
}
