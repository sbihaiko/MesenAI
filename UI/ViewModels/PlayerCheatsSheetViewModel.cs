using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
		//The user's rule (2026-10-03): every wait moves. The community catalog
		//fetch in flight: a moving bar, and "looking" rather than "none".
		[ObservableProperty] public partial bool IsCommunityLoading { get; private set; }

		//*Add a Code…*: an inline entry row (the full editor stays in Classic › Tools › Cheats).
		[ObservableProperty] public partial bool IsAddCodeOpen { get; set; }
		[ObservableProperty] public partial string NewCode { get; set; } = "";
		[ObservableProperty] public partial string NewDescription { get; set; } = "";
		[ObservableProperty] public partial string AddCodeError { get; private set; } = "";

		//P.11 (ADR-0245 §4, #922): search by intent. The typed intent goes to
		//scripts/cheat_intent.py (Jev, the one backend that passed the #915
		//gate) through the injected runner; the matched listed entry is
		//highlighted, or IntentLine says none matched. The OpenRouter key is
		//typed here once, kept only in the OS credential store and removable
		//from here (ADR-0242 Q1, ADR-0247).
		[ObservableProperty] public partial string IntentText { get; set; } = "";
		[ObservableProperty] public partial string IntentLine { get; private set; } = "";
		[ObservableProperty] public partial bool IsIntentSearching { get; private set; }
		[ObservableProperty] public partial bool IsIntentAvailable { get; private set; }
		[ObservableProperty] public partial string KeyText { get; set; } = "";
		[ObservableProperty] public partial string KeyLine { get; private set; } = "";

		//P.12 (ADR-0245 §4, #924): a copy not in the bundled list can look its
		//codes up online. scripts/cheat_web_lookup.py runs through the injected
		//checker and checks each code on the user's copy; only codes whose check
		//passed become rows ("found online, checked on your copy"), toggled into
		//the same CheatCodes list as a database row.
		[ObservableProperty] public partial bool IsWebLookupAvailable { get; private set; }
		[ObservableProperty] public partial bool IsWebSearching { get; private set; }
		[ObservableProperty] public partial string WebLine { get; private set; } = "";
		public string LookOnlineLabel => CheatWebLookup.LookOnlineLabel;

		public string FindLabel => CheatIntentSearch.FindLabel;
		public string IntentPlaceholder => CheatIntentSearch.IntentPlaceholder;
		public string SaveKeyLabel => CheatIntentSearch.SaveKeyLabel;
		public string RemoveKeyLabel => CheatIntentSearch.RemoveKeyLabel;
		public string KeyPlaceholder => CheatIntentSearch.KeyPlaceholder;

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
		private string _romFile = "";
		private Action<string> _openUrl = _ => { };
		private IReadOnlyList<CommunityCheat> _community = Array.Empty<CommunityCheat>();
		//#639: the cheat hash of the copy running now; null = the copy never
		//changes under the sheet (a caller with no running game, or a test).
		private Func<string>? _runningCheatSha1;
		private int _communityLoading;
		private IByokKeyStore? _keyStore;
		private Func<Task<ICheatIntentRunner?>>? _intentRunner;
		private CheatDbCode? _intentMatch;
		private int _intentToken;
		private string _romPath = "";
		private Func<Task<ICheatWebChecker?>>? _webChecker;
		private IReadOnlyList<WebFoundCode> _web = Array.Empty<WebFoundCode>();
		private int _webToken;

		//CheatCodes saves to the running game's file: a sheet left over from
		//another copy must not write that game's list (CheatSheet.SavesTo).
		private bool CanSave => _runningCheatSha1 == null || CheatSheet.SavesTo(_cheatSha1, _runningCheatSha1());

		public IReadOnlyList<StoredCheat> Stored => _stored;

		//recordingArt is the Remaster game view's context flag (ADR-0245 §3):
		//Play passes false, so every code is available there.
		//gameName and romFile (its file name: GB or GBC, #662) prefill the share
		//form; community is the catalog known when the sheet opens
		//(CommunityCheatCatalogFetcher.LastKnown).
		public void Open(ConsoleType console, string cheatSha1, IReadOnlyList<CheatDbGame> db, IReadOnlyList<StoredCheat> stored, bool recordingArt, bool disableAll, Action<IReadOnlyList<StoredCheat>> save,
			string gameName = "", Action<string>? openUrl = null, IReadOnlyList<CommunityCheatGame>? community = null, Func<string>? runningCheatSha1 = null, string romFile = "", string romPath = "")
		{
			_runningCheatSha1 = runningCheatSha1;
			_console = console;
			_cheatSha1 = cheatSha1;
			_gameName = gameName;
			_romFile = romFile ?? "";
			_romPath = romPath ?? "";
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
			ClearIntent();
			ClearWebLookup();
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

		//The owner's community fetch started; FinishCommunityLoading(token) ends
		//it (a later BeginCommunityLoading's fetch owns the wait).
		public int BeginCommunityLoading()
		{
			IsCommunityLoading = true;
			Refresh();
			return ++_communityLoading;
		}

		//The fetch answered - a catalog, or nothing (offline, a bad file).
		public void FinishCommunityLoading(int token)
		{
			if(token != _communityLoading || !IsCommunityLoading) {
				return;
			}
			IsCommunityLoading = false;
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
				_openUrl(CheatShare.BuildIssueUrl(_cheatSha1, _gameName, _console, _romFile, row.Row.Codes, row.Row.Description));
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

		//The key store and a factory for the runner (null when python3 or the
		//tools are missing); without them the intent search stays hidden.
		public void ConfigureIntentSearch(IByokKeyStore keyStore, Func<Task<ICheatIntentRunner?>> runner)
		{
			_keyStore = keyStore;
			_intentRunner = runner;
			Refresh();
		}

		//The web lookup (null when python3 or the tools are missing); without
		//it, or for a copy the bundled list has, Look Online stays hidden.
		public void ConfigureWebLookup(Func<Task<ICheatWebChecker?>> checker)
		{
			_webChecker = checker;
			Refresh();
		}

		public async Task LookOnline()
		{
			if(!IsWebLookupAvailable || IsWebSearching || _webChecker == null) {
				return;
			}
			int token = ++_webToken;
			IsWebSearching = true;
			WebLine = CheatWebLookup.SearchingLine;
			IReadOnlyList<WebFoundCode> found;
			string line;
			ICheatWebChecker? checker = await _webChecker();
			if(checker == null) {
				found = Array.Empty<WebFoundCode>();
				line = CheatWebLookup.NeedsToolsLine;
			} else {
				found = await checker.LookUpAsync(_romPath, _gameName);
				line = CheatWebLookup.Offered(found).Count == 0 ? CheatWebLookup.NoneLine : "";
			}
			if(token != _webToken) {
				return;
			}
			_web = found;
			IsWebSearching = false;
			WebLine = line;
			Refresh();
		}

		private void ClearWebLookup()
		{
			_webToken++;
			_web = Array.Empty<WebFoundCode>();
			IsWebSearching = false;
			WebLine = "";
		}

		public async Task SearchByIntent()
		{
			CheatDbGame? game = _borrowed ?? _thisCopy;
			if(game == null || _intentRunner == null || IsIntentSearching) {
				return;
			}
			int token = ++_intentToken;
			_intentMatch = null;
			IntentLine = "";
			IsIntentSearching = true;
			CheatIntentOutcome outcome;
			try {
				ICheatIntentRunner? runner = await _intentRunner();
				outcome = runner == null
					? new CheatIntentOutcome(CheatIntentStatus.Failed, null, CheatIntentSearch.NeedsToolsLine)
					: await CheatIntentSearch.SearchAsync(runner, game, IntentText);
			} catch(ByokKeyMissingException) {
				outcome = new CheatIntentOutcome(CheatIntentStatus.Failed, null, CheatIntentSearch.NeedsKeyLine);
			} catch(Exception ex) when(ex is ByokLaunchException || ex is ByokKeyStoreException) {
				//Their text never carries the key (ByokJobLauncher, ByokKeyStore)
				outcome = new CheatIntentOutcome(CheatIntentStatus.Failed, null, ex.Message);
			}
			if(token != _intentToken) {
				return;
			}
			IsIntentSearching = false;
			_intentMatch = outcome.Entry;
			IntentLine = outcome.Line;
			Refresh();
		}

		public void SaveKey()
		{
			if(_keyStore == null || string.IsNullOrWhiteSpace(KeyText)) {
				return;
			}
			try {
				_keyStore.Write(CheatIntentSearch.Vendor, ByokKey.Normalize(KeyText));
				KeyLine = CheatIntentSearch.KeyStoredLine;
			} catch(ByokKeyStoreException ex) {
				KeyLine = ex.Message;
			}
			KeyText = "";
		}

		public void RemoveKey()
		{
			if(_keyStore == null) {
				return;
			}
			try {
				KeyLine = _keyStore.Remove(CheatIntentSearch.Vendor) ? CheatIntentSearch.KeyRemovedLine : CheatIntentSearch.NoKeyLine;
			} catch(ByokKeyStoreException ex) {
				KeyLine = ex.Message;
			}
		}

		private void ClearIntent()
		{
			_intentToken++;
			_intentMatch = null;
			IntentText = "";
			IntentLine = "";
			KeyText = "";
			KeyLine = "";
			IsIntentSearching = false;
		}

		//Not-in-list fallback: codes from a game picked by name, marked
		//"made for another copy — may not work".
		public void PickGame(CheatDbGame game)
		{
			ClearIntent();
			_borrowed = game;
			SetSearchTextSilently("");
			Refresh();
		}

		public void ChangeGame()
		{
			ClearIntent();
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
			//Web codes are for a copy the bundled list does not have (§4).
			IReadOnlyList<WebFoundCode> web = _thisCopy == null ? _web : Array.Empty<WebFoundCode>();
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(_console, game, anotherCopy, _stored, _recordingArt, rowFilter, _community, web);
			Rows = rows.Select(r => new PlayerCheatRow(r, CheatShare.CanShare(r, _console, _cheatSha1), CheatIntentSearch.IsMatch(r, _intentMatch))).ToList();
			IsIntentAvailable = _intentRunner != null && _keyStore?.UnsupportedReason == null && game != null && CheatConsoleScope.HasCheatList(_console);
			IsWebLookupAvailable = _webChecker != null && _thisCopy == null && CheatConsoleScope.HasCheatList(_console) && _romPath.Length > 0;
			CountOn = CheatSheet.CountOn(_stored);
			StatusLine = CheatSheet.StatusLine(_console, game, anotherCopy, CountOn, rows.Count(r => r.Source == CheatRowSource.Community), IsCommunityLoading, rows.Count(r => r.Source == CheatRowSource.WebFound));
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
		//P.11: the entry the search by intent picked.
		public bool IsIntentMatch { get; }
		public string IntentMatchMark => CheatIntentSearch.MatchMark;

		public PlayerCheatRow(CheatSheetRow row, bool canShare = false, bool isIntentMatch = false)
		{
			Row = row;
			CanShare = canShare;
			IsIntentMatch = isIntentMatch;
		}

		public override string ToString() => Description;
	}
}
