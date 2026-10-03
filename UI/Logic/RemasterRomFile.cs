using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mesen.Logic;

//#689: the Remaster jobs (kit, build, import) read --rom as the ROM file
//itself - artist_chr_kit.py refuses anything that is not an iNES file. A game
//opened from a .zip/.7z is the archive's inner file, so the jobs get that file
//written out under the temp folder: one folder per archive entry (two
//archives holding "game.nes" never share a file), named as the entry so the
//console still reads from its extension. Never into the project: Share
//packages the project, and a ROM must not travel with it.
public static class RemasterRomFile
{
	public const string FolderName = "MesenAI-remaster-rom";

	//path/innerFile: the opened file and the archive entry ("" = not an
	//archive); resource: the whole "<archive>\x1<inner>[\x1<index>]" string
	//the core opened; extract(resource, target) writes the entry to target.
	//Falls back to the archive when the entry cannot be written - the jobs then
	//fail on it with their own message, as they did before.
	public static string ForJobs(string path, string innerFile, string resource, string tempRoot, Func<string, string, bool> extract)
	{
		if(string.IsNullOrEmpty(innerFile)) {
			return path;
		}
		string folder = Path.Combine(tempRoot, FolderName, Key(resource));
		string target = Path.Combine(folder, Path.GetFileName(innerFile.Replace('\\', '/')));
		string part = target + ".part";
		try {
			Directory.CreateDirectory(folder);
			if(!extract(resource, part) || !File.Exists(part)) {
				File.Delete(part);
				return path;
			}
			//Replaced in one step: a job still reading the previous copy keeps it.
			File.Move(part, target, true);
			return target;
		} catch(IOException) {
			return path;
		} catch(UnauthorizedAccessException) {
			return path;
		}
	}

	private static string Key(string resource)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
		return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
	}
}
