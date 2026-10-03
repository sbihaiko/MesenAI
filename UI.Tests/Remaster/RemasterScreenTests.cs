using System;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.3 (PRD Part B §13.5.3 W-R0-W-R3; rules 2, 4, 6, 10): which Remaster
	//screen shows and each control's state with its reason.
	public class RemasterScreenTests
	{
		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/py", Array.Empty<string>(), "3.12", ToolsGate.Found, "/tools");
		private static readonly RemasterFeasibility NoPython = Ready with { Python = PythonGate.Missing };

		private static RemasterInputs Inputs(bool game = true, ConsoleType console = ConsoleType.Nes, bool recording = false, bool job = false,
			string project = "/p/Contra", bool own = true, int textured = 1, RemasterFeasibility? feasibility = null, bool headless = true)
		{
			return new RemasterInputs(game, console, recording, job, project, own, textured, feasibility ?? Ready, headless);
		}

		[Fact]
		public void No_project_is_W_R0_and_its_primary_opens_a_rom_when_no_game_runs()
		{
			RemasterScreenState noGame = RemasterScreen.Evaluate(Inputs(game: false, project: ""));
			Assert.Equal(RemasterView.NoProject, noGame.View);
			Assert.True(noGame.PrimaryOpensRom);
			Assert.Equal(RemasterReason.NoGame, noGame.Record.Reason);

			RemasterScreenState running = RemasterScreen.Evaluate(Inputs(project: ""));
			Assert.False(running.PrimaryOpensRom);
			Assert.True(running.Record.Enabled);
		}

		[Fact]
		public void A_project_is_W_R1_and_a_recording_is_W_R2_without_the_banner()
		{
			Assert.Equal(RemasterView.Project, RemasterScreen.Evaluate(Inputs()).View);
			RemasterScreenState recording = RemasterScreen.Evaluate(Inputs(recording: true, feasibility: NoPython));
			Assert.Equal(RemasterView.Recording, recording.View);
			Assert.False(recording.ShowFeasibilityBanner);
		}

		[Fact]
		public void Missing_python_shows_the_banner_but_recording_stays_enabled()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(feasibility: NoPython));
			Assert.True(s.ShowFeasibilityBanner);
			Assert.True(s.Record.Enabled);
			Assert.Equal(RemasterReason.NeedsPython, s.PrepareFigures.Reason);

			RemasterScreenState noTools = RemasterScreen.Evaluate(Inputs(feasibility: Ready with { Tools = ToolsGate.Missing }));
			Assert.True(noTools.ShowFeasibilityBanner);
			Assert.Equal(RemasterReason.NeedsTools, noTools.PrepareFigures.Reason);
			Assert.False(RemasterScreen.Evaluate(Inputs()).ShowFeasibilityBanner);
		}

		[Theory]
		[InlineData(ConsoleType.Nes, true, RemasterReason.None)]
		[InlineData(ConsoleType.Gameboy, true, RemasterReason.NesOnly)]
		[InlineData(ConsoleType.Sms, true, RemasterReason.NesOnly)]
		[InlineData(ConsoleType.Gba, false, RemasterReason.NesOnly)]
		public void Console_scope_follows_the_data(ConsoleType console, bool canRecord, RemasterReason paintReason)
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(console: console));
			Assert.Equal(canRecord, s.Record.Enabled);
			if(!canRecord) {
				Assert.Equal(RemasterReason.ConsoleNotSupported, s.Record.Reason);
			}
			Assert.Equal(paintReason, s.PrepareFigures.Reason);
		}

		[Fact]
		public void Tas_ai_and_build_are_shown_disabled_with_their_reasons()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs());
			Assert.Equal(RemasterControl.Off(RemasterReason.TasLater), s.RecordFromTas);
			Assert.Equal(RemasterControl.Off(RemasterReason.AiNotReady), s.LetTheAiPlay);
			Assert.Equal(RemasterControl.Off(RemasterReason.NoKitYet), s.BuildAndShow);
			Assert.Equal(RemasterReason.TasNotInThisBuild, RemasterScreen.Evaluate(Inputs(headless: false)).RecordFromTas.Reason);
		}

		[Fact]
		public void A_running_job_disables_recording_and_another_kit_run()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(job: true));
			Assert.Equal(RemasterReason.JobRunning, s.Record.Reason);
			Assert.Equal(RemasterReason.JobRunning, s.PrepareFigures.Reason);
		}

		[Fact]
		public void A_chosen_project_of_another_game_cannot_be_recorded_into()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(own: false));
			Assert.Equal(RemasterReason.NotThisProjectsGame, s.Record.Reason);
			Assert.Equal(RemasterReason.NotThisProjectsGame, s.PrepareFigures.Reason);
		}

		[Fact]
		public void The_kit_runs_after_a_recording_only_when_it_can()
		{
			Assert.True(RemasterScreen.RunKitAfterRecording(Inputs()));
			Assert.False(RemasterScreen.RunKitAfterRecording(Inputs(textured: 0)));
			Assert.False(RemasterScreen.RunKitAfterRecording(Inputs(feasibility: NoPython)));
			Assert.False(RemasterScreen.RunKitAfterRecording(Inputs(console: ConsoleType.Gameboy)));
		}

		[Theory]
		[InlineData(0, "00:00")]
		[InlineData(102, "01:42")]
		[InlineData(3599, "59:59")]
		[InlineData(3723, "1:02:03")]
		[InlineData(-5, "00:00")]
		public void The_pill_shows_minutes_and_seconds(int seconds, string expected)
		{
			Assert.Equal(expected, RemasterScreen.FormatElapsed(TimeSpan.FromSeconds(seconds)));
		}

		//W-R2's "318 new shapes · 2 screens captured": hidden while the core's
		//coverage report is all zero (off NES, or nothing drawn yet).
		[Theory]
		[InlineData(0u, 0u, false)]
		[InlineData(318u, 2u, true)]
		[InlineData(5u, 0u, true)]
		[InlineData(0u, 1u, true)]
		public void The_pill_shows_its_counters_once_the_core_reports_any(uint tilesSeen, uint screensSeen, bool shown)
		{
			Assert.Equal(shown, RemasterScreen.ShowsRecordingCounters(tilesSeen, screensSeen));
		}

		[Theory]
		[InlineData(Workspace.Play, RemasterActivity.Recording, true)]
		[InlineData(Workspace.Share, RemasterActivity.Job, true)]
		[InlineData(Workspace.Remaster, RemasterActivity.Recording, false)]
		[InlineData(Workspace.Play, RemasterActivity.None, false)]
		public void The_profile_button_dot_marks_remaster_work_while_another_profile_shows(Workspace active, RemasterActivity activity, bool dot)
		{
			Assert.Equal(dot, RemasterActivityIndicator.ShowsDot(active, activity));
		}

		[Fact]
		public void Recording_outranks_a_job_for_the_dot()
		{
			Assert.Equal(RemasterActivity.Recording, RemasterActivityIndicator.Of(true, true));
			Assert.Equal(RemasterActivity.Job, RemasterActivityIndicator.Of(false, true));
			Assert.Equal(RemasterActivity.None, RemasterActivityIndicator.Of(false, false));
		}
	}
}
