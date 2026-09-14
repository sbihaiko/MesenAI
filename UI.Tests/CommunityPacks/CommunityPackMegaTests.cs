using Mesen.Logic;
using System;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//Coverage for CommunityPackMega (allow-list kind "mega", ADR-0187 §3).
	//The key derivation is pinned against the same vector as
	//scripts/test_fetch_pack.py's check_mega_url_parsing, and the AES-CTR
	//transform is checked against Python's output for that vector: a bug in
	//either produces plausible garbage rather than an error, so the zip just
	//fails to open and reads like a corrupt upload.
	public class CommunityPackMegaTests
	{
		//32 bytes 0x00..0x1F, base64url, no padding.
		private const string KeyFragment = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

		[Fact]
		public void Parse_DerivesHandleKeyAndNonce()
		{
			CommunityPackMega.FileRef? file = CommunityPackMega.Parse("https://mega.nz/file/AbCd1234#" + KeyFragment);

			Assert.NotNull(file);
			Assert.Equal("AbCd1234", file!.Handle);
			//words[i] ^ words[i+4] for i in 0..3, big-endian.
			Assert.Equal("10101010101010101010101010101010", Convert.ToHexString(file.AesKey).ToLowerInvariant());
			//words 4-5 then eight zero bytes.
			Assert.Equal("10111213141516170000000000000000", Convert.ToHexString(file.Nonce).ToLowerInvariant());
		}

		//A folder link's fragment is "<key>!<handle>!<key>"; only the leading
		//key belongs to this file.
		[Fact]
		public void Parse_FolderStyleFragment_StopsAtTheBang()
		{
			CommunityPackMega.FileRef? plain = CommunityPackMega.Parse("https://mega.nz/file/AbCd1234#" + KeyFragment);
			CommunityPackMega.FileRef? folder = CommunityPackMega.Parse("https://mega.nz/file/AbCd1234#" + KeyFragment + "!x!y");

			Assert.NotNull(folder);
			Assert.Equal(plain!.AesKey, folder!.AesKey);
		}

		[Theory]
		[InlineData("https://mega.nz/file/AbCd1234")]           //no fragment
		[InlineData("https://mega.nz/file/AbCd1234#short")]     //key is not 32 bytes
		[InlineData("https://mega.nz/folder/AbCd1234#" + KeyFragment)] //no /file/ handle
		[InlineData("not a url")]
		public void Parse_MalformedLink_ReturnsNull(string url)
		{
			Assert.Null(CommunityPackMega.Parse(url));
		}

		[Fact]
		public void RequestBody_AsksForAnHttpsUrl()
		{
			//Without ssl=1 the API answers with an http:// download URL, which
			//the allow-list refuses - the flag is what keeps that rejection
			//from being the normal case.
			Assert.Equal("[{\"a\":\"g\",\"g\":1,\"ssl\":1,\"p\":\"AbCd1234\"}]", CommunityPackMega.RequestBody("AbCd1234"));
		}

		[Fact]
		public void ExtractDownloadUrl_ReadsTheGField()
		{
			const string json = "[{\"s\":823693,\"at\":\"x\",\"g\":\"https://gfs302n298.userstorage.mega.co.nz/dl/abc\"}]";

			Assert.Equal("https://gfs302n298.userstorage.mega.co.nz/dl/abc", CommunityPackMega.ExtractDownloadUrl(json));
		}

		[Theory]
		[InlineData("")]
		[InlineData("[-9]")]
		[InlineData("[{\"s\":1}]")]
		public void ExtractDownloadUrl_NoDownloadUrl_ReturnsNull(string json)
		{
			Assert.Null(CommunityPackMega.ExtractDownloadUrl(json));
		}

		//Pinned against the Python implementation's output for the same key
		//and plaintext, so a drift in either counter handling is a failure
		//here rather than an unopenable zip in the field.
		[Fact]
		public void DecryptCtr_MatchesTheReferenceVector()
		{
			CommunityPackMega.FileRef file = CommunityPackMega.Parse("https://mega.nz/file/AbCd1234#" + KeyFragment)!;
			byte[] data = new byte[40];
			for(int i = 0; i < data.Length; i++) {
				data[i] = (byte)i;
			}

			CommunityPackMega.DecryptCtr(file.AesKey, file.Nonce, data);

			Assert.Equal(
				"9d83da6bf325525334dca0b98b1f33af3ba6506324faa84d8d60d34eb2b76d9ae3b14e15b3e03cf1",
				Convert.ToHexString(data).ToLowerInvariant());
		}

		//CTR is its own inverse, so a round trip is the property that matters
		//for a body that is not a multiple of the block size.
		[Fact]
		public void DecryptCtr_IsItsOwnInverse_AcrossBlockBoundaries()
		{
			CommunityPackMega.FileRef file = CommunityPackMega.Parse("https://mega.nz/file/AbCd1234#" + KeyFragment)!;
			byte[] original = new byte[4099];
			new Random(1234).NextBytes(original);
			byte[] data = (byte[])original.Clone();

			CommunityPackMega.DecryptCtr(file.AesKey, file.Nonce, data);
			Assert.NotEqual(original, data);
			CommunityPackMega.DecryptCtr(file.AesKey, file.Nonce, data);

			Assert.Equal(original, data);
		}

		[Fact]
		public void Scrub_RemovesTheKeyAndKeepsTheHandle()
		{
			const string secret = "zVnPz7NURkiwlODHLRtd3C1Oe1nhcwg7C_6vXbL8kyM";

			string scrubbed = CommunityPackMega.Scrub("GET https://mega.nz/file/yVsRmCiS#" + secret + " failed");

			Assert.DoesNotContain(secret, scrubbed);
			Assert.Contains("mega.nz/file/yVsRmCiS", scrubbed);
		}

		[Fact]
		public void Scrub_TextWithoutAMegaLink_IsUnchanged()
		{
			const string text = "[CommunityPackDownloader] REJECTED (host not allow-listed): https://example.com/x";

			Assert.Equal(text, CommunityPackMega.Scrub(text));
		}
	}
}
