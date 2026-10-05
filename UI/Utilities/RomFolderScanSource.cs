using Mesen.Config;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Mesen.Utilities
{
	//#845 follow-up (ADR-0256 Decision 9 amendment): the host half of the
	//library scan - the standard places to look, the real folder lister, the
	//mount table and the two budgets. RomFolderScan owns every rule and reads
	//nothing; this is the only part of the feature that touches a disk, which is
	//why it is a separate file beside MountedVolumes.
	public static class RomFolderScanSource
	{
		//Depth 5 for a home, depth 3 for a volume: measured on the requesting
		//machine, whose library sits at depth 5 (depth 4 misses it, 0.31 s, and
		//depth 5 finds it, 0.86 s) and whose plugged-in volumes hold theirs at
		//the top. The folder and wall-clock budgets stop a home that is a forest.
		private const int HomeDepth = 5;
		private const int VolumeDepth = 3;
		//The shallow pass: the same places, stopped early enough to answer while
		//the deep one is still walking. Its rows are published the moment they
		//land and replaced by the deep answer, so a slow disk costs the player the
		//best answer rather than every answer.
		private const int ShallowHomeDepth = 2;
		private const int ShallowVolumeDepth = 1;
		private static readonly TimeSpan ShallowBudget = TimeSpan.FromMilliseconds(400);
		private const int MaxFolders = 200_000;
		private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1.5);

		public static IReadOnlyList<RomPickerHit> Scan(RomScanPass pass)
		{
			Stopwatch clock = Stopwatch.StartNew();
			bool shallow = pass == RomScanPass.Shallow;
			List<ScanBase> bases = new();

			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if(!string.IsNullOrEmpty(home)) {
				bases.Add(new ScanBase(home, shallow ? ShallowHomeDepth : HomeDepth));
			}
			//The Mesen home is a base of its own: on macOS it lives under the
			//user's Library, which the home walk skips by name.
			string mesenHome = ConfigManager.HomeFolder;
			if(!string.IsNullOrEmpty(mesenHome) && !SameFolder(mesenHome, home)) {
				bases.Add(new ScanBase(mesenHome, shallow ? ShallowHomeDepth : HomeDepth));
			}
			foreach(string volume in MountedVolumes.List()) {
				bases.Add(new ScanBase(volume, shallow ? ShallowVolumeDepth : VolumeDepth));
			}

			return RomFolderScan.Run(
				bases,
				new ScanLimits(MaxFolders, shallow ? ShallowBudget : Budget),
				ListFolder, () => clock.Elapsed, default,
				//A dead network mount blocks a read in the kernel, where neither
				//budget above can interrupt it (ADR-0256 Decision 9 amendment).
				MountedVolumes.NetworkMountPoints());
		}

		//The real lister. IgnoreInaccessible answers an unreadable folder with
		//nothing instead of throwing mid-enumeration, so the walk never sees it
		//as a hit; the try/catch is the belt to that suspenders (a path the
		//platform cannot spell at all).
		private static (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) ListFolder(string folder)
		{
			try {
				EnumerationOptions options = new() { IgnoreInaccessible = true, RecurseSubdirectories = false };
				return (Directory.GetDirectories(folder, "*", options), Directory.GetFiles(folder, "*", options));
			} catch(Exception) {
				return (Array.Empty<string>(), Array.Empty<string>());
			}
		}

		private static bool SameFolder(string left, string right)
		{
			try {
				return string.Equals(
					Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
					Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
					StringComparison.OrdinalIgnoreCase);
			} catch(Exception) {
				return false;
			}
		}
	}
}
