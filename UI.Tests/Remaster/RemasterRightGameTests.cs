using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//#969 (PRD Part B §13.5.5 W-X2, §13.3 rule 10): "This is not the game the
	//project was recorded from." comes with Open the Right Game…, which loads
	//the ROM the project folder is named after, or opens the picker.
	public sealed class RemasterRightGameTests
	{
		private const string Project = "/games/Contra (U)";

		[Fact]
		public void Not_this_projects_game_resolves_the_matching_recent_rom()
		{
			RemasterRightGame? step = RemasterScreen.RightGameStep(RemasterReason.NotThisProjectsGame, Project,
				new[] { "/elsewhere/Mega Man.nes", "/old/Contra (U).nes" }, new string[0]);

			Assert.NotNull(step);
			Assert.Equal("/old/Contra (U).nes", step!.RomPath);
			Assert.False(step.OpensPicker);
		}

		[Fact]
		public void With_no_recent_match_the_games_folder_rom_is_used()
		{
			RemasterRightGame? step = RemasterScreen.RightGameStep(RemasterReason.NotThisProjectsGame, Project,
				new[] { "/elsewhere/Mega Man.nes" }, new[] { "/games/Contra.nes", "/games/contra (u).NES" });

			Assert.Equal("/games/contra (u).NES", step!.RomPath);
		}

		[Fact]
		public void With_no_matching_rom_the_step_opens_the_picker()
		{
			RemasterRightGame? step = RemasterScreen.RightGameStep(RemasterReason.NotThisProjectsGame, Project + "/",
				new[] { "/elsewhere/Mega Man.nes" }, new[] { "/games/Contra (J).nes" });

			Assert.NotNull(step);
			Assert.True(step!.OpensPicker);
			Assert.Equal("", step.RomPath);
		}

		[Theory]
		[InlineData(RemasterReason.None)]
		[InlineData(RemasterReason.NoGame)]
		[InlineData(RemasterReason.JobRunning)]
		public void Any_other_reason_has_no_right_game_step(RemasterReason reason)
		{
			Assert.Null(RemasterScreen.RightGameStep(reason, Project, new[] { "/old/Contra (U).nes" }, new string[0]));
		}

		[Fact]
		public void A_project_and_a_foreign_game_disable_record_with_the_reason_that_has_the_step()
		{
			RemasterInputs inputs = new(true, Mesen.Interop.ConsoleType.Nes, false, false, Project, false, 1,
				new RemasterFeasibility(PythonGate.Found, "", new string[0], "", ToolsGate.Found, ""), true);

			RemasterScreenState s = RemasterScreen.Evaluate(inputs);

			Assert.Equal(RemasterView.Project, s.View);
			Assert.Equal(RemasterReason.NotThisProjectsGame, s.Record.Reason);
			Assert.NotNull(RemasterScreen.RightGameStep(s.Record.Reason, Project, new string[0], new string[0]));
		}
	}
}
