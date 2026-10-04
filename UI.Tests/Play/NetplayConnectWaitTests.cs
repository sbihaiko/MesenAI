using System.Threading;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//Every user-perceivable wait shows a moving indicator: Netplay's Connect
	//blocks on the socket, so the window is busy from the click until the
	//attempt succeeds or fails.
	public class NetplayConnectWaitTests
	{
		[Fact]
		public async Task Busy_from_the_click_until_the_attempt_succeeds()
		{
			NetplayConnectWait wait = new();
			ManualResetEventSlim started = new(false);
			ManualResetEventSlim release = new(false);

			Assert.False(wait.IsConnecting);
			Task<bool> run = wait.RunAsync(() => { started.Set(); release.Wait(); return true; });
			Assert.True(wait.IsConnecting);
			started.Wait();
			Assert.True(wait.IsConnecting);

			release.Set();
			Assert.True(await run);
			Assert.False(wait.IsConnecting);
		}

		[Fact]
		public async Task Busy_clears_when_the_attempt_fails_or_throws()
		{
			NetplayConnectWait wait = new();
			Assert.False(await wait.RunAsync(() => false));
			Assert.False(wait.IsConnecting);

			await Assert.ThrowsAsync<System.InvalidOperationException>(() => wait.RunAsync(() => throw new System.InvalidOperationException()));
			Assert.False(wait.IsConnecting);
		}

		[Fact]
		public async Task Changed_fires_on_and_off()
		{
			NetplayConnectWait wait = new();
			System.Collections.Generic.List<bool> seen = new();
			wait.Changed += () => seen.Add(wait.IsConnecting);
			await wait.RunAsync(() => true);
			Assert.Equal(new[] { true, false }, seen);
		}

		[Fact]
		public async Task A_second_click_while_connecting_does_not_start_a_second_attempt()
		{
			NetplayConnectWait wait = new();
			ManualResetEventSlim release = new(false);
			int calls = 0;
			Task<bool> first = wait.RunAsync(() => { Interlocked.Increment(ref calls); release.Wait(); return true; });
			Assert.False(await wait.RunAsync(() => { Interlocked.Increment(ref calls); return true; }));
			release.Set();
			await first;
			Assert.Equal(1, calls);
		}
	}
}
