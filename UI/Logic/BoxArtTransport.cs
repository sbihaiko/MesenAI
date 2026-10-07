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
	//a transport failure (offline, DNS, TLS, a timeout). The cache turns a throw into
	//a negative cache entry - a tile that quietly falls back to its generic cover is
	//the only acceptable outcome, so nothing may reach the sheet.
	public delegate Task<BoxArtHttpResponse> BoxArtHttpSender(Uri url, CancellationToken cancellationToken);
}
