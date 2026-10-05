using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Utilities
{
	public static class ApplicationHelper
	{
		public static Window? GetMainWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is Window wnd) {
				return wnd;
			}

			return null;
		}

		public static Window? GetActiveOrMainWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w.IsActive).FirstOrDefault() ?? GetMainWindow();
			}

			return GetMainWindow();
		}

		public static Window? GetActiveWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w.IsActive).FirstOrDefault();
			}

			return null;
		}

		public static T? GetExistingWindow<T>() where T : MesenWindow
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w is T).FirstOrDefault() as T;
			}

			return null;
		}

		public static T GetOrCreateUniqueWindow<T>(Control? centerParent, Func<T> createWindow) where T : MesenWindow
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				T? wnd = desktop.Windows.Where(w => w is T).FirstOrDefault() as T;
				if(wnd == null) {
					wnd = createWindow();
					if(centerParent != null) {
						wnd.ShowCentered((Control)centerParent);
					} else {
						wnd.Show();
					}
					return wnd;
				} else {
					wnd.BringToFront();
					return wnd;
				}
			}

			throw new NotSupportedException();
		}

		public static List<Window> GetOpenedWindows()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return new List<Window>(desktop.Windows);
			}

			return new List<Window>();
		}

		//Taken from Avalonia's code (MIT): https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Dialogs/AboutAvaloniaDialog.xaml.cs
		public static void OpenBrowser(string url)
		{
			//#859: the URL reaches the opener as one argument, never joined into a
			//shell command line. What program and argv that is lives in the
			//host-free BrowserLaunch (asserted in UI.Tests).
			using Process? process = Process.Start(BrowserLaunch.Build(url, BrowserLaunch.Host));
		}
	}
}
