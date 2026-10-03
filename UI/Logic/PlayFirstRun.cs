namespace Mesen.Logic;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P12): the first-run sheet that
//replaces the setup wizard - same choices, fewer words. Storage is
//StoreInUserProfile (default the user folder); the keyboard is one popup over
//the wizard's two exclusive checkboxes (KeyPresets' arrow and WASD layouts);
//both gamepad presets are always applied, with no checkbox, because they bind
//different devices and turning one off only hides a pad the user may plug in
//later. A pad neither preset matches goes through W-P15.
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
	//Today's wizard defaults. Check for updates and Desktop shortcut are only
	//shown on Windows/Linux; on macOS they keep the wizard's values (the
	//shortcut was already a no-op there).
	public static FirstRunChoice Defaults { get; } = new(true, FirstRunKeyboard.ArrowKeys, true, true);

	public static FirstRunMappings Mappings(FirstRunKeyboard keyboard)
	{
		return new FirstRunMappings(true, true, keyboard == FirstRunKeyboard.Wasd, keyboard == FirstRunKeyboard.ArrowKeys);
	}

	//The ASCII is the macOS form; Windows and Linux add the two checkboxes.
	public static bool ShowsDesktopOptions(bool isMacOS) => !isMacOS;

	//Rule 2: 2 radios + popup + Start Playing = 4 (6 with the checkboxes).
	public static int ControlCount(bool isMacOS) => ShowsDesktopOptions(isMacOS) ? 6 : 4;

	//Esc and the close button keep what is selected and continue: there is no
	//Cancel, because the app cannot run without a storage choice.
	public static FirstRunChoice OnDismiss(FirstRunChoice current) => current;

	//#661: quitting the app or shutting the OS down while the sheet is up
	//closes it without applying the choice - nothing is written, so it shows
	//again next launch - and never blocks the shutdown, even when the folder
	//cannot be written. Every other close applies the choice (OnDismiss).
	public static bool ConfirmsOnClose(bool shuttingDown) => !shuttingDown;
}
