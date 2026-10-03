using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.7 (W-R6, W-R7): which folder is a finished pack, the import's argv and
	//refusals, and the composer's readiness.
	public sealed class RemasterHandOffTests
	{
		private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/env", new[] { "python3" }, "3.12", ToolsGate.Found, "/tools");

		private static bool AllTools(string path) => path.StartsWith("/tools/", StringComparison.Ordinal);

		[Fact]
		public void A_folder_with_hires_txt_is_a_finished_pack_and_a_recorded_folder_is_a_project()
		{
			using KitFixture f = new();
			Assert.Equal(RemasterFolderKind.Neither, RemasterHandOff.Classify(f.Project));
			f.Write("textures/hires.txt", "<ver>106\n");
			Assert.Equal(RemasterFolderKind.FinishedPack, RemasterHandOff.Classify(f.Project));
			f.Write(".bootstrap", "sha1");
			Assert.Equal(RemasterFolderKind.Project, RemasterHandOff.Classify(f.Project));
		}

		[Fact]
		public void A_patched_pack_is_one_whose_manifest_ships_a_patch_line()
		{
			using KitFixture f = new();
			f.Write("hires.txt", "<ver>106\n<patch>a.ips,0123456789ABCDEF0123456789ABCDEF01234567\n");
			Assert.True(RemasterHandOff.IsPatchedPack(f.Project));
		}

		[Fact]
		public void The_project_is_written_next_to_the_pack_and_never_over_an_existing_folder()
		{
			HashSet<string> taken = new() { Path.Combine("/packs", "Contra80s (editable)") };
			Assert.Equal(Path.Combine("/packs", "Contra80s (editable) 2"), RemasterHandOff.ImportDestination("/packs/Contra80s/", taken.Contains));
			Assert.Equal(Path.Combine("/packs", "Zelda (editable)"), RemasterHandOff.ImportDestination("/packs/Zelda", taken.Contains));
		}

		[Fact]
		public void The_import_argv_passes_the_stock_rom_only_for_a_patched_pack()
		{
			RemasterJobSpec plain = RemasterHandOff.ImportJob(Ready, "/packs/A", "/packs/A (editable)", "/roms/a.nes", false, "A");
			Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "mep_import.py"), "import", "/packs/A", "--out", "/packs/A (editable)" }, plain.Argv.ToArray());
			Assert.Equal(RemasterJobKind.Import, plain.Kind);
			RemasterJobSpec patched = RemasterHandOff.ImportJob(Ready, "/packs/A", "/out", "/roms/a.nes", true, "A");
			Assert.Equal(new[] { "--rom", "/roms/a.nes" }, patched.Argv.TakeLast(2).ToArray());
		}

		[Theory]
		[InlineData(PythonGate.Missing, ToolsGate.Found, true, false, false, false, RemasterHandOffReason.NeedsPython)]
		[InlineData(PythonGate.Found, ToolsGate.Missing, true, false, false, false, RemasterHandOffReason.NeedsTools)]
		[InlineData(PythonGate.Found, ToolsGate.Found, false, false, false, false, RemasterHandOffReason.ToolMissing)]
		[InlineData(PythonGate.Found, ToolsGate.Found, true, true, false, false, RemasterHandOffReason.JobRunning)]
		[InlineData(PythonGate.Found, ToolsGate.Found, true, false, true, false, RemasterHandOffReason.NeedsStockRom)]
		[InlineData(PythonGate.Found, ToolsGate.Found, true, false, true, true, RemasterHandOffReason.None)]
		[InlineData(PythonGate.Found, ToolsGate.Found, true, false, false, false, RemasterHandOffReason.None)]
		public void Make_editable_says_why_it_cannot_run(PythonGate python, ToolsGate tools, bool hasScript, bool job, bool patched, bool game, RemasterHandOffReason expected)
		{
			RemasterHandOffControl c = RemasterHandOff.ImportControl(Ready with { Python = python, Tools = tools }, p => hasScript && AllTools(p), job, false, patched, game);
			Assert.Equal(expected, c.Reason);
			Assert.Equal(expected == RemasterHandOffReason.None, c.Enabled);
		}

		[Fact]
		public void A_refusal_with_a_cited_line_keeps_the_file_and_line_and_drops_the_prefix()
		{
			RemasterImportRefusal r = RemasterHandOff.Refusal("error: /packs/A/hires.txt:12: unknown tag <foo>; refusing it rather than dropping it");
			Assert.Equal(("/packs/A/hires.txt", 12), (r.File, r.Line));
			Assert.Equal("unknown tag <foo>; refusing it rather than dropping it", r.Sentence);

			RemasterImportRefusal plain = RemasterHandOff.Refusal("error: /packs/A: no hires.txt found");
			Assert.Equal(("", 0), (plain.File, plain.Line));
			Assert.Equal("/packs/A: no hires.txt found", plain.Sentence);
		}

		[Fact]
		public void Show_line_reads_the_cited_manifest_line()
		{
			using KitFixture f = new();
			string hires = f.Write("hires.txt", "<ver>106\n<foo>bar\n");
			Assert.Equal("<foo>bar", RemasterHandOff.CitedText(new RemasterImportRefusal("x", hires, 2)));
			Assert.Equal("", RemasterHandOff.CitedText(new RemasterImportRefusal("x", hires, 9)));
		}

		private static RemasterProjectInfo Project(KitFixture f, params bool[] textured)
		{
			List<RemasterRecording> recs = new();
			for(int i = 0; i < textured.Length; i++) {
				string id = RemasterProjectReader.FormatId(i + 1);
				recs.Add(new RemasterRecording(id, Path.Combine(f.Project, "auto", id), textured[i], false, "play", null, null, ""));
			}
			return new RemasterProjectInfo(f.Project, "Contra", recs, false, "");
		}

		[Fact]
		public void The_composer_opens_the_newest_recording_that_holds_textures()
		{
			using KitFixture f = new();
			RemasterProjectInfo p = Project(f, true, true, false);
			Assert.Equal("rec-002", RemasterHandOff.ComposeRecording(p)!.Id);
			IReadOnlyList<string> argv = RemasterHandOff.ComposeArgv(Ready, RemasterHandOff.ComposeRecording(p)!, "/roms/c.nes");
			Assert.Equal(new[] { "/usr/bin/env", "python3", Path.Combine("/tools", "compose_editor.py"), Path.Combine(f.Project, "auto", "rec-002"), "--rom", "/roms/c.nes" }, argv.ToArray());
			Assert.DoesNotContain("--rom", RemasterHandOff.ComposeArgv(Ready, RemasterHandOff.ComposeRecording(p)!, ""));
		}

		[Fact]
		public void Compose_is_disabled_without_layout_data_and_enabled_with_it()
		{
			using KitFixture f = new();
			RemasterProjectInfo p = Project(f, true);
			Func<string, bool> exists = path => AllTools(path) || File.Exists(path);
			Assert.Equal(RemasterHandOffReason.NothingRecorded, RemasterHandOff.ComposeControl(Project(f), Ready, exists, false).Reason);
			Assert.Equal(RemasterHandOffReason.NoLayoutData, RemasterHandOff.ComposeControl(p, Ready, exists, false).Reason);
			f.Write("auto/rec-001/textures/sheets/adjacency.json", "{}");
			Assert.True(RemasterHandOff.ComposeControl(p, Ready, exists, false).Enabled);
			Assert.Equal(RemasterHandOffReason.RecordingRunning, RemasterHandOff.ComposeControl(p, Ready, exists, true).Reason);
			Assert.Equal(RemasterHandOffReason.ToolMissing, RemasterHandOff.ComposeControl(p, Ready, File.Exists, false).Reason);
		}
	}
}
