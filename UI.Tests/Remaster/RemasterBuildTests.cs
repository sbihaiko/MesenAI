using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.6 (PRD Part B §13.5.3 W-R1 zone 3, W-R3, W-R4): Build & show in game -
	//the job that runs `mep_project.py build`, when the button is enabled, what
	//the result asks the game to do, and the "N files changed" line.
	public class RemasterBuildTests
	{
		private sealed class FakeProcess : IJobProcess
		{
			public Action<string, bool> OnLine = (_, _) => { };
			public Action<int> OnExit = _ => { };
			public void Kill() => OnExit(-9);
		}

		private sealed class FakeLauncher : IJobProcessLauncher
		{
			public FakeProcess? Last;

			public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
			{
				Last = new FakeProcess { OnLine = onLine, OnExit = onExit };
				return Last;
			}
		}

		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/py", Array.Empty<string>(), "3.12", ToolsGate.Found, "/tools");

		private static RemasterInputs Inputs(bool game = true, ConsoleType console = ConsoleType.Nes, bool recording = false, bool job = false,
			bool own = true, int textured = 1, RemasterFeasibility? feasibility = null, bool kit = true)
		{
			return new RemasterInputs(game, console, recording, job, "/p/Contra", own, textured, feasibility ?? Ready, true, kit);
		}

		private static RemasterJobSpec Spec() =>
			RemasterBuilds.Spec(new PythonCandidate("/py", new[] { "-X" }), "/tools", "/p/Contra", "/roms/Contra.nes", "Contra");

		[Fact]
		public void The_build_job_runs_mep_project_build_with_the_rom_in_four_steps()
		{
			RemasterJobSpec spec = Spec();
			Assert.Equal(RemasterJobKind.Build, spec.Kind);
			Assert.Equal(new[] { "/py", "-X", Path.Combine("/tools", "mep_project.py"), "build", "/p/Contra", "--rom", "/roms/Contra.nes" }, spec.Argv.ToArray());
			Assert.Equal(4, spec.TotalSteps);
		}

		[Fact]
		public void Build_and_show_is_enabled_for_the_running_games_project_with_a_kit()
		{
			Assert.Equal(RemasterControl.On, RemasterScreen.Evaluate(Inputs()).BuildAndShow);
		}

		[Theory]
		[InlineData(false, ConsoleType.Nes, false, false, true, 1, true, RemasterReason.NoGame)]
		[InlineData(true, ConsoleType.Nes, false, false, false, 1, true, RemasterReason.NotThisProjectsGame)]
		[InlineData(true, ConsoleType.Gameboy, false, false, true, 1, true, RemasterReason.NesOnly)]
		[InlineData(true, ConsoleType.Nes, false, false, true, 0, false, RemasterReason.NothingRecorded)]
		[InlineData(true, ConsoleType.Nes, false, false, true, 1, false, RemasterReason.NoKitYet)]
		[InlineData(true, ConsoleType.Nes, true, false, true, 1, true, RemasterReason.RecordingRunning)]
		[InlineData(true, ConsoleType.Nes, false, true, true, 1, true, RemasterReason.JobRunning)]
		public void Build_and_show_is_disabled_with_its_reason(bool game, ConsoleType console, bool recording, bool job, bool own, int textured, bool kit, RemasterReason reason)
		{
			RemasterControl build = RemasterScreen.Evaluate(Inputs(game, console, recording, job, own, textured, null, kit)).BuildAndShow;
			Assert.Equal(RemasterControl.Off(reason), build);
		}

		[Fact]
		public void Build_needs_python_and_the_tools_like_the_kit()
		{
			Assert.Equal(RemasterReason.NeedsPython, RemasterScreen.Evaluate(Inputs(feasibility: Ready with { Python = PythonGate.Missing })).BuildAndShow.Reason);
			Assert.Equal(RemasterReason.NeedsTools, RemasterScreen.Evaluate(Inputs(feasibility: Ready with { Tools = ToolsGate.Missing })).BuildAndShow.Reason);
		}

		[Fact]
		public void The_runner_takes_the_step_total_from_the_build_and_keeps_its_log()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec() with { TotalSteps = 0 });
			launcher.Last!.OnLine("steps: 4", false);
			launcher.Last.OnLine("recording: rec-002", false);
			launcher.Last.OnLine("== copy", false);
			Assert.Equal(RemasterJobStep.Copy, RemasterJobs.StepOf(runner.Snapshot.CurrentStep));
			launcher.Last.OnLine("ok   copy", false);
			launcher.Last.OnLine("== figures", false);
			Assert.Equal(RemasterJobStep.ImportFigures, RemasterJobs.StepOf(runner.Snapshot.CurrentStep));
			launcher.Last.OnLine("error: usr001-figure.png: bad", true);
			Assert.Equal(4, runner.Snapshot.TotalSteps);
			Assert.Equal(25, runner.Snapshot.Percent);
			launcher.Last.OnExit(1);

			Assert.Equal(new[] { "steps: 4", "recording: rec-002", "== copy", "ok   copy", "== figures", "error: usr001-figure.png: bad" }, runner.Log.ToArray());
		}

		[Fact]
		public void The_log_is_bounded_and_cleared_by_the_next_job()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			runner.Start(Spec());
			for(int i = 0; i < RemasterJobRunner.LogLimit + 10; i++) {
				launcher.Last!.OnLine("line " + i, false);
			}
			Assert.Equal(RemasterJobRunner.LogLimit, runner.Log.Count);
			Assert.Equal("line " + (RemasterJobRunner.LogLimit + 9), runner.Log.Last());
			launcher.Last!.OnExit(0);
			runner.Start(Spec());
			Assert.Empty(runner.Log);
		}

		[Theory]
		[InlineData("show: images", RemasterShowKind.Images)]
		[InlineData("show: reload", RemasterShowKind.Reload)]
		[InlineData("show: sideways", RemasterShowKind.None)]
		public void The_outcome_is_the_builds_show_line(string line, RemasterShowKind kind)
		{
			RemasterBuildOutcome outcome = RemasterBuildOutcome.Parse(new[] { "steps: 4", "recording: rec-003", "wrote 2 file(s)", line });
			Assert.Equal(kind, outcome.Show);
			Assert.Equal("rec-003", outcome.RecordingId);
		}

		[Theory]
		[InlineData(RemasterShowKind.Images, true, true, RemasterShowAction.ReloadImages)]
		[InlineData(RemasterShowKind.Reload, true, true, RemasterShowAction.ReloadPack)]
		[InlineData(RemasterShowKind.None, true, true, RemasterShowAction.ReloadPack)]
		[InlineData(RemasterShowKind.Images, false, true, RemasterShowAction.None)]
		[InlineData(RemasterShowKind.Images, true, false, RemasterShowAction.None)]
		public void Show_in_game_reloads_images_in_place_when_it_can(RemasterShowKind show, bool gameIsProjects, bool nes, RemasterShowAction action)
		{
			//W-X3: opening another game never stops a build, so the result is
			//shown only on the project's own game - never poured into another.
			Assert.Equal(action, RemasterShow.Decide(new RemasterBuildOutcome("rec-001", show), gameIsProjects, nes));
		}

		[Fact]
		public void Files_changed_since_the_last_build_count_paintable_kit_files_newer_than_the_stamp()
		{
			string project = Path.Combine(Path.GetTempPath(), "g6-fresh-" + Guid.NewGuid().ToString("N"));
			try {
				string kit = Path.Combine(project, "kit", "rec-001");
				Directory.CreateDirectory(Path.Combine(kit, "sheets"));
				Directory.CreateDirectory(Path.Combine(kit, "figures"));
				Directory.CreateDirectory(Path.Combine(project, "kit", "pages", "chr"));
				Assert.Null(RemasterBuildFreshness.ChangedSinceLastBuild(project, "rec-001"));

				string stamp = Path.Combine(project, "mep", RemasterBuildFreshness.StampFile);
				Directory.CreateDirectory(Path.GetDirectoryName(stamp)!);
				File.WriteAllText(stamp, "{}");
				DateTime built = DateTime.UtcNow.AddMinutes(-5);
				File.SetLastWriteTimeUtc(stamp, built);

				void Write(string rel, int minutes)
				{
					string path = Path.Combine(project, rel);
					File.WriteAllText(path, "x");
					File.SetLastWriteTimeUtc(path, built.AddMinutes(minutes));
				}
				Write("kit/rec-001/sheets/usr000.png", 1);
				Write("kit/rec-001/sheets/usr000.orig.png", 1);
				Write("kit/rec-001/sheets/usr000.json", 1);
				Write("kit/rec-001/figures/usr000-figure.png", 2);
				Write("kit/rec-001/figures/usr000-figure.ora", 2);
				Write("kit/rec-001/sheets/usr001.png", -1);
				Write("kit/pages/chr/Chr_0.png", 3);

				Assert.Equal(3, RemasterBuildFreshness.ChangedSinceLastBuild(project, "rec-001"));
			} finally {
				Directory.Delete(project, true);
			}
		}
	}
}
