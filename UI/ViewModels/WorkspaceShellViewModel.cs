using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.ViewModels
{
	//G.1 (PRD Part B §8, ADR-0241, §13.5.1 W-S1-W-S3): the shell around the
	//active workspace - the active-profile button and its switcher rows, the
	//bar's visibility and the one-sentence status line. The rules live in the
	//host-free UI/Logic/WorkspaceShell.cs (UI.Tests/Shell); this type maps them
	//to UI strings. Phase 3 VM rule (UI/AGENTS.md): the constructor does no
	//EmuApi/ConfigManager I/O - the persisted workspace and the game state are
	//injected by MainWindowViewModel, which also persists a switch.
	public partial class WorkspaceShellViewModel : ViewModelBase
	{
		private readonly WorkspaceState _state;
		private readonly bool _isMacOS;
		private bool _gameLoaded;
		private bool _paused;
		private string _gameName = "";
		private string _packName = "";
		private RemasterActivity _remasterActivity;
		private string _remasterStatus = "";
		private string _packInstallStatus = "";
		//G.5 (W-P13/W-P16): one clause an edge flow adds to Play's status line.
		private string _playNotice = "";

		[ObservableProperty] public partial Workspace Active { get; private set; }
		[ObservableProperty] public partial bool IsPlay { get; private set; }
		[ObservableProperty] public partial string ActiveName { get; private set; } = "";
		[ObservableProperty] public partial string ActiveGlyph { get; private set; } = "";
		[ObservableProperty] public partial List<WorkspaceSwitcherRowViewModel> Rows { get; private set; } = new();
		[ObservableProperty] public partial bool IsBarVisible { get; private set; } = true;
		[ObservableProperty] public partial string StatusText { get; private set; } = "";
		//G.3 (§13.6): the profile button's dot while Remaster records or runs a
		//job and another profile is shown - red for a recording, tint for a job.
		[ObservableProperty] public partial bool ShowsActivityDot { get; private set; }
		[ObservableProperty] public partial bool ShowsRecordingDot { get; private set; }
		[ObservableProperty] public partial bool ShowsJobDot { get; private set; }

		//Raised after the active workspace changed (never for a no-op pick).
		public event Action<Workspace>? WorkspaceChanged;

		[Obsolete("For designer only")]
		public WorkspaceShellViewModel() : this(Workspace.Play, OperatingSystem.IsMacOS()) { }

		public WorkspaceShellViewModel(Workspace initial, bool isMacOS)
		{
			_state = new WorkspaceState(initial);
			_isMacOS = isMacOS;
			Refresh();
		}

		//The modifier printed grey on each switcher row: ⌘ on macOS, Ctrl elsewhere.
		public string ShortcutHint(Workspace workspace)
		{
			int digit = WorkspaceShell.ShortcutDigit(workspace);
			return _isMacOS ? "⌘" + digit : "Ctrl+" + digit;
		}

		//Picking a row (or ⌘1/⌘2/⌘3) replaces the window's content and the bar's
		//name. Nothing else happens: no pause, no stop, no settings rewrite.
		public bool Select(Workspace target)
		{
			if(!_state.Select(target)) {
				return false;
			}
			Refresh();
			WorkspaceChanged?.Invoke(_state.Active);
			return true;
		}

		//G.3: fed by the Remaster workspace whenever its recording or job changes.
		public void UpdateRemasterActivity(RemasterActivity activity, string status)
		{
			_remasterActivity = activity;
			_remasterStatus = status ?? "";
			RefreshChrome();
		}

		//G.4 (W-P9): a pack installing while the game plays; with the overlay
		//open, the status line carries the pill's sentence. Empty = none.
		public void UpdatePackInstall(string text)
		{
			_packInstallStatus = text ?? "";
			RefreshChrome();
		}

		public void UpdateGameState(bool gameLoaded, bool paused, string gameName, string packName)
		{
			_gameLoaded = gameLoaded;
			_paused = paused;
			_gameName = gameName ?? "";
			_packName = packName ?? "";
			RefreshChrome();
		}

		//G.5: "Zelda needs the FDS BIOS" (no game) or "waiting for one file"
		//(appended while the game runs). Empty clears it.
		public void SetPlayNotice(string notice)
		{
			_playNotice = notice ?? "";
			RefreshChrome();
		}

		public string PlayNotice => _playNotice;

		private void Refresh()
		{
			Active = _state.Active;
			IsPlay = _state.IsPlay;
			ActiveName = WorkspaceName(Active);
			ActiveGlyph = WorkspaceGlyph(Active);
			Rows = _state.Rows.Select(r => new WorkspaceSwitcherRowViewModel(
				r.Workspace, WorkspaceGlyph(r.Workspace), WorkspaceName(r.Workspace),
				ResourceHelper.GetMessage("WorkspaceDescription" + r.Workspace), ShortcutHint(r.Workspace), r.IsActive
			)).ToList();
			RefreshChrome();
		}

		private void RefreshChrome()
		{
			IsBarVisible = WorkspaceShell.IsBarVisible(_state.Active, _gameLoaded, _paused);
			StatusText = ShellStatusLine.Classify(_gameLoaded, _paused, _packName) switch {
				ShellStatusKind.Playing => ResourceHelper.GetMessage("ShellStatusPlaying", _gameName),
				ShellStatusKind.PlayingWithPack => ResourceHelper.GetMessage("ShellStatusPlayingWithPack", _gameName, _packName),
				ShellStatusKind.Paused => ResourceHelper.GetMessage("ShellStatusPaused", _gameName),
				ShellStatusKind.PausedWithPack => ResourceHelper.GetMessage("ShellStatusPausedWithPack", _gameName, _packName),
				_ => ResourceHelper.GetMessage("ShellStatusNoGame"),
			};
			if(_state.IsPlay) {
				StatusText = PlayStatusNotice.Compose(StatusText, _playNotice, _gameLoaded);
			}
			ShowsActivityDot = RemasterActivityIndicator.ShowsDot(_state.Active, _remasterActivity);
			ShowsRecordingDot = ShowsActivityDot && _remasterActivity == RemasterActivity.Recording;
			ShowsJobDot = ShowsActivityDot && _remasterActivity == RemasterActivity.Job;
			if(_state.IsPlay && _packInstallStatus.Length > 0) {
				StatusText = _packInstallStatus;
			}
			//W-X3: in Play or Share the status line names Remaster's work.
			if(ShowsActivityDot && _remasterStatus.Length > 0) {
				StatusText = _remasterStatus;
			}
		}

		private static string WorkspaceName(Workspace workspace) => ResourceHelper.GetMessage("WorkspaceName" + workspace);

		private static string WorkspaceGlyph(Workspace workspace)
		{
			return workspace switch {
				Workspace.Remaster => "✎",
				Workspace.Share => "▣",
				_ => "▶",
			};
		}
	}

	//One row of the W-S3 switcher popover.
	public sealed class WorkspaceSwitcherRowViewModel
	{
		public Workspace Workspace { get; }
		public string Glyph { get; }
		public string Name { get; }
		public string Description { get; }
		public string ShortcutHint { get; }
		public bool IsActive { get; }

		public WorkspaceSwitcherRowViewModel(Workspace workspace, string glyph, string name, string description, string shortcutHint, bool isActive)
		{
			Workspace = workspace;
			Glyph = glyph;
			Name = name;
			Description = description;
			ShortcutHint = shortcutHint;
			IsActive = isActive;
		}

		public override string ToString() => Name;
	}
}
