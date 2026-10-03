using System;
using System.Collections.Generic;
using System.IO;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//#647-#650: Remaster and Share run their jobs on separate runners. A job is
	//tagged with its project folder so the other workspace's gate refuses a
	//second job on the same mep/ (#647) and a finished result is shown only on
	//the project it ran on (#648); a finished build reloads the pack by itself
	//only when ADR-0244 keeps the player's place (#649); quitting asks about
	//Share's job, and a cancelled import removes only the folder it created (#650).
	public class WorkspaceJobsTests
	{
		private sealed class FakeProcess : IJobProcess
		{
			public Action<int> OnExit = _ => { };
			public void Kill() => OnExit(-9);
		}

		private sealed class FakeLauncher : IJobProcessLauncher
		{
			public FakeProcess? Last;

			public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
			{
				Last = new FakeProcess { OnExit = onExit };
				return Last;
			}
		}

		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/py", Array.Empty<string>(), "3.12", ToolsGate.Found, "/tools");
		private static readonly string ProjectA = Path.Combine(Path.GetTempPath(), "jobs", "Contra (USA)");
		private static readonly string ProjectB = Path.Combine(Path.GetTempPath(), "jobs", "Castlevania (USA)");

		private static RemasterJobSnapshot Job(RemasterJobStatus status, string folder) =>
			new(status, RemasterJobKind.Build, 0, 4, "", "", "Contra (USA)", folder);

		[Fact]
		public void The_snapshot_names_the_project_the_job_runs_on_until_it_is_cleared()
		{
			FakeLauncher launcher = new();
			RemasterJobRunner runner = new(launcher);
			Assert.True(runner.Start(new RemasterJobSpec(RemasterJobKind.Pack, new[] { "/py" }, "/tools", 1, ProjectA, "Contra (USA)")));
			Assert.Equal(ProjectA, runner.Snapshot.ProjectFolder);
			launcher.Last!.OnExit(0);
			Assert.Equal(RemasterJobStatus.Succeeded, runner.Snapshot.Status);
			Assert.Equal(ProjectA, runner.Snapshot.ProjectFolder);
			runner.Clear();
			Assert.Equal("", runner.Snapshot.ProjectFolder);
		}

		[Fact]
		public void Only_a_running_job_on_the_same_folder_holds_the_other_workspace()
		{
			Assert.True(RemasterJobs.RunsOn(Job(RemasterJobStatus.Running, ProjectA), ProjectA));
			Assert.True(RemasterJobs.RunsOn(Job(RemasterJobStatus.Running, ProjectA + Path.DirectorySeparatorChar), ProjectA));
			Assert.False(RemasterJobs.RunsOn(Job(RemasterJobStatus.Running, ProjectB), ProjectA));
			Assert.False(RemasterJobs.RunsOn(Job(RemasterJobStatus.Succeeded, ProjectA), ProjectA));
			Assert.False(RemasterJobs.RunsOn(RemasterJobSnapshot.Idle, ProjectA));
			Assert.False(RemasterJobs.RunsOn(Job(RemasterJobStatus.Running, ProjectA), ""));
		}

		[Fact]
		public void A_finished_job_is_a_result_only_on_the_project_it_ran_on()
		{
			RemasterJobSnapshot running = Job(RemasterJobStatus.Running, ProjectA);
			Assert.Same(running, RemasterJobs.ShownFor(running, ProjectB));
			foreach(RemasterJobStatus status in new[] { RemasterJobStatus.Succeeded, RemasterJobStatus.Failed, RemasterJobStatus.Stopped }) {
				RemasterJobSnapshot done = Job(status, ProjectA);
				Assert.Same(done, RemasterJobs.ShownFor(done, ProjectA));
				Assert.Equal(RemasterJobSnapshot.Idle, RemasterJobs.ShownFor(done, ProjectB));
			}
		}

		[Fact]
		public void Share_build_waits_for_a_remaster_job_on_the_same_project()
		{
			ShareProjectIdentity built = new(ProjectA, "Contra (USA)", "Contra (USA)", "NES", true, true);
			Assert.Equal(ShareBuildReason.RemasterJobRunning, ShareProjectPackage.BuildReason(built, true, Ready, false, remasterJobOnProject: true));
			//Share's own job is the card's, named first.
			Assert.Equal(ShareBuildReason.JobRunning, ShareProjectPackage.BuildReason(built, true, Ready, true, remasterJobOnProject: true));
			Assert.Equal(ShareBuildReason.None, ShareProjectPackage.BuildReason(built, true, Ready, false, remasterJobOnProject: false));
		}

		[Fact]
		public void Remaster_jobs_wait_for_shares_job_on_the_same_project_but_recording_does_not()
		{
			RemasterInputs inputs = new(true, ConsoleType.Nes, false, false, ProjectA, true, 1, Ready, true, HasKit: true, ShareJobOnProject: true);
			RemasterScreenState s = RemasterScreen.Evaluate(inputs);
			Assert.Equal(RemasterControl.Off(RemasterReason.ShareJobRunning), s.BuildAndShow);
			Assert.Equal(RemasterControl.Off(RemasterReason.ShareJobRunning), s.PrepareFigures);
			Assert.Equal(RemasterControl.On, s.Record);
			Assert.False(RemasterScreen.RunKitAfterRecording(inputs));
			//A reason that comes first still names itself.
			Assert.Equal(RemasterControl.Off(RemasterReason.NoKitYet), RemasterScreen.Evaluate(inputs with { HasKit = false }).BuildAndShow);
			Assert.Equal(RemasterControl.On, RemasterScreen.Evaluate(inputs with { ShareJobOnProject = false }).BuildAndShow);
		}

		[Theory]
		[InlineData(false, false, true, InterruptionKind.QuitWhilePackaging)]
		[InlineData(false, true, true, InterruptionKind.QuitWhileJob)]
		[InlineData(true, false, true, InterruptionKind.QuitWhileRecording)]
		[InlineData(false, false, false, InterruptionKind.None)]
		public void Quitting_asks_while_shares_job_runs(bool recording, bool job, bool packaging, InterruptionKind kind)
		{
			Assert.Equal(kind, Interruptions.ForQuit(recording, job, packaging));
		}

		[Fact]
		public void Quitting_while_packaging_is_a_quit()
		{
			Assert.True(Interruptions.Quits(InterruptionKind.QuitWhilePackaging));
		}

		[Fact]
		public void A_cancelled_import_removes_only_the_folder_it_created()
		{
			string dest = Path.Combine("/packs", "Contra80s (editable)");
			Assert.Equal(dest, RemasterHandOff.PartialImportToRemove(dest, existedBefore: false, RemasterJobStatus.Stopped));
			Assert.Equal("", RemasterHandOff.PartialImportToRemove(dest, existedBefore: true, RemasterJobStatus.Stopped));
			Assert.Equal("", RemasterHandOff.PartialImportToRemove(dest, existedBefore: false, RemasterJobStatus.Succeeded));
			Assert.Equal("", RemasterHandOff.PartialImportToRemove(dest, existedBefore: false, RemasterJobStatus.Running));
			Assert.Equal("", RemasterHandOff.PartialImportToRemove("", existedBefore: false, RemasterJobStatus.Stopped));
		}

		[Fact]
		public void A_finished_build_reloads_the_pack_by_itself_only_in_place()
		{
			Assert.Equal(RemasterShowAction.ReloadPack, RemasterShow.PackReload(PackChangePolicy.Plan(ConsoleType.Nes, false, false)));
			Assert.Equal(RemasterShowAction.None, RemasterShow.PackReload(PackChangePolicy.Plan(ConsoleType.Nes, movieActive: true, netplayActive: false)));
			Assert.Equal(RemasterShowAction.None, RemasterShow.PackReload(PackChangePolicy.Plan(ConsoleType.Nes, movieActive: false, netplayActive: true)));
			Assert.Equal(RemasterShowAction.None, RemasterShow.PackReload(PackChangePolicy.Plan(ConsoleType.Gba, false, false)));
		}
	}
}
