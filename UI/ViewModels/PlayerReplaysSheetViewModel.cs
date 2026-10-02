using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//R.2 (ADR-0205 §7; W-P4 › Save states › Shared replays): the community
	//replays recorded on this exact copy of the game, most-👍-first. A new
	//ViewModel under the UI/AGENTS.md Phase 3 rule: no EmuApi/RecordApi I/O
	//here - Open() is handed the ROM SHA-1, the console, the catalog, why
	//Watch is off, and the callbacks that open a URL, download a row and play
	//the downloaded file. Every rule is in the host-free UI/Logic
	//(CommunityReplayCatalog, ReplayWatch).
	public partial class PlayerReplaysSheetViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; set; }
		[ObservableProperty] public partial List<PlayerReplayRow> Rows { get; private set; } = new();
		[ObservableProperty] public partial string StatusLine { get; private set; } = "";
		//Why Watch is off (rule 4), or the in-place confirmation, or a failure.
		[ObservableProperty] public partial string NoticeLine { get; private set; } = "";
		[ObservableProperty] public partial bool IsBusy { get; private set; }

		//Raised by Close() so the owner can bring the pause overlay back (rule 8).
		public event Action? Closed;
		//Raised once a verified replay started playing: the owner takes the
		//pause surfaces down and resumes.
		public event Action? Watching;

		private string _romSha1 = "";
		private ConsoleType _console;
		private IReadOnlyList<CommunityReplay> _replays = Array.Empty<CommunityReplay>();
		private ReplayWatchReason _reason;
		private int _armedIssue;
		private Action<string> _openUrl = _ => { };
		private Func<CommunityReplay, Task<ReplayFetchResult>> _download = _ => Task.FromResult(new ReplayFetchResult(null, ReplayFetchFailure.Unavailable));
		private Action<string> _play = _ => { };

		//The ROM hash the sheet was opened for, so a late fetch can check it
		//still applies.
		public string RomSha1 => _romSha1;

		public void Open(string romSha1, ConsoleType console, IReadOnlyList<CommunityReplayGame> catalog, ReplayWatchReason reason,
			Action<string> openUrl, Func<CommunityReplay, Task<ReplayFetchResult>> download, Action<string> play)
		{
			_romSha1 = romSha1 ?? "";
			_console = console;
			_reason = reason;
			_openUrl = openUrl;
			_download = download;
			_play = play;
			_armedIssue = 0;
			IsBusy = false;
			NoticeLine = ReplayWatch.ReasonText(reason);
			SetCatalog(catalog);
			IsVisible = true;
		}

		//The fetched catalog, when it returns after Open: only this copy's rows
		//(exact SHA-1, same console) are taken from it.
		public void SetCatalog(IReadOnlyList<CommunityReplayGame> catalog)
		{
			_replays = CommunityReplayCatalog.ForRom(catalog, _romSha1, _console);
			if(_replays.All(r => r.Issue != _armedIssue)) {
				_armedIssue = 0;
			}
			Refresh();
		}

		//A tap on a row's 👍 count: the issue, to vote there (↗).
		public void OpenIssue(PlayerReplayRow row)
		{
			_openUrl(CommunityReplayCatalog.IssueUrl(row.Replay.Issue));
		}

		//Rule 7: the first click asks in place (playing restarts the game), the
		//second on the same row downloads, verifies and plays.
		public async Task Watch(PlayerReplayRow row)
		{
			if(_reason != ReplayWatchReason.None || IsBusy) {
				return;
			}
			if(ReplayWatch.Click(_armedIssue, row.Replay.Issue) == ReplayWatchStep.Arm) {
				_armedIssue = row.Replay.Issue;
				NoticeLine = ReplayWatch.ArmedText;
				Refresh();
				return;
			}
			_armedIssue = 0;
			IsBusy = true;
			NoticeLine = "";
			Refresh();
			ReplayFetchResult result = await _download(row.Replay);
			IsBusy = false;
			if(result.Failure != ReplayFetchFailure.None || result.Path == null) {
				NoticeLine = ReplayWatch.FailureText(result.Failure == ReplayFetchFailure.None ? ReplayFetchFailure.Unavailable : result.Failure);
				Refresh();
				return;
			}
			IsVisible = false;
			_play(result.Path);
			Watching?.Invoke();
		}

		public void Close()
		{
			IsVisible = false;
			_armedIssue = 0;
			Closed?.Invoke();
		}

		private void Refresh()
		{
			bool canWatch = _reason == ReplayWatchReason.None && !IsBusy;
			Rows = _replays.Select(r => new PlayerReplayRow(r, armed: r.Issue == _armedIssue, canWatch)).ToList();
			StatusLine = ReplayWatch.StatusLine(_replays.Count);
		}
	}

	//One shared replay, as the view binds it.
	public sealed class PlayerReplayRow
	{
		public CommunityReplay Replay { get; }
		public bool IsArmed { get; }
		public bool CanWatch { get; }
		public string Title => ReplayWatch.Title(Replay);
		public string Length => ReplayWatch.Length(Replay.Frames);
		public string VotesLabel => CommunityReplayCatalog.VotesLabel(Replay.Votes);
		public string WatchLabel => ReplayWatch.WatchLabel(IsArmed);
		public string CheatBadge => ReplayWatch.CheatBadge(ReplayWatch.CheatCount(Replay));
		public bool HasCheats => ReplayWatch.CheatCount(Replay) > 0;

		public PlayerReplayRow(CommunityReplay replay, bool armed, bool canWatch)
		{
			Replay = replay;
			IsArmed = armed;
			CanWatch = canWatch;
		}

		public override string ToString() => Title;
	}
}
