using System;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//The user's rule (2026-10-03): every wait the user can see shows a moving
	//indicator, so it never looks broken. Starting or stopping a recording
	//blocks in the core (the builder exports every ROM tile, 0.4-1 s), so it
	//runs off the UI thread and the screen shows the transition; the
	//Python/tools probe and a job's step show theirs too.
	public class RemasterWaitsTests
	{
		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/py", Array.Empty<string>(), "3.12", ToolsGate.Found, "/tools");

		private static RemasterInputs Inputs(bool recording = false, RecordingTransition transition = RecordingTransition.None, bool pending = false,
			string project = "/p/Contra", bool hasKit = true)
		{
			return new RemasterInputs(true, ConsoleType.Nes, recording, false, project, true, 1, Ready, true, hasKit,
				Transition: transition, FeasibilityPending: pending);
		}

		[Fact]
		public void While_a_recording_starts_record_and_the_jobs_wait_and_the_screen_stays()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(transition: RecordingTransition.Starting));
			Assert.Equal(RemasterView.Project, s.View);
			Assert.Equal(RemasterControl.Off(RemasterReason.RecordingStarting), s.Record);
			Assert.Equal(RemasterControl.Off(RemasterReason.RecordingStarting), s.PrepareFigures);
			Assert.Equal(RemasterControl.Off(RemasterReason.RecordingStarting), s.BuildAndShow);

			RemasterScreenState noProject = RemasterScreen.Evaluate(Inputs(transition: RecordingTransition.Starting, project: ""));
			Assert.Equal(RemasterView.NoProject, noProject.View);
			Assert.False(noProject.Record.Enabled);
		}

		[Fact]
		public void While_a_recording_stops_the_recording_view_stays_until_the_core_answers()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(recording: true, transition: RecordingTransition.Stopping));
			Assert.Equal(RemasterView.Recording, s.View);
			Assert.False(s.Record.Enabled);
			Assert.False(s.PrepareFigures.Enabled);
		}

		[Fact]
		public void A_click_or_esc_during_a_transition_does_nothing()
		{
			Assert.True(RecordingTransitions.AcceptsClick(RecordingTransition.None));
			Assert.False(RecordingTransitions.AcceptsClick(RecordingTransition.Starting));
			Assert.False(RecordingTransitions.AcceptsClick(RecordingTransition.Stopping));
			Assert.False(RecordingTransitions.ShowsWait(RecordingTransition.None));
			Assert.True(RecordingTransitions.ShowsWait(RecordingTransition.Starting));
			Assert.True(RecordingTransitions.ShowsWait(RecordingTransition.Stopping));
		}

		[Fact]
		public void While_the_tools_are_checked_the_jobs_wait_with_their_reason_and_the_check_shows()
		{
			RemasterScreenState s = RemasterScreen.Evaluate(Inputs(pending: true));
			Assert.True(s.ShowFeasibilityChecking);
			Assert.False(s.ShowFeasibilityBanner);
			Assert.Equal(RemasterControl.Off(RemasterReason.CheckingTools), s.PrepareFigures);
			Assert.Equal(RemasterControl.Off(RemasterReason.CheckingTools), s.BuildAndShow);
			//Recording needs neither Python nor the tools.
			Assert.True(s.Record.Enabled);

			Assert.True(RemasterScreen.Evaluate(Inputs(pending: true, project: "")).ShowFeasibilityChecking);
			Assert.False(RemasterScreen.Evaluate(Inputs(pending: true, recording: true)).ShowFeasibilityChecking);
			Assert.False(RemasterScreen.Evaluate(Inputs()).ShowFeasibilityChecking);
		}

		[Fact]
		public void A_running_jobs_bar_moves_within_a_step()
		{
			RemasterJobSnapshot firstStep = new(RemasterJobStatus.Running, RemasterJobKind.Kit, 0, 4, "", "", "Contra");
			Assert.Equal(0, firstStep.Percent);
			Assert.True(firstStep.BarIsIndeterminate);
			Assert.True((firstStep with { StepsDone = 2 }).BarIsIndeterminate);
			Assert.False((firstStep with { Status = RemasterJobStatus.Succeeded }).BarIsIndeterminate);
			Assert.False(RemasterJobSnapshot.Idle.BarIsIndeterminate);
		}
	}
}
