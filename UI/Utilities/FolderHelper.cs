using Mesen.Config;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Utilities
{
	public static class FolderHelper
	{
		//#845 (ADR-0256 Decision 9): the extension table moved to
		//Mesen.Logic.RomFileKinds, which is host-free and so reachable from the
		//ROM picker's dual-compiled rules (ADR-0123). These two stay as the
		//call sites' names, delegating, so there is still one table.
		public static bool IsRomFile(string path)
		{
			return RomFileKinds.IsRomFile(path);
		}

		public static bool IsArchiveFile(string path)
		{
			return RomFileKinds.IsArchiveFile(path);
		}

		public static bool CheckFolderPermissions(string folder, bool checkWritePermission = true)
		{
			if(!Directory.Exists(folder)) {
				try {
					if(string.IsNullOrWhiteSpace(folder)) {
						return false;
					}
					Directory.CreateDirectory(folder);
				} catch {
					return false;
				}
			}
			if(checkWritePermission) {
				try {
					string fileName = Guid.NewGuid().ToString() + ".txt";
					File.WriteAllText(Path.Combine(folder, fileName), "");
					File.Delete(Path.Combine(folder, fileName));
				} catch {
					return false;
				}
			}
			return true;
		}
	}
}
