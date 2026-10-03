using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#658: the Core's load thread waits on the UI's answer (W-P13's BIOS sheet)
	//while it holds its load locks, so closing answers every pending request
	//before EmuApi.Stop takes those locks, and answers any later one at once.
	public class CoreRequestWaitsTests
	{
		[Fact]
		public void A_request_waits_until_its_answer_ends_it()
		{
			CoreRequestWaits waits = new();
			TaskCompletionSource wait = waits.Begin();
			Assert.False(wait.Task.IsCompleted);
			Assert.Equal(1, waits.PendingCount);

			waits.End(wait);
			Assert.True(wait.Task.IsCompleted);
			Assert.Equal(0, waits.PendingCount);
		}

		[Fact]
		public void Closing_answers_every_pending_request()
		{
			CoreRequestWaits waits = new();
			TaskCompletionSource first = waits.Begin();
			TaskCompletionSource second = waits.Begin();

			waits.Close();

			Assert.True(waits.IsClosed);
			Assert.True(first.Task.IsCompleted);
			Assert.True(second.Task.IsCompleted);
			Assert.Equal(0, waits.PendingCount);
		}

		[Fact]
		public void A_request_after_closing_is_answered_at_once()
		{
			CoreRequestWaits waits = new();
			waits.Close();

			TaskCompletionSource late = waits.Begin();

			Assert.True(late.Task.IsCompleted);
			Assert.Equal(0, waits.PendingCount);
		}

		[Fact]
		public void An_answer_after_closing_is_harmless()
		{
			CoreRequestWaits waits = new();
			TaskCompletionSource wait = waits.Begin();
			waits.Close();

			waits.End(wait);

			Assert.True(wait.Task.IsCompletedSuccessfully);
		}
	}
}
