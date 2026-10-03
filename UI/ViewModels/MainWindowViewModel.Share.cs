using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.8 (PRD Part B §13.5.4, ADR-0241): the Share workspace. Its screens
	//(W-H1-W-H3 and W-H4's sheets) cover the content area; while a replay
	//records the game is shown inside Share under one strip, and nothing of
	//Play is on screen (rule 11). Switching profile never stops the recording
	//or the packaging job (§13.6).
	public partial class MainWindowViewModel
	{
		public ShareWorkspaceViewModel Share { get; private set; } = null!;
		[ObservableProperty] public partial bool IsShareScreenVisible { get; private set; }
		[ObservableProperty] public partial bool IsShareGameView { get; private set; }

		private void InitShare()
		{
			Share = new ShareWorkspaceViewModel(
				CommunityPackCatalogFetcher.LoadAllowlist(),
				new JobProcessLauncher(),
				() => Remaster.Feasibility,
				KnownProjects,
				new CoreReplayRecorder(),
				ApplicationHelper.OpenBrowser,
				ShareRecordingSession.Reveal,
				() => (RecordApi.MovieRecording() || RecordApi.MoviePlaying(), NetplayApi.IsConnected())
			);
			Share.RecordingChanged += UpdateShareSurfaces;
			//#647: one job at a time per project, across the two workspaces.
			WorkspaceJobs.Link(Remaster, Share);
			//Remaster's "Share this project — opens Share" (W-R1 zone ③, §13.6).
			Remaster.ShareProjectRequested += ShareProject;
		}

		//The projects Remaster knows: the one it shows (the running game's own,
		//or a folder the user opened there).
		private IEnumerable<string> KnownProjects()
		{
			if(Remaster.Project != null) {
				yield return Remaster.Project.Folder;
			}
		}

		//Rule 11: the link names its destination and the click is the switch;
		//Share lands on W-H3 for that project.
		public void ShareProject(string projectFolder)
		{
			SelectWorkspace(Workspace.Share);
			Share.OpenProject(projectFolder);
		}

		private void UpdateShareGame(bool gameLoaded, string siblingFolder)
		{
			Share?.UpdateGame(gameLoaded, RomInfo.ConsoleType, RomInfo.GetRomName(), (ResourcePath)RomInfo.RomPath,
				gameLoaded ? RemasterProjectLocator.ForGame(siblingFolder, ConfigManager.EnhancementPackFolder) : "");
		}

		//#619: the Share refreshes queued behind the gate measurement post from
		//the thread pool. Headless tests wait on them before they end.
		public Task ShareGateRefresh { get; private set; } = Task.CompletedTask;

		//Which Share surface is on screen. Called from UpdateRemasterSurfaces,
		//which then sets the game picture's layer.
		private void UpdateShareSurfaces()
		{
			bool share = Shell.Active == Workspace.Share;
			IsShareGameView = share && Share.IsRecording;
			IsShareScreenVisible = share && !Share.IsRecording;
			if(share) {
				//W-H3's Build needs the same Python/tools gate as Remaster's jobs.
				Task refresh = Remaster.EnsureFeasibilityMeasured().ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(Share.Refresh));
				ShareGateRefresh = Task.WhenAll(ShareGateRefresh, refresh);
			}
			IsGameViewVisible = IsPlayWorkspace || IsRemasterGameView || IsShareGameView;
			UpdateRendererVisibility();
		}
	}
}
