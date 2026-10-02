using System;
using System.Diagnostics;
using System.IO;

namespace Mesen.Services
{
	//G.6 (W-R4 › Open File): the OS default program for a kit file (the
	//artist's paint program, ADR-0183) or the file manager for a folder. An
	//external action the user asked for by clicking the row's button.
	public static class RemasterFileOpener
	{
		public static void Open(string path)
		{
			if(string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) {
				return;
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
			} catch(Exception ex) when(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) {
			}
		}
	}
}
