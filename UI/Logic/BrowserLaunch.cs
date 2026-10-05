using System;
using System.Diagnostics;

namespace Mesen.Logic;

//#859: which program opens a URL and with what argv, split out of
//ApplicationHelper so the decision is host-free and testable. The URL is an
//argument (or, on Windows, the ShellExecute target), never a joined command
//line handed to a shell.
public static class BrowserLaunch
{
	public enum Platform
	{
		Windows,
		MacOS,
		Linux
	}

	public static Platform Host =>
		OperatingSystem.IsWindows() ? Platform.Windows :
		OperatingSystem.IsMacOS() ? Platform.MacOS : Platform.Linux;

	public static ProcessStartInfo Build(string url, Platform platform)
	{
		ProcessStartInfo info = new() {
			//On Windows the URL itself is the ShellExecute target (the registry
			//resolves the handler); elsewhere it is an argument to the OS opener.
			FileName = platform switch {
				Platform.Windows => url,
				Platform.MacOS => "open",
				_ => "xdg-open"
			},
			CreateNoWindow = true,
			UseShellExecute = platform == Platform.Windows
		};
		if(platform != Platform.Windows) {
			info.ArgumentList.Add(url);
		}
		return info;
	}
}
