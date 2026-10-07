using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;

namespace Mesen.Tests.BoxArt
{
	//The BoxArtHttpSender a test hands to BoxArtCache instead of a real transport:
	//it answers from a table and records every URL it was asked for, so a test can
	//assert on the requests that were NOT made (the switch-off case, the negative
	//cache) rather than only on what came back. No network, no message handler, no
	//real clock.
	internal sealed class FakeBoxArtSender
	{
		private readonly Func<Uri, CancellationToken, Task<BoxArtHttpResponse>> _answer;
		private readonly List<Uri> _requests = new();

		public FakeBoxArtSender(Func<Uri, CancellationToken, Task<BoxArtHttpResponse>> answer)
		{
			_answer = answer;
		}

		public int RequestCount {
			get {
				lock(_requests) {
					return _requests.Count;
				}
			}
		}

		public IReadOnlyList<Uri> Requests {
			get {
				lock(_requests) {
					return _requests.ToArray();
				}
			}
		}

		public Task<BoxArtHttpResponse> Send(Uri url, CancellationToken cancellationToken)
		{
			lock(_requests) {
				_requests.Add(url);
			}
			return _answer(url, cancellationToken);
		}

		//200 with `boxart` for Named_Boxarts; when `title` is given, 200 with it for
		//Named_Titles, otherwise 404 there.
		public static FakeBoxArtSender Images(byte[] boxart, byte[]? title = null) =>
			new((url, _) => {
				if(url.AbsolutePath.Contains("/Named_Boxarts/", StringComparison.Ordinal)) {
					return Task.FromResult(BoxArtHttpResponse.Ok(boxart));
				}
				if(title != null && url.AbsolutePath.Contains("/Named_Titles/", StringComparison.Ordinal)) {
					return Task.FromResult(BoxArtHttpResponse.Ok(title));
				}
				return Task.FromResult(BoxArtHttpResponse.NotFound());
			});

		//404 for Named_Boxarts and 200 with `title` for Named_Titles: the collection
		//has a title screen for this game and no box art.
		public static FakeBoxArtSender TitlesOnly(byte[] title) =>
			new((url, _) => Task.FromResult(
				url.AbsolutePath.Contains("/Named_Titles/", StringComparison.Ordinal)
					? BoxArtHttpResponse.Ok(title)
					: BoxArtHttpResponse.NotFound()
			));

		//The collection knows the system and not the game.
		public static FakeBoxArtSender Missing() => new((_, _) => Task.FromResult(BoxArtHttpResponse.NotFound()));

		//A transport failure, as the delegate's contract describes one: offline,
		//DNS, TLS. The cache must turn it into a null, never into a throw.
		public static FakeBoxArtSender Offline() => new((_, _) => throw new IOException("the collection is unreachable"));

		//A transport that answers nothing and ignores nothing but cancellation -
		//what a dropped connection looks like when only the timeout ends it.
		public static FakeBoxArtSender Hanging() => new(async (_, cancellationToken) => {
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return BoxArtHttpResponse.NotFound();
		});
	}

	//The smallest bodies that still say what they are: the signature is all
	//BoxArtImage reads, and a test that needed a real PNG would be testing the
	//decoder instead of the cache.
	internal static class FakeImages
	{
		private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF, 0xE0 };

		public static byte[] Png(int size = 32) => Padded(PngSignature, size);
		public static byte[] Jpeg(int size = 32) => Padded(JpegSignature, size);
		public static byte[] NotAnImage(int size = 32) => Padded(System.Text.Encoding.ASCII.GetBytes("<html>"), size);

		private static byte[] Padded(byte[] signature, int size)
		{
			byte[] body = new byte[Math.Max(size, signature.Length)];
			signature.CopyTo(body, 0);
			return body;
		}
	}
}
