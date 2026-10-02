using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//G.1 (W-S1): the status line is one read-only sentence; while the bar is
	//visible it is the one place that always names the current pack.
	public class ShellStatusLineTests
	{
		[Fact]
		public void No_game_is_no_game_loaded_whatever_else_is_set()
		{
			Assert.Equal(ShellStatusKind.NoGame, ShellStatusLine.Classify(false, false, ""));
			Assert.Equal(ShellStatusKind.NoGame, ShellStatusLine.Classify(false, true, "Contra 80s"));
		}

		[Theory]
		[InlineData(false, "", ShellStatusKind.Playing)]
		[InlineData(false, "Contra 80s", ShellStatusKind.PlayingWithPack)]
		[InlineData(true, "", ShellStatusKind.Paused)]
		[InlineData(true, "Contra 80s", ShellStatusKind.PausedWithPack)]
		[InlineData(true, "   ", ShellStatusKind.Paused)]
		public void Game_loaded_names_the_pack_when_there_is_one(bool paused, string pack, ShellStatusKind expected)
		{
			Assert.Equal(expected, ShellStatusLine.Classify(true, paused, pack));
		}
	}
}
