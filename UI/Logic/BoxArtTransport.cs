using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Logic
{
	//One answer from the transport: the status code and the body. A 404 is a
	//response like any other - it is how the collection says "this game has no
	//picture", which is the difference between falling back to Named_Titles and
	//giving up.
	public sealed record BoxArtHttpResponse(int StatusCode, byte[] Body)
	{
		public static BoxArtHttpResponse Ok(byte[] body) => new(200, body);
		public static BoxArtHttpResponse NotFound() => new(404, Array.Empty<byte>());
	}

	//The one thing BoxArtCache asks a network to do.
	//
	//It is a delegate rather than an HttpMessageHandler on purpose. UI/Logic never
	//names System.Net.Http or HttpClient: the three-layer rule (ADR-0138 §53) keeps
	//network orchestration in UI/Services/*.cs, and scripts/verify-ui-logic-firewall.sh
	//- which `make doc-checks` runs - fails the build when a file here so much as
	//mentions the namespace. So the shipped adapter that wraps an HttpClient (with
	//its redirect and stream-cap policy) belongs in UI/Services/*.cs, the only place
	//under UI/ the firewall allows one, and it implements this delegate; tests
	//implement it with a fake and touch no network at all.
	//
	//The contract: return a response for anything the server answered, and THROW for
	//a transport failure (offline, DNS, TLS, a timeout).
	//
	//`maxBytes` is the ceiling the adapter enforces while it reads, not a number for
	//the caller to check afterwards: it stops at `maxBytes + 1` bytes and returns
	//what it has, so a body past the cap costs one byte of overshoot instead of a
	//whole response held in memory before anybody can measure it. A body longer than
	//`maxBytes` is past the cap, and BoxArtCache rejects it - which is a definitive
	//answer and costs a recorded miss, whereas a throw is not. Cancelling the token
	//must end the read as a throw like any other transport failure: the cache
	//answers null either way.
	public delegate Task<BoxArtHttpResponse> BoxArtHttpSender(Uri url, int maxBytes, CancellationToken cancellationToken);
}
