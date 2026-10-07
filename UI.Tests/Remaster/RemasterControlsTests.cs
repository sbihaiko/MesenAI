using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//#992 (#984, PRD Part B §13.3 rule 10): with the wrong game W-R1 says
	//"This is not the game the project was recorded from." once, so the five
	//controls stay disabled with no reason under each. Any other disabled
	//control still shows its reason (rule 4).
	public sealed class RemasterControlsTests
	{
		private static string Sentence(RemasterReason reason) => reason == RemasterReason.None ? "" : "sentence:" + reason;

		private static IEnumerable<RemasterControlViewModel> Five(RemasterControls c)
		{
			return new[] { c.Record, c.RecordFromTas, c.LetTheAiPlay, c.PrepareFigures, c.BuildAndShow };
		}

		[Fact]
		public void Wrong_game_disables_all_five_controls_without_a_reason_under_each()
		{
			RemasterScreenState state = new(RemasterView.Project, false,
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.TasNotInThisBuild),
				RemasterControl.Off(RemasterReason.AiNotReady),
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.NoKitYet),
				false);

			RemasterControls controls = RemasterControls.From(state, Sentence);

			Assert.True(controls.IsWrongGame);
			Assert.All(Five(controls), c => {
				Assert.False(c.IsEnabled);
				Assert.Equal("", c.Reason);
				Assert.False(c.HasReason);
			});
		}

		[Fact]
		public void A_disabled_control_that_is_not_the_wrong_game_still_shows_its_reason()
		{
			RemasterScreenState state = new(RemasterView.Project, false,
				RemasterControl.On,
				RemasterControl.Off(RemasterReason.TasNotInThisBuild),
				RemasterControl.On,
				RemasterControl.Off(RemasterReason.NothingRecorded),
				RemasterControl.Off(RemasterReason.NoKitYet),
				false);

			RemasterControls controls = RemasterControls.From(state, Sentence);

			Assert.False(controls.IsWrongGame);
			Assert.Equal(new RemasterControlViewModel(true, ""), controls.Record);
			Assert.Equal(new RemasterControlViewModel(false, "sentence:TasNotInThisBuild"), controls.RecordFromTas);
			Assert.Equal(new RemasterControlViewModel(true, ""), controls.LetTheAiPlay);
			Assert.Equal(new RemasterControlViewModel(false, "sentence:NothingRecorded"), controls.PrepareFigures);
			Assert.Equal(new RemasterControlViewModel(false, "sentence:NoKitYet"), controls.BuildAndShow);
		}

		[Fact]
		public void Not_this_projects_game_off_the_project_screen_is_not_the_wrong_game_row()
		{
			RemasterScreenState state = new(RemasterView.NoProject, false,
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				RemasterControl.Off(RemasterReason.NotThisProjectsGame),
				false);

			RemasterControls controls = RemasterControls.From(state, Sentence);

			Assert.False(controls.IsWrongGame);
			Assert.Equal("sentence:NotThisProjectsGame", controls.Record.Reason);
		}
	}
}
