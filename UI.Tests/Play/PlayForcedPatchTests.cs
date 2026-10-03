using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#732: a pack's ROM patch forced onto a revision it was not made for
	//(ApplyPatchOnHashMismatch) can freeze the game on one frame. In Player
	//mode the player is told in place, in the shared warning banner, with a way
	//out that reloads the game without the patch; Advanced keeps the core's OSD
	//line. The banner belongs to the load that forced the patch.
	public class PlayForcedPatchTests
	{
		private const string Patch = "/packs/HdPacks/Castlevania (1987) (Konami)/Castlevania_PRG1.ips";

		[Theory]
		[InlineData(InterruptionKind.None)]
		[InlineData(InterruptionKind.ForcedPatch)]
		public void A_forced_patch_in_Player_mode_shows_the_banner(InterruptionKind showing)
		{
			Assert.Equal(ForcedPatchBanner.Show, PlayForcedPatch.AfterLoad(UiMode.Player, Patch, showing));
		}

		[Fact]
		public void A_question_about_lost_work_is_never_replaced_by_the_warning()
		{
			Assert.Equal(ForcedPatchBanner.Leave, PlayForcedPatch.AfterLoad(UiMode.Player, Patch, InterruptionKind.QuitApp));
			Assert.Equal(ForcedPatchBanner.Leave, PlayForcedPatch.AfterLoad(UiMode.Player, Patch, InterruptionKind.ReloadWhileRecording));
		}

		[Theory]
		[InlineData(UiMode.Player)]
		[InlineData(UiMode.Advanced)]
		public void A_load_without_a_forced_patch_withdraws_the_previous_loads_banner(UiMode mode)
		{
			Assert.Equal(ForcedPatchBanner.Withdraw, PlayForcedPatch.AfterLoad(mode, "", InterruptionKind.ForcedPatch));
			Assert.Equal(ForcedPatchBanner.Leave, PlayForcedPatch.AfterLoad(mode, "", InterruptionKind.None));
			Assert.Equal(ForcedPatchBanner.Leave, PlayForcedPatch.AfterLoad(mode, "", InterruptionKind.QuitApp));
		}

		[Fact]
		public void Advanced_mode_keeps_the_cores_osd_line()
		{
			Assert.Equal(ForcedPatchBanner.Leave, PlayForcedPatch.AfterLoad(UiMode.Advanced, Patch, InterruptionKind.None));
		}

		[Theory]
		[InlineData(Patch, "Castlevania_PRG1.ips")]
		[InlineData("C:\\Mesen\\HdPacks\\Castlevania\\Castlevania_PRG1.ips", "Castlevania_PRG1.ips")]
		[InlineData("Castlevania_PRG1.ips", "Castlevania_PRG1.ips")]
		[InlineData("", "")]
		public void The_banner_names_the_patch_by_its_file_name(string forcedPatch, string expected)
		{
			Assert.Equal(expected, PlayForcedPatch.PatchName(forcedPatch));
		}

		[Fact]
		public void The_banner_goes_with_the_game()
		{
			Assert.True(PlayForcedPatch.WithdrawsWithoutGame(InterruptionKind.ForcedPatch));
			Assert.False(PlayForcedPatch.WithdrawsWithoutGame(InterruptionKind.QuitApp));
			Assert.False(PlayForcedPatch.WithdrawsWithoutGame(InterruptionKind.None));
		}

		//ADR-0249 (W-X2): a warning, not a stop - nothing is lost - and the way
		//out reloads the game, so it is the tinted button, in Play's tint.
		[Fact]
		public void The_warning_is_the_pale_orange_banner_with_a_tinted_reload_in_plays_tint()
		{
			Assert.Equal(BannerKind.Warning, InterruptionBanner.KindOf(InterruptionKind.ForcedPatch));
			Assert.True(InterruptionBanner.GoIsTinted(InterruptionKind.ForcedPatch));
			Assert.Equal(Workspace.Play, InterruptionBanner.WorkspaceOf(InterruptionKind.ForcedPatch));
			Assert.False(Interruptions.Quits(InterruptionKind.ForcedPatch));
		}
	}
}
