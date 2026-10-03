using Avalonia.Controls;
using Avalonia.Layout;

namespace Mesen.Utilities
{
	//ADR-0250 Decision 3: a tool window too big for a sheet (Enhancement
	//Packs, the log window, Netplay's Connect and Start Server forms) opens in
	//the Player look when a Player flow opens it - the window carries the
	//`player` class, so PlayerTheme's scope reaches its text, fields, footer
	//buttons (`regular primary` / `regular secondary` in its XAML) and its
	//message boxes (PlayerDialogScope sees the class). Opened from Classic,
	//the debugger or Advanced mode it stays classic: the decision is
	//PlayerDialogScope.UsesPlayerLook on the opener, re-taken on each open.
	public static class PlayerWindowLook
	{
		public static T Apply<T>(T window, Control? opener) where T : Window
		{
			bool player = opener switch {
				Window owner => PlayerDialogScope.UsesPlayerLook(owner),
				Control control => PlayerDialogScope.UsesPlayerLook(control),
				_ => false
			};
			window.Classes.Set("player", player);
			return window;
		}

		//The small forms are sized for the classic 21 px fields; the Player
		//ones are 30 px, so in the Player look the window fits its content's height.
		public static T ApplyToForm<T>(T window, Control? opener) where T : Window
		{
			Apply(window, opener);
			if(window.Classes.Contains("player")) {
				window.ClearValue(Layoutable.HeightProperty);
				window.SizeToContent = SizeToContent.Height;
			}
			return window;
		}
	}
}
