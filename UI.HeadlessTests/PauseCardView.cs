using Avalonia;
using Avalonia.Controls;

namespace Mesen.HeadlessTests;

//"Seguir o render" (2026-10-03): with a game loaded, a sheet opened from W-P4
//leaves the card on screen, dimmed behind the sheet and out of the pointer's
//reach. "The overlay is up" now means the card is on screen and on top.
internal static class PauseCardView
{
	public static bool IsPauseCardActive(this Visual root)
	{
		Border card = root.FindNamed<Border>("PlayerOverlay");
		return card.IsOnScreen() && card.IsHitTestVisible;
	}
}
