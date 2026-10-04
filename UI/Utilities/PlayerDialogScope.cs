using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Logic;

namespace Mesen.Utilities
{
	//ADR-0249: where a dialog raised from the Player GUI decides its look
	//(UI/Logic/PlayerDialog). A control is in the Player scope when it or an
	//ancestor carries the `player` class; a window shows the Player theme when
	//a visible part of it does (MainWindow in Player mode, Player Settings).
	//Classic windows, the debugger and Advanced mode never do.
	public static class PlayerDialogScope
	{
		public static bool IsInside(Control control)
		{
			for(StyledElement? e = control; e != null; e = e.Parent) {
				if(e.Classes.Contains("player")) {
					return true;
				}
			}
			return false;
		}

		public static bool IsShownBy(Window? window)
		{
			return window != null && window.GetVisualDescendants().OfType<Control>().Any(c => c.Classes.Contains("player") && c.IsEffectivelyVisible);
		}

		//Read without loading the configuration: a box shown before it exists
		//(the Core failing to load at startup) stays classic.
		public static bool PlayerMode => ConfigManager.IsConfigLoaded && ConfigManager.Config.Preferences.UiMode == UiMode.Player;

		public static bool UsesPlayerLook(Window? owner) => PlayerDialog.UsesPlayerLook(PlayerMode, IsShownBy(owner));

		public static bool UsesPlayerLook(Control owner) => PlayerDialog.UsesPlayerLook(PlayerMode, IsInside(owner));
	}
}
