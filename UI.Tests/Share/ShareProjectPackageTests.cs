using System;
using System.IO;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Share
{
	//G.8 (PRD Part B §13.5.4 W-H3, ADR-0243): sharing your own project. Step 1
	//packs the project's human layer (mep/) with `mep_build.py pack` as a job;
	//step 3 sends Game/Console taken from the project, not typed.
	public class ShareProjectPackageTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-g8-" + Guid.NewGuid().ToString("N"));

		public ShareProjectPackageTests()
		{
			Directory.CreateDirectory(_root);
		}

		public void Dispose()
		{
			try {
				Directory.Delete(_root, true);
			} catch(IOException) {
			}
		}

		private string Project(string name, bool built = true, string? packJson = null, string? stamp = null)
		{
			string folder = Path.Combine(_root, name);
			Directory.CreateDirectory(Path.Combine(folder, "auto", "rec-001", "textures"));
			File.WriteAllText(Path.Combine(folder, "auto", "rec-001", "textures", "hires.txt"), "<ver>107\n");
			if(built) {
				Directory.CreateDirectory(Path.Combine(folder, "mep", "textures"));
				File.WriteAllText(Path.Combine(folder, "mep", "textures", "hires.txt"), "<ver>107\n");
			}
			if(packJson != null) {
				Directory.CreateDirectory(Path.Combine(folder, "mep"));
				File.WriteAllText(Path.Combine(folder, "mep", "pack.json"), packJson);
			}
			if(stamp != null) {
				File.WriteAllText(Path.Combine(folder, ".bootstrap"), stamp);
			}
			return folder;
		}

		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

		[Theory]
		[InlineData("Contra (USA)", "contra-usa-mep.zip")]
		[InlineData("Mega Man 2 (USA) [Rev A]", "mega-man-2-usa-rev-a-mep.zip")]
		[InlineData("  ", "project-mep.zip")]
		public void The_zip_is_named_after_the_project(string name, string zip)
		{
			Assert.Equal(zip, ShareProjectPackage.ZipFileName(name));
		}

		[Fact]
		public void Game_and_console_come_from_the_stamp_and_the_pack_target()
		{
			string folder = Project("contra", packJson: "{\"targets\": [{\"system\": \"nes\", \"sha1\": \"AB\"}]}",
				stamp: "generator=mesence-bootstrap/1\nsha1=00\nrom=Contra (USA)\nfilter=xBRZ\n");
			ShareProjectIdentity p = ShareProjectPackage.Read(folder);
			Assert.Equal("contra", p.Name);
			Assert.Equal("Contra (USA)", p.Game);
			Assert.Equal("NES", p.ConsoleOption);
			Assert.True(p.HasTargets);
			Assert.True(p.HasBuiltLayer);
			Assert.Equal(Path.Combine(folder, "mep"), p.PackFolder);
			Assert.Equal(Path.Combine(folder, "contra-mep.zip"), p.ZipPath);
		}

		[Fact]
		public void Without_a_stamp_or_a_target_the_game_is_the_project_name_and_the_console_is_unknown()
		{
			ShareProjectIdentity p = ShareProjectPackage.Read(Project("Contra (USA)", packJson: "{ not json"));
			Assert.Equal("Contra (USA)", p.Game);
			Assert.Equal("", p.ConsoleOption);
			Assert.False(p.HasTargets);
		}

		[Fact]
		public void Build_is_disabled_with_its_reason_until_it_can_run()
		{
			ShareProjectIdentity built = ShareProjectPackage.Read(Project("a"));
			ShareProjectIdentity notBuilt = ShareProjectPackage.Read(Project("b", built: false));
			Assert.Equal(ShareBuildReason.NothingBuilt, ShareProjectPackage.BuildReason(notBuilt, true, Ready, false));
			//No pack.json target: only the running game's own project can give mep_build --rom.
			Assert.Equal(ShareBuildReason.NeedsGame, ShareProjectPackage.BuildReason(built, false, Ready, false));
			Assert.Equal(ShareBuildReason.None, ShareProjectPackage.BuildReason(built, true, Ready, false));
			Assert.Equal(ShareBuildReason.NeedsPython, ShareProjectPackage.BuildReason(built, true, Ready with { Python = PythonGate.TooOld }, false));
			Assert.Equal(ShareBuildReason.NeedsTools, ShareProjectPackage.BuildReason(built, true, Ready with { Tools = ToolsGate.Missing }, false));
			Assert.Equal(ShareBuildReason.JobRunning, ShareProjectPackage.BuildReason(built, true, Ready, true));
			ShareProjectIdentity targeted = ShareProjectPackage.Read(Project("c", packJson: "{\"targets\": [{\"system\": \"nes\", \"sha1\": \"AB\"}]}"));
			Assert.Equal(ShareBuildReason.None, ShareProjectPackage.BuildReason(targeted, false, Ready, false));
		}

		[Theory]
		[InlineData("{\"complete\": false}", false)]
		[InlineData("{\"complete\": true}", true)]
		[InlineData("{\"recording\": \"rec-001\"}", true)]
		public void A_first_build_stopped_mid_sync_is_not_a_built_layer(string buildStamp, bool hasBuiltLayer)
		{
			//#659: a half-written mep/ carries the `complete: false` claim of
			//mep_project_build; packing it would share a broken pack.
			string folder = Project("d");
			File.WriteAllText(Path.Combine(folder, "mep", RemasterBuildFreshness.StampFile), buildStamp);
			Assert.Equal(hasBuiltLayer, ShareProjectPackage.Read(folder).HasBuiltLayer);
		}

		[Fact]
		public void The_job_is_mep_build_pack_of_mep_into_the_project_root()
		{
			ShareProjectIdentity p = ShareProjectPackage.Read(Project("Contra (USA)"));
			RemasterJobSpec spec = ShareProjectPackage.PackJob(new PythonCandidate("/usr/bin/env", new[] { "python3" }), "/tools", p, "/roms/Contra (USA).nes");
			Assert.Equal(RemasterJobKind.Pack, spec.Kind);
			Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "mep_build.py"), "pack", p.PackFolder,
				"--out", p.ZipPath, "--rom", "/roms/Contra (USA).nes", "--quiet" }, spec.Argv);
			//No ROM: the existing pack.json target is kept.
			Assert.DoesNotContain("--rom", ShareProjectPackage.PackJob(new PythonCandidate("python3", Array.Empty<string>()), "/tools", p, "").Argv);
		}

		[Fact]
		public void Known_projects_are_existing_project_folders_listed_once()
		{
			string a = Project("a");
			string missing = Path.Combine(_root, "gone");
			Assert.Equal(new[] { a }, ShareProjectPackage.KnownProjects(new[] { a, "", missing, a + Path.DirectorySeparatorChar }).ToArray());
		}

		[Theory]
		[InlineData(500L, "1 KB")]
		[InlineData(4096L, "4 KB")]
		[InlineData(38L * 1024 * 1024 + 1000, "38 MB")]
		public void The_size_reads_in_kb_or_mb(long bytes, string text)
		{
			Assert.Equal(text, ShareProjectPackage.FormatSize(bytes));
		}
	}

	public class ShareScreenTests
	{
		[Theory]
		[InlineData(ConsoleType.Nes, true)]
		[InlineData(ConsoleType.Gameboy, true)]
		[InlineData(ConsoleType.Sms, true)]
		[InlineData(ConsoleType.Gba, true)]
		[InlineData(ConsoleType.Ws, false)]
		public void Replay_consoles_mirror_the_cores_share_settings(ConsoleType console, bool supported)
		{
			Assert.Equal(supported, ShareReplay.IsSupported(console));
		}

		[Fact]
		public void Start_recording_says_why_it_cannot_start()
		{
			Assert.Equal(ReplayStartReason.NoGame, ShareReplay.StartReason(false, ConsoleType.Nes, false, false));
			Assert.Equal(ReplayStartReason.ConsoleNotSupported, ShareReplay.StartReason(true, ConsoleType.Ws, false, false));
			Assert.Equal(ReplayStartReason.MovieBusy, ShareReplay.StartReason(true, ConsoleType.Nes, true, false));
			Assert.Equal(ReplayStartReason.Netplay, ShareReplay.StartReason(true, ConsoleType.Nes, false, true));
			Assert.Equal(ReplayStartReason.None, ShareReplay.StartReason(true, ConsoleType.Nes, false, false));
		}

		[Fact]
		public void Esc_stops_a_recording_closes_the_topmost_panel_and_never_navigates()
		{
			Assert.Equal(ShareEscAction.StopRecording, ShareEsc.Next(ReplaySheet.Recording, true));
			Assert.Equal(ShareEscAction.CloseSheet, ShareEsc.Next(ReplaySheet.Start, false));
			Assert.Equal(ShareEscAction.CloseSheet, ShareEsc.Next(ReplaySheet.Saved, true));
			Assert.Equal(ShareEscAction.CloseProjectList, ShareEsc.Next(ReplaySheet.None, true));
			Assert.Equal(ShareEscAction.None, ShareEsc.Next(ReplaySheet.None, false));
		}
	}
}
