using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic
{
	//The one place the app reads a folder off a disk for the ROM walk. Both
	//halves of it come here - the library scan the Play sheet's grid is built
	//from and the background walk that suggests libraries - so the two cannot
	//disagree about what a folder contains.
	//
	//It lives under UI/Logic because it is host-free by construction (it touches
	//no Avalonia, no core and no config): that is what lets UI.Tests run the real
	//scan over a real tree on disk, instead of only through the fake FolderLister
	//seam every other case there uses (review finding 6 on #1032).
	public static class DiskFolderLister
	{
		//IgnoreInaccessible answers an unreadable folder with nothing instead of
		//throwing mid-enumeration, so the walk never sees it as a library; the
		//try/catch is the belt to that suspenders (a path the platform cannot
		//spell at all, which is the one case #887's tests pin).
		public static (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) List(string folder)
		{
			try {
				EnumerationOptions options = new() { IgnoreInaccessible = true, RecurseSubdirectories = false };
				return (Directory.GetDirectories(folder, "*", options), Directory.GetFiles(folder, "*", options));
			} catch(Exception) {
				return (Array.Empty<string>(), Array.Empty<string>());
			}
		}
	}
}
