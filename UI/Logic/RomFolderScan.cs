using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Mesen.Logic;

//Where a scan starts and how far down it may go. The user's home and the Mesen
//home are deep (a library can sit five folders in, measured: the requesting
//machine's sits at depth 5 and depth 4 misses it), a mounted volume is shallow
//(a stick's games live at its top).
public sealed record ScanBase(string Folder, int MaxDepth);

//The two budgets that bound one scan: a folder count and a wall clock. They are
//what makes a scan of a whole home honest - it stops early and keeps the ranked
//subset it found, rather than running away.
public sealed record ScanLimits(int MaxFolders, TimeSpan Budget);

//The two passes one scan is made of. The shallow one is cheap and answers
//quickly, the deep one is the answer the feature was measured for - and a cold
//cache or a slow disk can spend the deep budget above any library, which is why
//the player is shown the shallow one while the deep one runs rather than a blank
//sheet.
public enum RomScanPass
{
	Shallow,
	Deep
}

//One folder's entries, as the host reads them. The walk never touches a disk;
//the caller injects this, which is what lets it be pinned in UI.Tests.
public delegate (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) FolderLister(string folder);

//#845 follow-up (ADR-0256 Decision 9 amendment): the bounded walk that finds
//the player's libraries. It collects the folders that hold at least one openable
//file DIRECTLY in them, so a library reads as one place rather than as every
//subfolder it happens to have.
//
//Host-free: it reads nothing itself, and both budgets plus the clock come in as
//parameters. Every failure mode a real filesystem offers - an unreadable folder,
//a symlink that lists itself, a mount that stalls - is answered here without a
//disk, which is why the walk can be tested at all.
public static class RomFolderScan
{
	//Real working folders with real ROMs are hits and must stay hits, so this is
	//a short name skip-list of things no library lives in - not a rule to hide
	//work trees (the requesting machine's `~/adr467-work/roms` is a hit on
	//purpose). `out`, `runs` and `runs-archive` are the same kind of name for
	//this project: measured on the requesting machine, `…/MesenCE/out/release`
	//and `~/runs-archive/fix-499/runs/499-measure/mint` were both offered as
	//libraries.
	private static readonly HashSet<string> _junkNames = new(StringComparer.OrdinalIgnoreCase) {
		"Library", "node_modules", ".git", ".Trash", "Applications",
		"Music", "Movies", "Pictures", "obj", "bin", "dist", "build",
		"out", "runs", "runs-archive"
	};

	//The ADR-0243 layout of a Remaster project: `mep/` (the human layer), `auto/`
	//(the machine one), `project.json` and the `.bootstrap` stamp that ties the
	//folder to one ROM. A folder holding any of them is a workspace whose ROMs
	//are the artist's subject - not a library, so it is neither a hit nor a place
	//to walk into.
	private static readonly HashSet<string> _projectMarkers = new(StringComparer.OrdinalIgnoreCase) {
		"project.json", "mep", "auto", ".bootstrap"
	};

	//`networkMounts` is the host's answer to "which of these mounts live on a
	//server", read off the mount table by RomFolderScanSource. It is a parameter
	//and not a probe because a dead mount cannot be tested for without waiting for
	//it - the very hang this avoids. A mount in it is never listed, neither as a
	//base nor as a child reached from one, so an SMB share whose server is gone
	//costs the player a suggestion instead of the sheet.
	public static IReadOnlyList<RomPickerHit> Run(IReadOnlyList<ScanBase> bases, ScanLimits limits, FolderLister list, Func<TimeSpan> elapsed, CancellationToken token, IReadOnlyCollection<string>? networkMounts = null)
	{
		List<RomPickerHit> hits = new();
		//A folder is entered once, whatever name it was reached by: a symlink
		//that lists itself (or a loop of them) terminates here, and two bases
		//that overlap do not walk the same tree twice.
		HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> network = new(
			(networkMounts ?? Array.Empty<string>()).Select(FullPath).Where(p => p.Length > 0),
			StringComparer.OrdinalIgnoreCase);
		int walked = 0;

		bool OutOfBudget()
		{
			return walked >= limits.MaxFolders || token.IsCancellationRequested || elapsed() >= limits.Budget;
		}

		foreach(ScanBase scanBase in bases) {
			if(OutOfBudget()) {
				break;
			}
			string start = FullPath(scanBase.Folder);
			if(start.Length == 0 || network.Contains(start) || !visited.Add(start)) {
				continue;
			}

			Stack<(string Folder, int Depth)> pending = new();
			pending.Push((start, 0));
			while(pending.Count > 0 && !OutOfBudget()) {
				(string folder, int depth) = pending.Pop();
				walked++;

				(IReadOnlyList<string> Folders, IReadOnlyList<string> Files) entries;
				try {
					entries = list(folder);
				} catch(Exception) {
					//Permission denied, a volume pulled out between listing and
					//descending, an unmounted path: not a hit, and not fatal.
					continue;
				}

				//An enhancement project is a workspace, not a library: nothing in
				//it is offered and nothing in it is walked. Read from the listing
				//the walk already has, so it costs no extra read.
				if(IsEnhancementProject(entries.Folders, entries.Files)) {
					continue;
				}

				string[] openable = entries.Files
					.Where(f => !RomFileKinds.IsHiddenName(Path.GetFileName(f)) && RomFileKinds.IsOpenable(f))
					.ToArray();
				if(openable.Length > 0) {
					//The console is read off the files the walk already listed, so
					//naming it costs no extra read. Archives vote for nothing, so a
					//folder of them answers Unknown rather than a guess.
					hits.Add(new RomPickerHit(folder, openable.Length, RomConsoleKinds.OfFiles(openable)));
				}

				if(depth >= scanBase.MaxDepth) {
					continue;
				}
				foreach(string child in entries.Folders) {
					string name = Path.GetFileName(child);
					if(RomFileKinds.IsHiddenName(name) || _junkNames.Contains(name)) {
						continue;
					}
					string full = FullPath(child);
					if(full.Length == 0 || network.Contains(full) || !visited.Add(full)) {
						continue;
					}
					pending.Push((full, depth + 1));
				}
			}
		}

		return hits;
	}

	//True when a folder's own listing carries the ADR-0243 project layout. The
	//names are the whole test: the marker is a folder or a file depending on
	//which one it is, and both lists are read so `.bootstrap` and `project.json`
	//count wherever the platform happened to put them.
	private static bool IsEnhancementProject(IReadOnlyList<string> folders, IReadOnlyList<string> files)
	{
		foreach(string name in folders.Concat(files)) {
			if(_projectMarkers.Contains(Path.GetFileName(name))) {
				return true;
			}
		}
		return false;
	}

	//A path the platform can spell, or "" when it cannot (a name with a null
	//character, say) - the walk then simply does not enter it.
	private static string FullPath(string folder)
	{
		try {
			return Path.GetFullPath(folder);
		} catch(Exception) {
			return "";
		}
	}
}
