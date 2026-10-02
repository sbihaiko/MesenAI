using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.3 (PRD Part B §13.5.3 W-R3, rule 6): the job card's runner over a fake
	//child process - progress from mep_project.py kit's own lines, Stop kills,
	//the result is one of done / failed (with its line) / stopped.
	public class RemasterJobRunnerTests
	{
		private sealed class FakeProcess : IJobProcess
		{
			public bool Killed { get; private set; }
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };
			public IReadOnlyList<string> Argv = Array.Empty<string>();

			public void Kill()
			{
				Killed = true;
				OnExit(-9);
			}
		}

		private sealed class FakeLauncher : IJobProcessLauncher
		{
			public FakeProcess? Last;
			public bool Throw;
			public int Started;

			public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
			{
				if(Throw) {
					throw new InvalidOperationException("no such file");
				}
				Started++;
				Last = new FakeProcess { OnLine = onLine, OnExit = onExit, Argv = argv };
				return Last;
			}
		}

		private static RemasterJobSpec Spec(int recordings = 1) =>
			RemasterJobs.Kit(new PythonCandidate("/py", new[] { "-X" }), "/tools", "/p/Contra", "/roms/Contra.nes", recordings, "Contra");

		[Theory]
		[InlineData(0, 0)]
		[InlineData(1, 5)]
		[InlineData(2, 8)]
		public void The_kit_has_two_generators_per_recording_one_page_union_and_one_assemble_per_kit(int recordings, int steps)
		{
			Assert.Equal(steps, RemasterJobs.KitSteps(recordings));
		}

		[Fact]
		public void The_kit_job_runs_mep_project_kit_on_the_users_python_with_the_rom()
		{
			RemasterJobSpec spec = Spec();
			Assert.Equal(new[] { "/py", "-X", System.IO.Path.Combine("/tools", "mep_project.py"), "kit", "/p/Contra", "--rom", "/roms/Contra.nes" }, spec.Argv.ToArray());
			Assert.Equal("/tools", spec.WorkingDirectory);
		}

		[Fact]
		public void Progress_follows_the_step_lines_and_success_fills_the_bar()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			List<RemasterJobSnapshot> seen = new();
			runner.Changed += seen.Add;

			Assert.True(runner.Start(Spec()));
			Assert.True(runner.Snapshot.IsRunning);
			launcher.Last!.OnLine("== artist_kit.py -> /p/Contra/kit/rec-001", false);
			Assert.Equal("artist_kit.py", runner.Snapshot.CurrentStep);
			Assert.Equal(RemasterJobStep.Figures, RemasterJobs.StepOf(runner.Snapshot.CurrentStep));
			launcher.Last.OnLine("some generator chatter", false);
			launcher.Last.OnLine("ok   artist_kit.py -> /p/Contra/kit/rec-001", false);
			launcher.Last.OnLine("== artist_bg_kit.py -> /p/Contra/kit/rec-001", false);
			Assert.Equal(1, runner.Snapshot.StepsDone);
			Assert.Equal(20, runner.Snapshot.Percent);

			launcher.Last.OnExit(0);

			Assert.Equal(RemasterJobStatus.Succeeded, runner.Snapshot.Status);
			Assert.Equal(100, runner.Snapshot.Percent);
			Assert.Equal(RemasterJobStatus.Running, seen.First().Status);
		}

		[Fact]
		public void A_failed_step_is_the_failure_line_even_when_later_steps_pass()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec());
			launcher.Last!.OnLine("Traceback (most recent call last):", true);
			launcher.Last.OnLine("FAIL artist_chr_kit.py -> /p/Contra/kit/pages", false);
			launcher.Last.OnLine("ok   artist_kit_assemble.py -> /p/Contra/kit/pages", false);
			launcher.Last.OnExit(1);

			Assert.Equal(RemasterJobStatus.Failed, runner.Snapshot.Status);
			Assert.Equal("FAIL artist_chr_kit.py -> /p/Contra/kit/pages", runner.Snapshot.FailureLine);
		}

		[Fact]
		public void Without_a_fail_line_the_last_error_line_explains_the_failure()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec());
			launcher.Last!.OnLine("error: /p/Contra has no recording with textures - nothing to project into a kit", true);
			launcher.Last.OnLine("   ", true);
			launcher.Last.OnExit(1);

			Assert.Equal("error: /p/Contra has no recording with textures - nothing to project into a kit", runner.Snapshot.FailureLine);
		}

		[Fact]
		public void Stop_kills_the_child_and_reads_as_stopped_not_failed()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec());

			runner.Stop();

			Assert.True(launcher.Last!.Killed);
			Assert.Equal(RemasterJobStatus.Stopped, runner.Snapshot.Status);
			Assert.Equal("", runner.Snapshot.FailureLine);
		}

		[Fact]
		public void One_job_at_a_time_and_a_late_line_from_a_finished_job_changes_nothing()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec());
			FakeProcess first = launcher.Last!;
			Assert.False(runner.Start(Spec()));
			Assert.Equal(1, launcher.Started);

			first.OnExit(0);
			runner.Clear();
			Assert.Equal(RemasterJobStatus.Idle, runner.Snapshot.Status);
			first.OnLine("ok   artist_kit.py -> x", false);
			first.OnExit(1);
			Assert.Equal(RemasterJobStatus.Idle, runner.Snapshot.Status);

			Assert.True(runner.Start(Spec()));
			first.OnExit(1);
			Assert.True(runner.Snapshot.IsRunning);
		}

		[Fact]
		public void A_program_that_cannot_start_is_a_failure_with_its_reason()
		{
			RemasterJobRunner runner = new(new FakeLauncher { Throw = true });
			Assert.True(runner.Start(Spec()));
			Assert.Equal(RemasterJobStatus.Failed, runner.Snapshot.Status);
			Assert.Equal("no such file", runner.Snapshot.FailureLine);
		}
	}
}
