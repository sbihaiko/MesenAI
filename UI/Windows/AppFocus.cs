using Avalonia.Controls;
using Mesen.Utilities;

namespace Mesen.Windows
{
	//#967: which of the app's windows has focus, as MainWindow.UpdateAutoPause
	//polls it (ADR-0254). Null means none of them - the app is in the
	//background. A seam so a headless test can lose and regain focus without a
	//real desktop (UI.HeadlessTests/FocusPauseWiringTests).
	public interface IAppFocus
	{
		Window? GetActiveWindow();
	}

	//The real one: any of the app's windows, not only the main window - a
	//Settings dialog in front still counts as "in the app".
	public sealed class AppFocus : IAppFocus
	{
		public static readonly AppFocus Desktop = new();

		private AppFocus()
		{
		}

		public Window? GetActiveWindow() => ApplicationHelper.GetActiveWindow();
	}
}
