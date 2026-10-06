using System.Diagnostics;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Utilities
{
	//#859: opening a URL must not pass through a shell. The old Linux path joined
	//`xdg-open {url}` into one string, hand-escaped it and ran it through
	//`/bin/sh -c`; there a newline became a second command, a quote was corrupted
	//into a stray backslash plus a quote that toggles quoting, and `$` reached the
	//shell live. The URL is an argument, never shell text.
	public sealed class BrowserLaunchTests
	{
		//Everything a shell would act on, plus a newline (a command separator).
		private const string HostileUrl = "https://example.com/?q=$(id)`id`;\"x\"&|<>~!#*()${IFS}\nnano /etc/passwd";

		[Fact]
		public void Linux_hands_the_url_to_xdg_open_as_one_argument_and_no_shell()
		{
			ProcessStartInfo info = BrowserLaunch.Build(HostileUrl, BrowserLaunch.Platform.Linux);

			Assert.Equal("xdg-open", info.FileName);
			Assert.False(info.UseShellExecute);
			//One argv entry, byte-for-byte the URL: nothing split it, nothing escaped it.
			Assert.Equal(HostileUrl, Assert.Single(info.ArgumentList));
			//No shell wrapper hiding in the argument list.
			Assert.DoesNotContain(info.ArgumentList, a => a == "-c" || a == "/bin/sh");
		}

		[Fact]
		public void MacOS_hands_the_url_to_open_as_one_argument_and_no_shell()
		{
			ProcessStartInfo info = BrowserLaunch.Build(HostileUrl, BrowserLaunch.Platform.MacOS);

			Assert.Equal("open", info.FileName);
			Assert.False(info.UseShellExecute);
			Assert.Equal(HostileUrl, Assert.Single(info.ArgumentList));
		}

		[Fact]
		public void Windows_hands_the_url_to_shell_execute_without_an_argument()
		{
			ProcessStartInfo info = BrowserLaunch.Build(HostileUrl, BrowserLaunch.Platform.Windows);

			//The ShellExecute handoff resolves the URL through the registry; it is
			//the target, not a joined command line, so there is nothing to escape.
			Assert.Equal(HostileUrl, info.FileName);
			Assert.True(info.UseShellExecute);
			Assert.Empty(info.ArgumentList);
		}

		[Theory]
		[InlineData(BrowserLaunch.Platform.Linux)]
		[InlineData(BrowserLaunch.Platform.MacOS)]
		[InlineData(BrowserLaunch.Platform.Windows)]
		public void No_platform_runs_the_url_through_a_shell(BrowserLaunch.Platform platform)
		{
			ProcessStartInfo info = BrowserLaunch.Build("https://example.com/page", platform);

			Assert.NotEqual("/bin/sh", info.FileName);
			Assert.DoesNotContain("-c", info.Arguments);
		}
	}
}
