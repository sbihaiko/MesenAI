using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

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

		//ADR-0252 §2: each listed project's painted cells, measured off the UI
		//thread (the same per-tile cache W-R1 uses); a folder not measured yet
		//shows its recordings alone.
		private readonly Dictionary<string, int?> _recentPainted = new(StringComparer.Ordinal);
		private int _recentGeneration;

		//Completes when the rows' painted-cell counts the last refresh asked for are in.
		public Task RecentSettled { get; private set; } = Task.CompletedTask;

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
			IReadOnlyList<RemasterRecentProject> projects = RemasterRecentProjects.List(_config.RecentProjects, RecentRomPaths(), RecentPacksFolder());
			List<RemasterRecentProjectRow> rows = projects
				.Select(p => RemasterRecentProjectRow.From(p, _recentPainted.TryGetValue(p.Folder, out int? n) ? n : null)).ToList();
			//Unchanged rows keep their buttons (and keyboard focus) across a Refresh.
			if(!rows.SequenceEqual(RecentProjects)) {
				RecentProjects = rows;
			}
			HasRecentProjects = RecentProjects.Count > 0;
			MeasureRecentProjects(projects);
		}

		private void MeasureRecentProjects(IReadOnlyList<RemasterRecentProject> projects)
		{
			int generation = ++_recentGeneration;
			if(projects.Count == 0) {
				RecentSettled = Task.CompletedTask;
				return;
			}
			TaskCompletionSource settled = new();
			RecentSettled = settled.Task;
			Task.Run(() => projects.Select(p => (p.Folder, _cellCache.Total(RemasterKitReader.Read(p.Folder)))).ToList()).ContinueWith(t => Dispatcher.UIThread.Post(() => {
				bool changed = false;
				if(generation == _recentGeneration && t.IsCompletedSuccessfully) {
					foreach((string folder, int? cells) in t.Result) {
						if(!_recentPainted.TryGetValue(folder, out int? old) || old != cells) {
							_recentPainted[folder] = cells;
							changed = true;
						}
					}
				}
				if(changed && IsNoProject) {
					//The rows only; the next measurement finds every count unchanged.
					RecentProjects = RecentProjects.Select(r => r with { Detail = RemasterRecentProjectRow.DetailOf(r.Recordings, _recentPainted.TryGetValue(r.Folder, out int? n) ? n : null) }).ToList();
				}
				settled.TrySetResult();
			}), TaskScheduler.Default);
		}

		public bool OpenRecentProject(RemasterRecentProjectRow row) => OpenProjectFolder(row.Folder);
	}

	//One row of W-R0's Recent projects: the game's name, its recordings and
	//(ADR-0252 §2) its painted cells when a file can say.
	public sealed record RemasterRecentProjectRow(string Folder, string Name, string Detail, int Recordings = 0)
	{
		public static RemasterRecentProjectRow From(RemasterRecentProject p) => From(p, null);

		public static RemasterRecentProjectRow From(RemasterRecentProject p, int? paintedCells)
		{
			return new RemasterRecentProjectRow(p.Folder, p.Name, DetailOf(p.Recordings, paintedCells), p.Recordings);
		}

		public static string DetailOf(int recordings, int? paintedCells)
		{
			string detail = recordings == 0 ? ResourceHelper.GetMessage("RemasterRecentNoRecordings")
				: recordings == 1 ? ResourceHelper.GetMessage("RemasterRecordingsOne")
				: ResourceHelper.GetMessage("RemasterRecordingsMany", recordings);
			string painted = RemasterWorkspaceViewModel.CellsPaintedText(paintedCells);
			return painted.Length > 0 ? detail + " · " + painted : detail;
		}
	}
}
