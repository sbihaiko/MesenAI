using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//P.10 (ADR-0245 §1-§3, §5; PRD Part B §13 W-P11): the Play Cheats sheet,
	//opened from the pause overlay's Cheats row. A new ViewModel under the
	//UI/AGENTS.md Phase 3 rule: no EmuApi/ConfigManager I/O here - Open() is
	//handed the database, the stored list, the console and the cheat hash, and
	//a save callback that writes the shared CheatCodes list. Every decision is
	//made by the host-free UI/Logic/CheatSheet.
	//R.4 (ADR-0248 §2, §5): community rows for the copy come below the bundled
	//list (SetCommunityCatalog may land after Open, when the fetch returns), the
	//vote count opens the issue, and the user's own codes get *Share This
	//Cheat ↗*. Both open a URL through the injected openUrl, never directly.
	public partial class PlayerCheatsSheetViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; set; }
		[ObservableProperty] public partial string SearchText { get; set; } = "";
		[ObservableProperty] public partial string SearchPlaceholder { get; private set; } = "";
		[ObservableProperty] public partial bool IsSearchEnabled { get; private set; }
		[ObservableProperty] public partial List<PlayerCheatRow> Rows { get; private set; } = new();
		[ObservableProperty] public partial List<CheatDbGame> GameResults { get; private set; } = new();
		[ObservableProperty] public partial bool IsGameSearch { get; private set; }
		[ObservableProperty] public partial bool IsBorrowingGame { get; private set; }
		[ObservableProperty] public partial string StatusLine { get; private set; } = "";
		[ObservableProperty] public partial bool IsAllOff { get; private set; }
		[ObservableProperty] public partial int CountOn { get; private set; }

		//*Add a Code…*: an inline entry row (the full editor stays in Tools ⋯).
		[ObservableProperty] public partial bool IsAddCodeOpen { get; set; }
		[ObservableProperty] public partial string NewCode { get; set; } = "";
		[ObservableProperty] public partial string NewDescription { get; set; } = "";
		[ObservableProperty] public partial string AddCodeError { get; private set; } = "";

		public string ReplayNote => CheatSheet.ReplayNote;
		public string AllOffNote => CheatSheet.AllOffNote;

		//Raised by Close() so the owner can bring the pause overlay back (rule 8).
		public event Action? Closed;

		private ConsoleType _console;
		private IReadOnlyList<CheatDbGame> _db = Array.Empty<CheatDbGame>();
		private IReadOnlyList<StoredCheat> _stored = Array.Empty<StoredCheat>();
		private CheatDbGame? _thisCopy;
		private CheatDbGame? _borrowed;
		private bool _recordingArt;
		private Action<IReadOnlyList<StoredCheat>> _save = _ => { };
		private string _cheatSha1 = "";
		private string _gameName = "";
		private Action<string> _openUrl = _ => { };
		private IReadOnlyList<CommunityCheat> _community = Array.Empty<CommunityCheat>();
		//#639: the cheat hash of the copy running now; null = the copy never
		//changes under the sheet (a caller with no running game, or a test).
		private Func<string>? _runningCheatSha1;

		//CheatCodes saves to the running game's file: a sheet left over from
		//another copy must not write that game's list (CheatSheet.SavesTo).
		private bool CanSave => _runningCheatSha1 == null || CheatSheet.SavesTo(_cheatSha1, _runningCheatSha1());

		public IReadOnlyList<StoredCheat> Stored => _stored;

		//recordingArt is the Remaster game view's context flag (ADR-0245 §3):
		//Play passes false, so every code is available there.
		//gameName prefills the share form; community is the catalog known when
		//the sheet opens (CommunityCheatCatalogFetcher.LastKnown).
		public void Open(ConsoleType console, string cheatSha1, IReadOnlyList<CheatDbGame> db, IReadOnlyList<StoredCheat> stored, bool recordingArt, bool disableAll, Action<IReadOnlyList<StoredCheat>> save,
			string gameName = "", Action<string>? openUrl = null, IReadOnlyList<CommunityCheatGame>? community = null, Func<string>? runningCheatSha1 = null)
		{
			_runningCheatSha1 = runningCheatSha1;
			_console = console;
			_cheatSha1 = cheatSha1;
			_gameName = gameName;
			_openUrl = openUrl ?? (_ => { });
			_community = CommunityCheatCatalog.ForCopy(community ?? Array.Empty<CommunityCheatGame>(), _cheatSha1, console);
			_db = db;
			_stored = stored;
			_recordingArt = recordingArt;
			_save = save;
			_thisCopy = CheatSheet.FindGameForCopy(db, cheatSha1);
			_borrowed = null;
			IsAllOff = disableAll;
			IsAddCodeOpen = false;
			NewCode = "";
			NewDescription = "";
			AddCodeError = "";
			SetSearchTextSilently("");
			Refresh();
			IsVisible = true;
		}

		//The fetched catalog, when it returns after Open: only this copy's rows
		//(exact SHA-1) are taken from it.
		public void SetCommunityCatalog(IReadOnlyList<CommunityCheatGame> catalog)
		{
			_community = CommunityCheatCatalog.ForCopy(catalog, _cheatSha1, _console);
			Refresh();
		}

		//The cheat hash the sheet was opened for, so a late fetch can check it
		//still applies.
		public string CheatSha1 => _cheatSha1;

		//A tap on a community row's 👍 count: the issue, to vote there (↗).
		public void OpenIssue(PlayerCheatRow row)
		{
			if(row.Row.Source == CheatRowSource.Community && row.Row.Issue > 0) {
				_openUrl(CommunityCheatCatalog.IssueUrl(row.Row.Issue));
			}
		}

		//*Share This Cheat ↗*: the pre-filled `[Cheat]` form in the browser.
		public void Share(PlayerCheatRow row)
		{
			if(CheatShare.CanShare(row.Row, _console, _cheatSha1)) {
				_openUrl(CheatShare.BuildIssueUrl(_cheatSha1, _gameName, _console, row.Row.Codes, row.Row.Description));
			}
		}

		public void Close()
		{
			IsVisible = false;
			Closed?.Invoke();
		}

		public void Toggle(PlayerCheatRow row)
		{
			if(!CanSave) {
				Refresh();
				return;
			}
			IReadOnlyList<StoredCheat> next = CheatSheet.Toggle(_stored, row.Row, _recordingArt);
			if(!ReferenceEquals(next, _stored)) {
				_stored = next;
				_save(_stored);
			}
			Refresh();
		}

		//Not-in-list fallback: codes from a game picked by name, marked
		//"made for another copy — may not work".
		public void PickGame(CheatDbGame game)
		{
			_borrowed = game;
			SetSearchTextSilently("");
			Refresh();
		}

		public void ChangeGame()
		{
			_borrowed = null;
			SetSearchTextSilently("");
			Refresh();
		}

		public void ToggleAddCode()
		{
			IsAddCodeOpen = !IsAddCodeOpen;
			AddCodeError = "";
		}

		public void AddCode()
		{
			if(!CanSave) {
				Refresh();
				return;
			}
			(IReadOnlyList<StoredCheat> next, string error) = CheatSheet.AddCode(_stored, _console, NewDescription, NewCode, _recordingArt);
			AddCodeError = error;
			if(error.Length == 0) {
				_stored = next;
				_save(_stored);
				NewCode = "";
				NewDescription = "";
				IsAddCodeOpen = false;
			}
			Refresh();
		}

		partial void OnSearchTextChanged(string value)
		{
			if(!_silent) {
				Refresh();
			}
		}

		private bool _silent;
		private void SetSearchTextSilently(string text)
		{
			_silent = true;
			SearchText = text;
			_silent = false;
		}

		private void Refresh()
		{
			CheatDbGame? game = _borrowed ?? _thisCopy;
			bool anotherCopy = _borrowed != null;
			IsBorrowingGame = anotherCopy;
			bool hasList = CheatConsoleScope.HasCheatList(_console);
			//A console without a bundled list still searches its community rows.
			IsSearchEnabled = hasList || _community.Count > 0;
			IsGameSearch = hasList && game == null;
			SearchPlaceholder = IsGameSearch ? "Search by game name…" : "Search: lives, jump, weapon…";

			GameResults = IsGameSearch ? CheatSheet.SearchGamesByName(_db, SearchText).ToList() : new List<CheatDbGame>();
			string rowFilter = IsGameSearch ? "" : SearchText;
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(_console, game, anotherCopy, _stored, _recordingArt, rowFilter, _community);
			Rows = rows.Select(r => new PlayerCheatRow(r, CheatShare.CanShare(r, _console, _cheatSha1))).ToList();
			CountOn = CheatSheet.CountOn(_stored);
			StatusLine = CheatSheet.StatusLine(_console, game, anotherCopy, CountOn, rows.Count(r => r.Source == CheatRowSource.Community));
		}
	}

	//One W-P11 toggle, as the view binds it.
	public sealed class PlayerCheatRow
	{
		public CheatSheetRow Row { get; }
		public string Description => Row.Description;
		public bool IsOn => Row.IsOn;
		public bool CanToggle => Row.CanToggle;
		public string Note => Row.Note;
		public bool HasNote => Row.Note.Length > 0;
		public bool IsCommunity => Row.Source == CheatRowSource.Community;
		public string CommunityMark => CommunityCheatCatalog.CommunityMark;
		public string VotesLabel => CommunityCheatCatalog.VotesLabel(Row.Votes);
		public bool CanShare { get; }

		public PlayerCheatRow(CheatSheetRow row, bool canShare = false)
		{
			Row = row;
			CanShare = canShare;
		}

		public override string ToString() => Description;
	}
}
