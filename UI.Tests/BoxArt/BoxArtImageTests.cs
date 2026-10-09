using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//The gate a downloaded body passes before it is written to the player's disk.
	//The signatures are the independent source of truth here: a PNG and a JPEG
	//written out by hand, byte by byte, and a body that is neither.
	public class BoxArtImageTests
	{
		[Fact]
		public void The_png_signature_is_recognised()
		{
			Assert.Equal(BoxArtImageFormat.Png, BoxArtImage.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
			Assert.Equal("png", BoxArtImage.Extension(BoxArtImageFormat.Png));
		}

		[Fact]
		public void The_jpeg_signature_is_recognised()
		{
			Assert.Equal(BoxArtImageFormat.Jpeg, BoxArtImage.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
			Assert.Equal("jpg", BoxArtImage.Extension(BoxArtImageFormat.Jpeg));
		}

		[Theory]
		//A page of HTML with a 200 is what a captive portal or a CDN error page
		//answers with; it must never reach the disk as if it were a picture.
		[InlineData("<html><body>Not Found</body></html>")]
		[InlineData("")]
		[InlineData("GIF89a")]
		public void Anything_else_is_unknown(string body)
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);

			Assert.Equal(BoxArtImageFormat.Unknown, BoxArtImage.Detect(bytes));
			Assert.Equal("", BoxArtImage.Extension(BoxArtImageFormat.Unknown));
		}

		[Fact]
		public void A_body_shorter_than_the_signature_is_not_read_out_of_bounds()
		{
			Assert.Equal(BoxArtImageFormat.Png, BoxArtImage.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }));
			Assert.Equal(BoxArtImageFormat.Unknown, BoxArtImage.Detect(new byte[] { 0x89, 0x50 }));
			Assert.Equal(BoxArtImageFormat.Unknown, BoxArtImage.Detect(new byte[] { 0xFF, 0xD8 }));
		}
	}
}
