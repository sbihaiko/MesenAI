using System;

namespace Mesen.Logic;

//ADR-0241, PRD Part B §8: the first-run choices. They used to be asked by the
//SetupWizardWindow, one screen before the main window; ADR-0256 Decision 8 took
//that screen out of the startup path, and what it asked moved into Play's
//Settings sheet (MainWindowViewModel.PlayerSettings' System tab) - the same two
//questions, on a surface the pad can drive.
//
//What survives here is only what is true without the wizard: the choice itself,
//its defaults, and the mappings it turns into. Storage is
//StoreInUserProfile (default the user folder); the keyboard is one of
//KeyPresets' arrow and WASD layouts. Both gamepad presets are always applied,
//with no checkbox, because they bind different devices and turning one off only
//hides a pad the user may plug in later. A pad neither preset matches goes
//through W-P15.
public enum FirstRunKeyboard
{
	//Arrow keys + S / A, the wizard's default.
	ArrowKeys,
	//WASD + K / J.
	Wasd
}

public sealed record FirstRunChoice(bool StoreInUserProfile, FirstRunKeyboard Keyboard, bool CheckForUpdates, bool CreateShortcut);

//The DefaultKeyMappingType flags the choice turns into (mapped by the owner).
public sealed record FirstRunMappings(bool Xbox, bool PlayStation, bool Wasd, bool Arrows);

public static class PlayFirstRun
{
	//The wizard's defaults, and still what a fresh install runs on: the user
	//folder, arrow keys, no update check (UpdateChannel.HasFeed is false while
	//the fork has no feed, #672), a desktop shortcut. CheckForUpdates and
	//CreateShortcut were the wizard's two Windows/Linux checkboxes and no
	//surface asks them any more - the record keeps them because Defaults is
	//also the first-run record the tests read (UI.Tests/Config/UpdateChannelTests).
	public static FirstRunChoice Defaults { get; } = new(true, FirstRunKeyboard.ArrowKeys, UpdateChannel.HasFeed, true);

	public static FirstRunMappings Mappings(FirstRunKeyboard keyboard)
	{
		return new FirstRunMappings(true, true, keyboard == FirstRunKeyboard.Wasd, keyboard == FirstRunKeyboard.ArrowKeys);
	}

	//ADR-0256 Decision 8: what the Settings surface's keyboard row reads off the
	//running config, the other way round from Mappings. DefaultKeyMappings holds
	//the Xbox and Ps4 flags alongside the keyboard one, so the keyboard half is
	//"WASD is set and the arrows are not"; anything else - neither flag, which is
	//a config that never had a preset, or both - reads as the default.
	public static FirstRunKeyboard KeyboardOf(bool wasdKeys, bool arrowKeys)
	{
		return wasdKeys && !arrowKeys ? FirstRunKeyboard.Wasd : Defaults.Keyboard;
	}

	//ADR-0256 Decision 8: the folder a storage choice means. The same two folders
	//ConfigManager resolves at startup (DefaultDocumentsFolder and
	//DefaultPortableFolder), passed in so this stays host-free.
	public static string Folder(bool storeInUserProfile, string documentsFolder, string portableFolder)
	{
		return storeInUserProfile ? documentsFolder : portableFolder;
	}

	//The same folder, spelled the same way. Path comparison belongs to the host,
	//so this is case-insensitive as Windows is, and harmless where it is not.
	public static bool SameFolder(string? left, string? right)
	{
		return !string.IsNullOrWhiteSpace(left) && string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	//Which storage radio the surface opens on: the folder the process is running
	//from. Same reading as the Advanced door's Change Folder window
	//(SelectStorageFolderViewModel), which asks whether HomeFolder is the folder
	//next to the app.
	public static bool InUserFolder(string currentFolder, string portableFolder)
	{
		return !SameFolder(currentFolder, portableFolder);
	}

	//ADR-0256 Decision 8: whether a storage choice needs the relaunch the wizard's
	//flow ended with (write the settings file, start the process again). The home
	//folder is resolved once, before the main window exists, and everything the
	//core writes - saves, save states, packs, logs - hangs off it, so a folder
	//change cannot take effect under a running process. Choosing the folder the
	//process is already on is not a change and asks for nothing.
	public static bool NeedsRestart(string currentFolder, string chosenFolder)
	{
		return !SameFolder(currentFolder, chosenFolder);
	}

	//ADR-0256 Decision 8: the startup rule the wizard used to be is gone, and it
	//is gone from the startup path itself rather than inverted into a flag -
	//Program.Main asks nothing before the main window now, and
	//UI.Tests/Play/EdgeFlowsTests' FirstRunStartupTests reads that path rather
	//than a constant that says so.
	//
	//What left with the wizard, and why:
	//  OnDismiss         - Esc and the close button kept the choice and continued.
	//                      There is no sheet to dismiss before the app can run.
	//  ConfirmsOnClose   - the sheet applied the choice on close, except when the
	//                      app or the OS was shutting down (#661). Nothing is
	//                      written on a close any more.
	//  ShowsDesktopOptions / ControlCount
	//                    - the Windows/Linux checkboxes and the first-run sheet's
	//                      control count (rule 2). No surface draws that card -
	//                      the Advanced door's Preferences tab keeps the two
	//                      checkboxes themselves.
}
