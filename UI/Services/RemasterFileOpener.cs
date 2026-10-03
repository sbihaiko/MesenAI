using System;
using System.Diagnostics;
using System.IO;

namespace Mesen.Services
{
	//G.6 (W-R4 › Open File) and G.7 (W-R1 zone ② tiles; ADR-0209 Consequences):
	//the OS default program for a kit file (the artist's paint program,
	//ADR-0183) or the file manager for a folder - `open` on macOS, `xdg-open`
	//on Linux, ShellExecute on Windows. An external action the user asked for
	//by clicking; the path goes through ArgumentList, never a joined command line.
	public static class RemasterFileOpener
	{
		public static bool Open(string path)
		{
			if(string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) {
				return false;
			}
			ProcessStartInfo info = new() { UseShellExecute = OperatingSystem.IsWindows(), CreateNoWindow = true };
			if(OperatingSystem.IsWindows()) {
				info.FileName = path;
			} else {
				info.FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
				info.ArgumentList.Add(path);
			}
			try {
				using Process? _ = Process.Start(info);
				return true;
			} catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) {
				return false;
			}
		}
	}
}
