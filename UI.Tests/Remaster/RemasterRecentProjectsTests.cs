using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//W-R0 "Recent projects" (PRD Part B §13.5.3, ADR-0243 Decision 1): the
	//list is derived from the folders Remaster showed and the recent games'
	//enhancement folders; a folder that is gone or not a project is dropped.
	public sealed class RemasterRecentProjectsTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-recent-" + Guid.NewGuid().ToString("N"));

		public RemasterRecentProjectsTests()
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

		//A project folder with `recordings` auto/rec-NNN/ recordings.
		private string Project(string relative, int recordings)
		{
			string folder = Path.Combine(_root, relative);
			Directory.CreateDirectory(Path.Combine(folder, "auto"));
			for(int i = 1; i <= recordings; i++) {
				string tex = Path.Combine(folder, "auto", RemasterProjectReader.FormatId(i), "textures");
				Directory.CreateDirectory(tex);
				File.WriteAllText(Path.Combine(tex, "hires.txt"), "<ver>107\n");
			}
			return folder;
		}

		private string Rom(string relative)
		{
			string path = Path.Combine(_root, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, "");
			return path;
		}

		private string Packs => Path.Combine(_root, "EnhancementPacks");

		[Fact]
		public void Shown_projects_come_first_then_the_recent_games_projects()
		{
			string castlevania = Project("roms/Castlevania (USA)", 3);
			string zelda = Project("EnhancementPacks/The Legend of Zelda (USA)", 1);
			string opened = Project("art/Contra (USA) (editable)", 0);
			List<string> roms = new() {
				Rom("roms/Castlevania (USA).nes"),
				//read-only ROM folder: the project is EnhancementPacks/<Game>/
				Rom("ro/The Legend of Zelda (USA).zip"),
				//played, never recorded: no project, no row
				Rom("roms/Metroid (USA).nes"),
			};

			IReadOnlyList<RemasterRecentProject> rows = RemasterRecentProjects.List(new List<string> { opened }, roms, Packs);

			Assert.Equal(new[] { opened, castlevania, zelda }, rows.Select(r => r.Folder));
			Assert.Equal(new[] { "Contra (USA) (editable)", "Castlevania (USA)", "The Legend of Zelda (USA)" }, rows.Select(r => r.Name));
			Assert.Equal(new[] { 0, 3, 1 }, rows.Select(r => r.Recordings));
		}

		[Fact]
		public void The_name_is_project_json_s_when_it_has_one()
		{
			string folder = Project("roms/cv", 1);
			File.WriteAllText(Path.Combine(folder, "project.json"), "{\"name\": \"Castlevania (USA)\", \"recordings\": []}");

			Assert.Equal("Castlevania (USA)", RemasterRecentProjects.List(new List<string> { folder }, new List<string>(), Packs).Single().Name);
		}

		[Fact]
		public void A_folder_that_is_gone_or_no_longer_a_project_is_dropped()
		{
			string gone = Path.Combine(_root, "gone");
			string plain = Path.Combine(_root, "plain");
			Directory.CreateDirectory(plain);
			string kept = Project("kept", 2);

			IReadOnlyList<RemasterRecentProject> rows = RemasterRecentProjects.List(new List<string> { gone, plain, "", kept }, new List<string> { Path.Combine(_root, "missing.nes") }, Packs);

			Assert.Equal(new[] { kept }, rows.Select(r => r.Folder));
		}

		[Fact]
		public void A_project_both_shown_and_played_lists_once()
		{
			string project = Project("roms/Castlevania (USA)", 1);
			string rom = Rom("roms/Castlevania (USA).nes");

			IReadOnlyList<RemasterRecentProject> rows = RemasterRecentProjects.List(new List<string> { project + Path.DirectorySeparatorChar, project }, new List<string> { rom }, Packs);

			Assert.Single(rows);
		}

		[Fact]
		public void The_list_is_capped()
		{
			List<string> remembered = Enumerable.Range(1, 8).Select(i => Project("p" + i, 1)).ToList();

			IReadOnlyList<RemasterRecentProject> rows = RemasterRecentProjects.List(remembered, new List<string>(), Packs);

			Assert.Equal(RemasterRecentProjects.MaxShown, rows.Count);
			Assert.Equal(remembered.Take(RemasterRecentProjects.MaxShown), rows.Select(r => r.Folder));
		}

		[Fact]
		public void Remember_moves_the_folder_to_the_front_once_and_caps_the_list()
		{
			List<string> mru = new() { "/a", "/b", "/c" };

			Assert.Equal(new[] { "/b", "/a", "/c" }, RemasterRecentProjects.Remember(mru, "/b"));
			Assert.Same(mru, RemasterRecentProjects.Remember(mru, "/a"));
			Assert.Same(mru, RemasterRecentProjects.Remember(mru, ""));

			List<string> full = Enumerable.Range(0, RemasterRecentProjects.MaxRemembered).Select(i => "/p" + i).ToList();
			List<string> next = RemasterRecentProjects.Remember(full, "/new");
			Assert.Equal(RemasterRecentProjects.MaxRemembered, next.Count);
			Assert.Equal("/new", next[0]);
			Assert.DoesNotContain("/p" + (RemasterRecentProjects.MaxRemembered - 1), next);
		}

		[Theory]
		[InlineData("/roms/Contra (USA).nes", "/roms/Contra (USA)")]
		[InlineData("/roms/Zelda.zip", "/roms/Zelda")]
		[InlineData("", "")]
		public void The_sibling_is_the_rom_s_folder_plus_its_name_as_the_core_computes_it(string rom, string expected)
		{
			Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), RemasterRecentProjects.SiblingOf(rom.Replace('/', Path.DirectorySeparatorChar)));
		}
	}
}
