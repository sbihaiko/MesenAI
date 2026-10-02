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

		public IReadOnlyList<StoredCheat> Stored => _stored;

		//recordingArt is the Remaster game view's context flag (ADR-0245 §3):
		//Play passes false, so every code is available there.
		public void Open(ConsoleType console, string cheatSha1, IReadOnlyList<CheatDbGame> db, IReadOnlyList<StoredCheat> stored, bool recordingArt, bool disableAll, Action<IReadOnlyList<StoredCheat>> save)
		{
			_console = console;
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
			IsSearchEnabled = CheatConsoleScope.HasCheatList(console);
			SetSearchTextSilently("");
			Refresh();
			IsVisible = true;
		}

		public void Close()
		{
			IsVisible = false;
			Closed?.Invoke();
		}

		public void Toggle(PlayerCheatRow row)
		{
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
			IsGameSearch = IsSearchEnabled && game == null;
			SearchPlaceholder = IsGameSearch ? "Search by game name…" : "Search: lives, jump, weapon…";

			GameResults = IsGameSearch ? CheatSheet.SearchGamesByName(_db, SearchText).ToList() : new List<CheatDbGame>();
			string rowFilter = IsGameSearch ? "" : SearchText;
			Rows = CheatSheet.BuildRows(_console, game, anotherCopy, _stored, _recordingArt, rowFilter).Select(r => new PlayerCheatRow(r)).ToList();
			CountOn = CheatSheet.CountOn(_stored);
			StatusLine = CheatSheet.StatusLine(_console, game, anotherCopy, CountOn);
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

		public PlayerCheatRow(CheatSheetRow row)
		{
			Row = row;
		}

		public override string ToString() => Description;
	}
}
