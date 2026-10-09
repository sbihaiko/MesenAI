namespace Mesen.Logic;

//W-P4 and the Play sheets over the game (user's decisions, 2026-10-03:
//"Sim, quadro congelado" and "Seguir o render"). The native picture is hidden
//while a Play surface covers the game (PlayGameLayer), so what sits behind the
//scrim is a frozen copy of the last frame, drawn as an Avalonia image; and a
//sheet opened from W-P4 leaves the W-P4 card on screen, dimmed behind it, as
//the renders W-P5 … W-P16 show.

public enum FrozenFrameStep
{
	Keep,
	Capture,
	Drop
}

public static class PlayFrozenFrame
{
	//The frozen frame stands in for the native picture exactly where
	//PlayGameLayer hides that picture for a surface: in the game view, with
	//no home/slot grid over it. The software renderer already draws its last
	//frame as an Avalonia image under the scrim, so it needs no copy.
	//pictureOut is false while a pause that cut the load card short holds the
	//game before its first picture (ADR-0254): there is no frame to stand in.
	public static bool Shows(bool gameViewVisible, bool recentsVisible, bool softwareFrame, bool surfaceOverGame, bool pictureOut = true)
	{
		return gameViewVisible && !recentsVisible && !softwareFrame && surfaceOverGame && pictureOut;
	}

	//Taken once when a surface first covers a loaded game (the core keeps the
	//last frame while paused, so a later capture would be the same picture),
	//dropped when nothing covers the game any more (it resumed) or the game is
	//gone - a previous game's frame never shows behind the next one's sheets.
	//
	//`gameOnScreen` is the picture the frozen frame stands in for actually being
	//the one on screen: the game view showing the game and not the home over it.
	//It defaults to true so a caller that does not know says the ordinary thing,
	//but the caller that does must say it: while the load card holds an open the
	//home is still up, and there is no picture of the game to freeze yet.
	public static FrozenFrameStep Next(bool holding, bool surfaceOverGame, bool gameLoaded, bool gameOnScreen = true, bool pictureOut = true)
	{
		bool wanted = surfaceOverGame && gameLoaded && pictureOut && gameOnScreen;
		if(wanted && !holding) {
			return FrozenFrameStep.Capture;
		}
		if(!wanted && holding) {
			return FrozenFrameStep.Drop;
		}
		return FrozenFrameStep.Keep;
	}
}

public enum PauseCardLayer
{
	Hidden,
	//W-P4 itself: the card is the surface on top.
	Active,
	//A sheet opened from W-P4 is on top; the card stays behind it, dimmed.
	Dimmed
}

public static class PauseCard
{
	//The black layer between the dimmed card and the sheet. Measured on the
	//renders: the card's background goes from 250 (W-P4) to 181 (W-P5, W-P6,
	//W-P7, W-P8, W-P16), the game behind it from 23/38/67 to 17/28/49 - one
	//uniform multiplier of ~0.725, i.e. black at 27.5 % (0x46 / 255).
	public const byte DimAlpha = 0x46;

	//Every sheet the Esc router closes back to W-P4 (PlayEsc) was opened from
	//it, so the card stays behind it. One exception: the first-start pack picker
	//opens without W-P4.
	public static PauseCardLayer Layer(bool gameLoaded, bool overlayVisible, PlaySheet sheet)
	{
		if(!gameLoaded) {
			return overlayVisible ? PauseCardLayer.Active : PauseCardLayer.Hidden;
		}
		bool behindSheet = PlayEsc.Next(true, sheet, false) == PlayEscAction.CloseSheetToOverlay;
		if(behindSheet) {
			return PauseCardLayer.Dimmed;
		}
		if(sheet == PlaySheet.None && overlayVisible) {
			return PauseCardLayer.Active;
		}
		return PauseCardLayer.Hidden;
	}
}
