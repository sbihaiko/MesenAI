using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//#639 (defense in depth): the Cheats sheet saves through CheatCodes, whose
	//file is the *running* game's. A sheet opened for one copy never writes
	//once another copy is running - it would replace that game's cheats with
	//the first game's list.
	public class CheatSheetSaveTargetTests
	{
		private const string ContraSha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
		private const string MarioSha1 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

		[Fact]
		public void The_copy_the_sheet_opened_for_is_saved()
		{
			Assert.True(CheatSheet.SavesTo(ContraSha1, ContraSha1));
		}

		[Fact]
		public void The_hash_compare_ignores_case()
		{
			Assert.True(CheatSheet.SavesTo(ContraSha1, ContraSha1.ToLowerInvariant()));
		}

		[Fact]
		public void Another_running_copy_is_never_written()
		{
			Assert.False(CheatSheet.SavesTo(ContraSha1, MarioSha1));
		}

		[Fact]
		public void A_game_gone_since_the_sheet_opened_is_never_written()
		{
			Assert.False(CheatSheet.SavesTo(ContraSha1, ""));
		}
	}
}
