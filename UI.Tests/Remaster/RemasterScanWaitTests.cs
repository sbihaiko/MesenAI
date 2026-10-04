using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//The user's rule (2026-10-03): every wait the user can see shows a moving
	//indicator. Opening or refreshing a Remaster project reads recordings,
	//shapes and painted cells off the UI thread; the screen shows a wait until
	//the last of those scans answers, and a stale scan never clears a newer one.
	public class RemasterScanWaitTests
	{
		[Fact]
		public void Nothing_waits_until_a_scan_begins_and_it_clears_when_the_scan_ends()
		{
			RemasterScanWait wait = new();
			Assert.False(wait.IsWaiting);
			int token = wait.Begin(RemasterScanKind.Shapes);
			Assert.True(wait.IsWaiting);
			Assert.True(wait.End(RemasterScanKind.Shapes, token));
			Assert.False(wait.IsWaiting);
		}

		[Fact]
		public void A_stale_scan_never_clears_a_newer_ones_wait()
		{
			RemasterScanWait wait = new();
			int old = wait.Begin(RemasterScanKind.Cells);
			int newer = wait.Begin(RemasterScanKind.Cells);
			Assert.False(wait.End(RemasterScanKind.Cells, old));
			Assert.True(wait.IsWaiting);
			Assert.True(wait.End(RemasterScanKind.Cells, newer));
			Assert.False(wait.IsWaiting);
		}

		[Fact]
		public void The_wait_lasts_until_every_kind_of_scan_has_ended()
		{
			RemasterScanWait wait = new();
			int shapes = wait.Begin(RemasterScanKind.Shapes);
			int cells = wait.Begin(RemasterScanKind.Cells);
			wait.End(RemasterScanKind.Shapes, shapes);
			Assert.True(wait.IsWaiting);
			wait.End(RemasterScanKind.Cells, cells);
			Assert.False(wait.IsWaiting);
		}

		[Fact]
		public void A_scan_that_is_no_longer_needed_clears_its_wait_and_its_late_answer_is_ignored()
		{
			RemasterScanWait wait = new();
			int token = wait.Begin(RemasterScanKind.Tiles);
			wait.Cancel(RemasterScanKind.Tiles);
			Assert.False(wait.IsWaiting);
			Assert.False(wait.End(RemasterScanKind.Tiles, token));
		}

		[Fact]
		public void The_projects_list_scan_has_its_own_words()
		{
			RemasterScanWait wait = new();
			wait.Begin(RemasterScanKind.Shapes);
			Assert.False(wait.IsWaitingFor(RemasterScanKind.Recent));
			wait.Begin(RemasterScanKind.Recent);
			Assert.True(wait.IsWaitingFor(RemasterScanKind.Recent));
			Assert.True(wait.IsWaitingForProject);
		}
	}
}
