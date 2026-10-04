using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//PRD Part B §13.5.3 W-R0 "Recent projects" (ADR-0243 Decision 1): the
//projects the artist worked on, each the ROM's enhancement folder. The list
//is derived, not stored as a new format: the project folders Remaster showed
//(a short MRU in RemasterConfig) first, then the projects of the recent games
//(the ADR-0049 sibling `<dir>/<Game>/`, else `EnhancementPacks/<Game>/`), so a
//project recorded before the MRU existed still lists. A folder that is gone,
//or no longer a project, is dropped. Host-free: BCL file system only.
public sealed record RemasterRecentProject(string Folder, string Name, int Recordings);

public static class RemasterRecentProjects
{
	//W-R0 draws two rows; five keep the screen within one page.
	public const int MaxShown = 5;
	public const int MaxRemembered = 10;

	//The MRU after `folder` was shown: it moves to the front, once, capped.
	//Returns the same list when it is already first (no needless config write).
	public static List<string> Remember(List<string> remembered, string folder)
	{
		if(string.IsNullOrEmpty(folder)) {
			return remembered;
		}
		if(remembered.Count > 0 && RemasterProjectLocator.SameFolder(remembered[0], folder)) {
			return remembered;
		}
		List<string> next = new() { folder };
		foreach(string f in remembered) {
			if(next.Count >= MaxRemembered) {
				break;
			}
			if(!string.IsNullOrEmpty(f) && !RemasterProjectLocator.SameFolder(f, folder)) {
				next.Add(f);
			}
		}
		return next;
	}

	//The ADR-0049 sibling of a ROM file, as MepPackManager::GetSiblingFolder
	//computes it: the file's folder plus its name without the extension (for
	//an archive, the archive's own path).
	public static string SiblingOf(string romPath)
	{
		if(string.IsNullOrEmpty(romPath)) {
			return "";
		}
		string? dir = Path.GetDirectoryName(romPath);
		string name = Path.GetFileNameWithoutExtension(romPath);
		return string.IsNullOrEmpty(dir) || name.Length == 0 ? "" : Path.Combine(dir, name);
	}

	public static IReadOnlyList<RemasterRecentProject> List(IReadOnlyList<string> remembered, IReadOnlyList<string> recentRoms, string packsFolder)
	{
		List<string> candidates = new(remembered);
		foreach(string rom in recentRoms) {
			string project = RemasterProjectLocator.ForGame(SiblingOf(rom), packsFolder);
			if(project.Length > 0) {
				candidates.Add(project);
			}
		}

		List<RemasterRecentProject> rows = new();
		List<string> seen = new();
		foreach(string folder in candidates) {
			if(rows.Count >= MaxShown) {
				break;
			}
			if(!RemasterProjectReader.IsProjectFolder(folder) || seen.Exists(s => RemasterProjectLocator.SameFolder(s, folder))) {
				continue;
			}
			seen.Add(folder);
			RemasterProjectInfo info = RemasterProjectReader.Read(folder);
			rows.Add(new RemasterRecentProject(info.Folder, info.Name, info.Recordings.Count));
		}
		return rows;
	}
}
