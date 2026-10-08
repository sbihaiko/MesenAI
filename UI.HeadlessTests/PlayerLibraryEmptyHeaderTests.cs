using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1069 (ADR-0264 Decision 8): the library header is ONE sentence - "Your
//library · N games in M folders" - and it is the counted one on EVERY state of
//the library surface, an empty library included. A library the player emptied,
//or never had a folder for, used to fall back to the plain sheet title, so the
//header stopped naming the library it describes exactly where a player most
//needs it read back: "0 games in 0 folders" is the answer to "why is the grid
//empty", and "Your library" alone is not.
//
//The two empty paths meet on that one contract and are two code paths, so each
//gets its own case: the rescan that follows the LAST FOLDER being removed
//(PlayerRomPickerViewModel.Folders), and ShowLibrary's early return when there
//is no folder to read at all.
//
//View-model state only, built directly as PlayerRomPickerModeTests builds it:
//both folder sources are stubbed, the scan never touches a disk and no window
//is opened, so the cases run with or without the native core.
[NativeCoreFree("PlayerRomPickerViewModel's constructor only builds its AddKnownGameFolder delegate; both folder sources are stubbed here and no case designates a games folder")]
public class PlayerLibraryEmptyHeaderTests : IDisposable
{
	//Decision 8's literal for an empty library, from the ONE sentence the ADR
	//names: zero games, zero folders, en-US.
	private const string EmptyLibraryHeader = "Your library · 0 games in 0 folders";

	private readonly PreferencesConfig _prefs = ConfigManager.Config.Preferences;
	private readonly List<string>? _libraryFolders;
	private readonly string? _gameFolder;
	private readonly bool _overrideGameFolder;

	public PlayerLibraryEmptyHeaderTests()
	{
		//The list is a stored preference, so the cases save and restore it: the
		//config is process-global and the rest of the suite reads it too.
		_libraryFolders = _prefs.LibraryFolders;
		_gameFolder = _prefs.GameFolder;
		_overrideGameFolder = _prefs.OverrideGameFolder;
	}

	public void Dispose()
	{
		_prefs.LibraryFolders = _libraryFolders;
		_prefs.GameFolder = _gameFolder ?? "";
		_prefs.OverrideGameFolder = _overrideGameFolder;
		ConfigManager.Config.Save();
	}

	//The library the case is about: the folder list is the seam, and the scan
	//answers the same empty answer for every folder it is handed, so no case
	//here reads a disk.
	private static PlayerRomPickerViewModel Picker(IReadOnlyList<string> folders)
	{
		return new PlayerRomPickerViewModel {
			LibraryFolderSource = () => folders,
			LibraryScanStreamSource = (_, _, onEntries) => {
				LibraryScanResult result = new(Array.Empty<LibraryEntry>(), 0, false);
				onEntries(result.Entries);
				return result;
			},
			//No root, no hit and no walk may come from the machine this suite
			//happens to run on.
			VolumeSource = () => Array.Empty<string>(),
			WholeComputerFolder = null,
			SuggestionSource = _ => Array.Empty<RomPickerHit>(),
			RunScanInline = true,
			RunLibraryScanInline = true
		};
	}

	//A folder list that is stored and then emptied: the list is non-null from
	//the start, so opening the sheet seeds nothing and writes nothing.
	private void StoreFolderList(params string[] folders)
	{
		_prefs.LibraryFolders = new List<string>(folders);
		_prefs.OverrideGameFolder = false;
	}

	//(a) The last folder leaves the list: the grid is the named empty state, and
	//the header is still the counted one - 0 games in 0 folders - because the
	//header describes the library, not the grid.
	[AvaloniaFact]
	public void Removing_the_last_folder_leaves_the_counted_header()
	{
		List<string> folders = new() { "/mesen-1069/library" };
		StoreFolderList(folders.ToArray());

		PlayerRomPickerViewModel picker = Picker(folders);
		picker.Open();
		picker.OpenFoldersSheet();
		Assert.Single(picker.LibraryFolderRows);

		picker.RemoveLibraryFolder(picker.LibraryFolderRows.Single());

		Assert.Empty(picker.LibraryFolderRows);
		Assert.Empty(picker.Tiles);
		Assert.Equal(EmptyLibraryHeader, picker.HeaderText);
	}

	//(b) The library opens with no folder to read at all: the same sentence,
	//because it is the same library.
	[AvaloniaFact]
	public void ShowLibrary_with_no_folder_leaves_the_counted_header()
	{
		StoreFolderList();

		PlayerRomPickerViewModel picker = Picker(Array.Empty<string>());
		picker.Open();

		Assert.True(picker.IsLibraryMode, "the sheet did not open on the library surface");
		Assert.Empty(picker.Tiles);
		Assert.Equal(EmptyLibraryHeader, picker.HeaderText);
	}
}
