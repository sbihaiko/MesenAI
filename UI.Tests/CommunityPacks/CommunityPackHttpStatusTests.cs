using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//Coverage for CommunityPackHttpStatus (ADR-0138 §50): the downloader
	//must follow real redirects and must NOT treat 304 Not Modified as one.
	public class CommunityPackHttpStatusTests
	{
		[Theory]
		[InlineData(301)]
		[InlineData(302)]
		[InlineData(303)]
		[InlineData(307)]
		[InlineData(308)]
		public void IsFollowableRedirect_RfcRedirects_ReturnsTrue(int statusCode)
		{
			Assert.True(CommunityPackHttpStatus.IsFollowableRedirect(statusCode));
		}

		[Theory]
		[InlineData(200)]
		[InlineData(204)]
		[InlineData(304)]
		[InlineData(400)]
		[InlineData(404)]
		[InlineData(500)]
		[InlineData(300)]
		[InlineData(305)]
		[InlineData(306)]
		public void IsFollowableRedirect_NonRedirectsIncluding304_ReturnsFalse(int statusCode)
		{
			Assert.False(CommunityPackHttpStatus.IsFollowableRedirect(statusCode));
		}

		//A refusal is not a network failure. The pack client used to test only
		//"did a body come back?", so an HTTP 403 (rate limited / not permitted)
		//was reported as "host not allowed, redirect rejected, size cap,
		//timeout, or network error" - five causes, none of them the real one -
		//and the ROM-load latch was cleared, re-attempting the same refused
		//download on every following load.
		[Theory]
		[InlineData(403)]
		[InlineData(429)]
		public void IsRateLimited_RefusalStatuses_ReturnsTrue(int statusCode)
		{
			Assert.True(CommunityPackHttpStatus.IsRateLimited(statusCode));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(200)]
		[InlineData(304)]
		[InlineData(404)]
		[InlineData(410)]
		[InlineData(500)]
		public void IsRateLimited_AnythingElse_ReturnsFalse(int statusCode)
		{
			Assert.False(CommunityPackHttpStatus.IsRateLimited(statusCode));
		}

		//Describe is what lands in mesen.log, the only surface a failed
		//auto-install has (ADR-0138 SS42 keeps the routine path silent).
		//The assertion that matters is the negative one: a 403 must not be
		//described as the generic network bucket.
		[Theory]
		[InlineData(403)]
		[InlineData(429)]
		public void Describe_RefusalStatuses_NamesTheStatusAndNeverTheNetworkBucket(int statusCode)
		{
			string text = CommunityPackHttpStatus.Describe(statusCode);
			Assert.Contains("HTTP " + statusCode, text);
			Assert.DoesNotContain("network error", text);
			Assert.Contains("refus", text);
		}

		[Fact]
		public void Describe_NoHttpAnswer_NamesTheGenericBucket()
		{
			string text = CommunityPackHttpStatus.Describe(0);
			Assert.Contains("no HTTP answer", text);
			Assert.Contains("network error", text);
		}

		[Fact]
		public void Describe_NotFound_NamesTheStatus()
		{
			Assert.Contains("HTTP 404", CommunityPackHttpStatus.Describe(404));
		}

		[Fact]
		public void Describe_ServerError_NamesTheStatus()
		{
			string text = CommunityPackHttpStatus.Describe(503);
			Assert.Contains("HTTP 503", text);
			Assert.DoesNotContain("refus", text);
		}
	}
}
