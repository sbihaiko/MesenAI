using System;

namespace Mesen.Logic
{
	//What the first bytes of a downloaded body say it is.
	public enum BoxArtImageFormat
	{
		Unknown = 0,
		Png,
		Jpeg
	}

	//The gate a downloaded body passes before anything reaches the player's disk:
	//the collection publishes PNGs, a CDN or a captive portal is free to answer a
	//200 with a page of HTML, and a file the decoder cannot open is worse than no
	//file at all - the tile would be blank with no fallback. The signature is what
	//this reads, not the extension and not the Content-Type header: both come from
	//the same server that would have to be trusted.
	public static class BoxArtImage
	{
		private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

		public static BoxArtImageFormat Detect(ReadOnlySpan<byte> body)
		{
			if(StartsWith(body, PngSignature)) {
				return BoxArtImageFormat.Png;
			}
			//JPEG has no magic number of its own; a start-of-image marker followed
			//by any segment marker is the shortest signature that is not a guess.
			if(body.Length >= 3 && body[0] == 0xFF && body[1] == 0xD8 && body[2] == 0xFF) {
				return BoxArtImageFormat.Jpeg;
			}
			return BoxArtImageFormat.Unknown;
		}

		//The extension the cached file is written under. Empty for Unknown: a body
		//this class cannot name is never cached, so nothing writes an extensionless
		//file.
		public static string Extension(BoxArtImageFormat format) => format switch {
			BoxArtImageFormat.Png => "png",
			BoxArtImageFormat.Jpeg => "jpg",
			_ => ""
		};

		private static bool StartsWith(ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature)
		{
			return body.Length >= signature.Length && body.Slice(0, signature.Length).SequenceEqual(signature);
		}
	}
}
