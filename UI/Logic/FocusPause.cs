namespace Mesen.Logic;

//ADR-0254 (accepted 2026-10-04): losing focus pauses the game - that much was
//already true behind `Preferences.PauseWhenInBackground` - and in Play it now
//*says so*, with the W-P4 overlay whose Esc is the way back in. Before this the
//app froze with nothing on screen saying why, and resumed by itself the moment
//the window came forward, which is what cost the user a game: a window took
//focus during a fullscreen match and the emulator kept running behind it.
//
//The two halves of the decision the user made are here rather than in the
//window, so they are asserted without one (ADR-0123; UI.Tests/Play/
//FocusPauseTests). `MainWindow.UpdateAutoPause` is the only caller - it owns
//the poll, the preferences and the pause calls.
//
//The user's answers, 2026-10-04: "Fica pausado na tela de Esc" and "Ligado por
//padrão no Play".
public static class FocusPause
{
	//Which pauses get a voice. Only the focus one: the menus-and-config pause
	//fires while the player is *using* the app, in front of a menu or a window
	//they opened themselves, so it has nothing to explain. Outside Play there is
	//no W-P4 to open, so a Classic or Advanced window keeps the silent pause it
	//has always had; a Play door with no game loaded has nothing to pause *for*
	//yet, and the overlay's rows would have no game to describe.
	public static bool ShowsOverlay(bool focusLost, bool isPlayDoor, bool gameLoaded)
		=> focusLost && isPlayDoor && gameLoaded;

	//The overlay the focus path opened waits for the player's Esc, so the
	//automatic resume must not fire under it: the game would come back running
	//behind the pause card, which is both useless (the player never saw the
	//card) and a lie about the state of the game. Every other pause - the menus
	//and config paths, and the focus pause once the player has answered -
	//resumes by itself exactly as before.
	public static bool AutoResumes(bool pausedByFocusWithOverlay, bool overlayOpen)
		=> !(pausedByFocusWithOverlay && overlayOpen);
}
