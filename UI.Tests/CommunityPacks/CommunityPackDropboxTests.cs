using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//Coverage for CommunityPackDropbox (allow-list kind "dropbox", ADR-0187
	//§2): the dl=1 rewrite. Mirrors scripts/test_fetch_pack.py's
	//check_dropbox_dl_rewrite - CI and client must agree on what a given
	//share link downloads to.
	public class CommunityPackDropboxTests
	{
		[Theory]
		[InlineData("https://www.dropbox.com/s/abc/Pack.zip?dl=0", "https://www.dropbox.com/s/abc/Pack.zip?dl=1")]
		[InlineData("https://www.dropbox.com/s/abc/Pack.zip", "https://www.dropbox.com/s/abc/Pack.zip?dl=1")]
		[InlineData("https://www.dropbox.com/s/abc/Pack.zip?dl=1", "https://www.dropbox.com/s/abc/Pack.zip?dl=1")]
		public void ForceDownload_SetsDlFlag(string url, string expected)
		{
			Assert.Equal(expected, CommunityPackDropbox.ForceDownload(url));
		}

		//The /scl/fi/ links 404 without rlkey, so the rewrite must not be a
		//"replace the query" shortcut.
		[Fact]
		public void ForceDownload_KeepsEveryOtherParameter()
		{
			string result = CommunityPackDropbox.ForceDownload(
				"https://www.dropbox.com/scl/fi/x/Pack.zip?rlkey=k1&st=abc&dl=0");

			Assert.Contains("rlkey=k1", result);
			Assert.Contains("st=abc", result);
			Assert.Contains("dl=1", result);
			Assert.DoesNotContain("dl=0", result);
		}

		[Theory]
		[InlineData("")]
		[InlineData("not a url")]
		public void ForceDownload_UnparseableInput_IsReturnedUnchanged(string url)
		{
			Assert.Equal(url, CommunityPackDropbox.ForceDownload(url));
		}
	}
}
