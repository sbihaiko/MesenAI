using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.ViewModels
{
	//W-R0 › Recent projects (PRD Part B §13.5.3): one row per project the
	//artist worked on (UI/Logic/RemasterRecentProjects.cs); a click opens it
	//through OpenProjectFolder, the path Choose Folder… takes. Read only while
	//W-R0 shows, so W-R1 never pays for it.
	public partial class RemasterWorkspaceViewModel
	{
		[ObservableProperty] public partial List<RemasterRecentProjectRow> RecentProjects { get; private set; } = new();
		[ObservableProperty] public partial bool HasRecentProjects { get; private set; }

		//The recent games (Play's list) and the packs folder whose projects
		//list too; a test swaps them for fixtures.
		public Func<IReadOnlyList<string>> RecentRomPaths { get; set; } = () => ConfigManager.Config.RecentFiles.Items.Select(i => i.RomFile.Path).ToList();
		public Func<string> RecentPacksFolder { get; set; } = () => ConfigManager.EnhancementPackFolder;

		private void RefreshRecentProjects()
		{
			if(IsProject && _project != null) {
				_config.RecentProjects = RemasterRecentProjects.Remember(_config.RecentProjects, _project.Folder);
			}
			if(!IsNoProject) {
				return;
			}
			List<RemasterRecentProjectRow> rows = RemasterRecentProjects.List(_config.RecentProjects, RecentRomPaths(), RecentPacksFolder())
				.Select(RemasterRecentProjectRow.From).ToList();
			//Unchanged rows keep their buttons (and keyboard focus) across a Refresh.
			if(!rows.SequenceEqual(RecentProjects)) {
				RecentProjects = rows;
			}
			HasRecentProjects = RecentProjects.Count > 0;
		}

		public bool OpenRecentProject(RemasterRecentProjectRow row) => OpenProjectFolder(row.Folder);
	}

	//One row of W-R0's Recent projects: the game's name and its recordings.
	public sealed record RemasterRecentProjectRow(string Folder, string Name, string Detail)
	{
		public static RemasterRecentProjectRow From(RemasterRecentProject p)
		{
			string detail = p.Recordings == 0 ? ResourceHelper.GetMessage("RemasterRecentNoRecordings")
				: p.Recordings == 1 ? ResourceHelper.GetMessage("RemasterRecordingsOne")
				: ResourceHelper.GetMessage("RemasterRecordingsMany", p.Recordings);
			return new RemasterRecentProjectRow(p.Folder, p.Name, detail);
		}
	}
}
