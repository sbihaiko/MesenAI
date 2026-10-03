using System;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.3 (ADR-0243 Q1/Q2): the recordings list W-R1 shows, read without Python.
	//The cases mirror scripts/test_mep_project.py's layout rules.
	public sealed class RemasterProjectReaderTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-g3-" + Guid.NewGuid().ToString("N"));

		public RemasterProjectReaderTests()
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

		private string Touch(params string[] parts)
		{
			string path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, "");
			return path;
		}

		[Theory]
		[InlineData("rec-001", 1)]
		[InlineData("rec-012", 12)]
		[InlineData("rec-123456", 123456)]
		[InlineData("rec-000", 0)]
		[InlineData("rec-1234567", 0)]
		[InlineData("rec-", 0)]
		[InlineData("rec-01a", 0)]
		[InlineData("textures", 0)]
		public void Recording_ids_parse_as_the_core_and_mep_project_do(string name, int expected)
		{
			Assert.Equal(expected, RemasterProjectReader.RecordingNumber(name));
		}

		[Fact]
		public void A_folder_with_auto_or_the_stamp_is_a_project_and_an_empty_one_is_not()
		{
			Assert.False(RemasterProjectReader.IsProjectFolder(_root));
			Directory.CreateDirectory(Path.Combine(_root, "auto"));
			Assert.True(RemasterProjectReader.IsProjectFolder(_root));

			string other = Path.Combine(_root, "other");
			Directory.CreateDirectory(other);
			File.WriteAllText(Path.Combine(other, ".bootstrap"), "sha1=x\n");
			Assert.True(RemasterProjectReader.IsProjectFolder(other));
		}

		//W-P6: the automatic upscale's byline names the scaler it was made with,
		//read from the `.bootstrap` stamp MepPackManager writes.
		[Fact]
		public void The_automatic_upscales_scaler_comes_from_the_bootstrap_stamp()
		{
			File.WriteAllText(Path.Combine(_root, ".bootstrap"),
				"generator=mesence-bootstrap/1\nsha1=abc\nrom=Contra (USA)\nfilter=xBRZ\nscale=4\n");

			Assert.Equal("xBRZ 4×", RemasterProjectReader.BootstrapScaler(_root));
		}

		[Fact]
		public void No_stamp_or_no_filter_or_scale_key_names_no_scaler()
		{
			Assert.Equal("", RemasterProjectReader.BootstrapScaler(_root));

			File.WriteAllText(Path.Combine(_root, ".bootstrap"), "filter=xBRZ\n");
			Assert.Equal("", RemasterProjectReader.BootstrapScaler(_root));
		}

		[Fact]
		public void Recordings_are_listed_in_id_order_with_the_manifest_metadata()
		{
			Touch("auto", "rec-002", "textures", "hires.txt");
			Touch("auto", "rec-010", "audio", "fingerprints.json");
			Touch("auto", "rec-001", "textures", "hires.txt");
			Directory.CreateDirectory(Path.Combine(_root, "auto", "notes"));
			File.WriteAllText(Path.Combine(_root, "project.json"), """
				{"name": "Contra (USA)", "recordings": [
				  {"id": "rec-001", "recordedAt": "2026-10-02T12:00:00Z", "source": "play", "durationSeconds": 102.5, "note": "stage 1"},
				  {"id": "rec-002", "source": "rocket", "durationSeconds": "long"},
				  {"id": "rec-099", "source": "tas"}
				]}
				""");

			RemasterProjectInfo info = RemasterProjectReader.Read(_root);

			Assert.Equal("Contra (USA)", info.Name);
			Assert.Equal(new[] { "rec-001", "rec-002", "rec-010" }, info.Recordings.Select(r => r.Id).ToArray());
			RemasterRecording first = info.Recordings[0];
			Assert.Equal("play", first.Source);
			Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), first.RecordedAtUtc);
			Assert.Equal(102.5, first.DurationSeconds);
			Assert.Equal("stage 1", first.Note);
			//An unknown source or a non-number duration reads as unset, as in Python.
			Assert.Null(info.Recordings[1].Source);
			Assert.Null(info.Recordings[1].DurationSeconds);
			Assert.True(info.Recordings[2].HasAudio);
			Assert.False(info.Recordings[2].HasTextures);
			Assert.Equal(2, info.TexturedRecordingCount);
			Assert.Equal("", info.Problem);
		}

		[Fact]
		public void Without_a_manifest_the_list_is_derived_from_the_folders_and_named_after_the_folder()
		{
			string project = Path.Combine(_root, "Castlevania (USA)");
			Directory.CreateDirectory(Path.Combine(project, "auto", "rec-003", "textures"));
			File.WriteAllText(Path.Combine(project, "auto", "rec-003", "textures", "hires.txt"), "");

			RemasterProjectInfo info = RemasterProjectReader.Read(project);

			Assert.Equal("Castlevania (USA)", info.Name);
			Assert.Equal("rec-003", Assert.Single(info.Recordings).Id);
			Assert.Null(info.Recordings[0].Source);
		}

		[Fact]
		public void A_bare_auto_textures_reads_as_rec_001_without_being_moved()
		{
			Touch("auto", "textures", "hires.txt");
			Touch("auto", "rec-002", "textures", "hires.txt");

			RemasterProjectInfo info = RemasterProjectReader.Read(_root);

			Assert.Equal(new[] { "rec-001", "rec-002" }, info.Recordings.Select(r => r.Id).ToArray());
			Assert.Equal(Path.Combine(_root, "auto"), info.Recordings[0].Path);
		}

		[Fact]
		public void A_bare_layout_and_rec_001_together_name_the_problem()
		{
			Touch("auto", "textures", "hires.txt");
			Touch("auto", "rec-001", "textures", "hires.txt");

			RemasterProjectInfo info = RemasterProjectReader.Read(_root);

			Assert.Contains("rec-001", info.Problem);
		}

		[Fact]
		public void A_broken_manifest_still_lists_the_folders_and_says_why_metadata_is_missing()
		{
			Touch("auto", "rec-001", "textures", "hires.txt");
			File.WriteAllText(Path.Combine(_root, "project.json"), "{not json");

			RemasterProjectInfo info = RemasterProjectReader.Read(_root);

			Assert.Single(info.Recordings);
			Assert.Contains("project.json", info.Problem);
		}

		[Fact]
		public void An_empty_kit_folder_is_not_a_kit()
		{
			Directory.CreateDirectory(Path.Combine(_root, "kit"));
			Assert.False(RemasterProjectReader.Read(_root).HasKit);
			Touch("kit", "pages", "index.html");
			Assert.True(RemasterProjectReader.Read(_root).HasKit);
		}

		[Fact]
		public void The_games_project_is_the_sibling_else_the_enhancement_packs_fallback()
		{
			string sibling = Path.Combine(_root, "roms", "Contra (USA)");
			string packs = Path.Combine(_root, "EnhancementPacks");
			Assert.Equal("", RemasterProjectLocator.ForGame(sibling, packs));

			Directory.CreateDirectory(Path.Combine(packs, "Contra (USA)", "auto"));
			Assert.Equal(Path.Combine(packs, "Contra (USA)"), RemasterProjectLocator.ForGame(sibling, packs));

			Directory.CreateDirectory(Path.Combine(sibling, "auto"));
			Assert.Equal(sibling, RemasterProjectLocator.ForGame(sibling, packs));
		}

		[Fact]
		public void A_recording_folder_names_its_project()
		{
			string project = Path.Combine(_root, "Contra (USA)");
			Assert.Equal(project, RemasterProjectLocator.FromRecordingFolder(Path.Combine(project, "auto", "rec-004")));
			Assert.Equal("", RemasterProjectLocator.FromRecordingFolder(Path.Combine(project, "rec-004")));
			Assert.Equal("", RemasterProjectLocator.FromRecordingFolder(""));
			Assert.True(RemasterProjectLocator.SameFolder(project, project + Path.DirectorySeparatorChar));
		}
	}
}
