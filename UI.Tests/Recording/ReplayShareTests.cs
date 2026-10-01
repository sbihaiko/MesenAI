using System;
using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Recording
{
	//ADR-0205 sections 2 and 6 (slice R.1): the host-free half of the
	//Record-and-share action - the file name it writes and the pre-filled issue
	//URL it opens in the author's own browser.
	public class ReplayShareTests
	{
		[Fact]
		public void IssueUrl_TargetsTheReplayFormOnThisRepository_WithTheFormsLabel()
		{
			Uri url = new Uri(ReplayShare.BuildIssueUrl(""));
			Assert.Equal("https", url.Scheme);
			Assert.Equal("github.com", url.Host);
			Assert.Equal("/sbihaiko/MesenAI/issues/new", url.AbsolutePath);
			Assert.Contains("template=replay.yml", url.Query);
			Assert.Contains("labels=replay", url.Query);
		}

		[Fact]
		public void IssueUrl_PrefillsOnlyThePrefixAsTitle_BecauseTheWorkflowRewritesTheWholeTitle()
		{
			string query = new Uri(ReplayShare.BuildIssueUrl("")).Query;
			Assert.Contains("title=" + Uri.EscapeDataString("[Replay] "), query);
		}

		[Fact]
		public void IssueUrl_CarriesTheDescriptionInTheNotesField_Escaped()
		{
			string query = new Uri(ReplayShare.BuildIssueUrl("stage skip & \"quotes\"\nsecond line")).Query;
			Assert.Contains("notes=" + Uri.EscapeDataString("stage skip & \"quotes\"\nsecond line"), query);
			Assert.DoesNotContain("& \"", query);
		}

		[Fact]
		public void IssueUrl_EscapesANonBmpCharacterWhole()
		{
			string query = new Uri(ReplayShare.BuildIssueUrl("run \U0001F3AE")).Query;
			Assert.Contains("notes=" + Uri.EscapeDataString("run \U0001F3AE"), query);
			Assert.DoesNotContain("%EF%BF%BD", query);
		}

		[Fact]
		public void IssueUrl_BoundsALongDescription_SoTheBrowserAcceptsTheLink()
		{
			string url = ReplayShare.BuildIssueUrl(new string('x', 10000));
			Assert.True(url.Length <= ReplayShare.MaxUrlLength, "url length " + url.Length);
		}

		[Theory]
		[InlineData("a", 300)]
		[InlineData("\u3042", 300)]
		[InlineData("\U0001F3AE", 300)]
		public void FileName_StaysWithin255Bytes_AndKeepsTheTimestampAndExtension(string unit, int count)
		{
			string name = ReplayShare.FileName(string.Concat(System.Linq.Enumerable.Repeat(unit, count)), new DateTime(2026, 10, 1, 14, 5, 9));
			Assert.True(Encoding.UTF8.GetByteCount(name) <= 255, "bytes " + Encoding.UTF8.GetByteCount(name));
			Assert.EndsWith(" 2026-10-01 14.05.09.mmo", name);
			Assert.DoesNotContain("\uFFFD", name);
		}

		[Fact]
		public void FileName_IsAMmoNamedAfterTheRom_AndNeverCarriesAPathSeparator()
		{
			string name = ReplayShare.FileName("Contra (USA)/odd:name?", new DateTime(2026, 10, 1, 14, 5, 9));
			Assert.EndsWith(".mmo", name);
			Assert.StartsWith("Contra (USA)", name);
			Assert.DoesNotContain("/", name);
			Assert.DoesNotContain("\\", name);
			Assert.DoesNotContain(":", name);
			Assert.DoesNotContain("?", name);
			Assert.Contains("2026-10-01", name);
		}

		[Fact]
		public void FileName_FallsBackWhenTheRomHasNoName()
		{
			Assert.StartsWith("replay", ReplayShare.FileName("", new DateTime(2026, 10, 1)));
		}
	}
}
