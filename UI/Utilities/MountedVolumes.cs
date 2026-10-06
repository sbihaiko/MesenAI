using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mesen.Utilities
{
	//#845 (ADR-0256 Decision 9): the roots the ROM picker offers beside the two
	//folders the app knows. A stick or a card is where a cabinet's games usually
	//live, and the app has no way to be told about one, so the picker asks the
	//platform.
	//
	//Host-aware by necessity (there is no portable answer), which is exactly why
	//it is a separate file with one method and the picker's rules take the list
	//as a parameter: UI/Logic stays dual-compiled and testable, and this is the
	//only part of the feature that cannot be.
	public static class MountedVolumes
	{
		public static IReadOnlyList<string> List()
		{
			try {
				if(OperatingSystem.IsWindows()) {
					return WindowsDrives();
				}
				if(OperatingSystem.IsMacOS()) {
					//macOS mounts everything the user plugged in under /Volumes.
					return Children("/Volumes");
				}
				//Linux desktops mount removable media under the user's own media
				//directory or /run/media, and permanent ones under /mnt.
				return Children("/media")
					.Concat(Children(Path.Combine("/run/media", Environment.UserName)))
					.Concat(Children("/mnt"))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
			} catch {
				//A machine that answers nothing offers the two folders it always
				//has; the picker is never left without a root.
				return Array.Empty<string>();
			}
		}

		//The mounts the library scan must not enter (ADR-0256 Decision 9
		//amendment): a network mount whose server is gone blocks a directory read
		//in the kernel, where no wall-clock budget can interrupt it, so the scan
		//would hang the sheet instead of degrading. On Windows the list is empty
		//because WindowsDrives above never offers a network drive in the first
		//place - the drive-type filter is already the whole rule there.
		//
		//The rule that reads the table is NetworkMounts', host-free and pinned in
		//UI.Tests; this only fetches the platform's own text.
		public static IReadOnlyList<string> NetworkMountPoints()
		{
			try {
				if(OperatingSystem.IsWindows()) {
					return Array.Empty<string>();
				}
				return NetworkMounts.FromTable(OperatingSystem.IsMacOS() ? MountCommand() : ReadProcMounts());
			} catch {
				//A machine that cannot answer offers the same scan it always did:
				//no network mount is skipped, and none is invented.
				return Array.Empty<string>();
			}
		}

		//macOS has no /proc: `mount` with no arguments prints the whole table,
		//one mount per line, and it is the same text `/sbin/mount` shows a person.
		private static string MountCommand()
		{
			using System.Diagnostics.Process mount = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/sbin/mount") {
				RedirectStandardOutput = true,
				UseShellExecute = false
			}) ?? throw new InvalidOperationException("mount could not be started");
			string table = mount.StandardOutput.ReadToEnd();
			mount.WaitForExit(2000);
			return table;
		}

		//Linux keeps it in /proc/mounts (the kernel's own list; /etc/mtab is a
		//symlink to it on every modern distribution).
		private static string ReadProcMounts()
		{
			return File.ReadAllText("/proc/mounts");
		}

		private static IReadOnlyList<string> WindowsDrives()
		{
			List<string> drives = new();
			foreach(DriveInfo drive in DriveInfo.GetDrives()) {
				try {
					//Ready only: an empty card reader is a drive letter that
					//throws on every read, and a row that does nothing is worse
					//than no row. Network and optical drives are left out too -
					//this is the removable-media list, not a full browser.
					if(drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable) {
						drives.Add(drive.RootDirectory.FullName);
					}
				} catch {
					//One unreadable drive does not cost the others.
				}
			}
			return drives;
		}

		//The directories directly under a mount point. `/Volumes` also holds the
		//boot volume's own name (a symlink back to `/`), which is not a place to
		//start from.
		private static IReadOnlyList<string> Children(string folder)
		{
			try {
				if(!Directory.Exists(folder)) {
					return Array.Empty<string>();
				}
				return Directory.GetDirectories(folder)
					.Where(d => !IsRoot(d))
					.ToList();
			} catch {
				return Array.Empty<string>();
			}
		}

		//True for an entry that is the filesystem root itself - on macOS the boot
		//volume is a symlink under /Volumes, and walking from it is not a choice
		//worth offering.
		private static bool IsRoot(string path)
		{
			try {
				string resolved = Directory.ResolveLinkTarget(path, returnFinalTarget: true) is DirectoryInfo target
					? target.FullName : path;
				return resolved.TrimEnd(Path.DirectorySeparatorChar).Length == 0;
			} catch {
				return false;
			}
		}
	}
}
