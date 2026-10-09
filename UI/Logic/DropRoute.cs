using System;
using System.Collections.Generic;

namespace Mesen.Logic;

public enum DropAction
{
	//The drop carried no file path.
	Ignore,
	//The path is not a file (gone, or a folder without a pack manifest): say so.
	FileNotFound,
	//An IPS/BPS/UPS patch, told by its header: the ROM beside it opens patched.
	ApplyPatch,
	LoadState,
	//A movie, only while a game runs (it plays over the running game).
	PlayMovie,
	//#986: a pack archive or folder - installed through the pack window's path.
	InstallPack,
	//Anything else - a ROM, or an archive the ROM loader opens.
	LoadRom
}

//#986: the manifest that makes a dropped item a pack (DropRoute.FindPackManifest).
public enum PackManifest { None, Mep, HdLegacy }

//#993: what the window shows once a dropped pack's install returns.
public enum PackInstallOutcome { ShowError, ShowInstalled, OfferPowerCycle }

//#953: what a file dropped on the main window opens (MainWindow.OnDrop) and
//what LoadRomHelper.LoadFile does with a path - decided here over plain inputs
//so UI.Tests pins it; reading the file and acting on the answer stay in the
//window. #986: a pack is told by its manifest, found by the existing discovery
//rules (ADR-0040, ADR-0120 zip subfolder fallback) - pack.json (MEP) or
//hires.txt (legacy HD) at the root of a zip or of its single top-level folder,
//or at the root of a dropped folder; pack.json wins (ADR-0005). A zip with
//neither keeps the ROM loader, so a zipped ROM still loads. Every shape told
//as a pack here is one MepZipValidator accepts (#993 review).
public static class DropRoute
{
	private const string SaveStateExt = ".mss";
	public const string ZipExt = ".zip";
	private const string MepManifest = "pack.json";
	private const string HdManifest = "hires.txt";
	private const string MacOsxFolder = "__MACOSX/";
	private const string DsStore = ".DS_Store";
	private static readonly string[] MovieExts = { ".mmo", ".bk2", ".gbmv" };

	//How many leading bytes Decide needs to tell a patch.
	public const int HeaderLength = 5;

	public static DropAction Decide(string? path, bool fileExists, ReadOnlySpan<byte> header, bool isRunning, bool isFolder = false, IReadOnlyCollection<string>? packEntries = null)
	{
		if(string.IsNullOrEmpty(path)) {
			return DropAction.Ignore;
		}
		if(isFolder) {
			return packEntries != null && FindPackManifest(packEntries, true) != PackManifest.None ? DropAction.InstallPack : DropAction.FileNotFound;
		}
		if(!fileExists) {
			return DropAction.FileNotFound;
		}
		if(IsPatchHeader(header)) {
			return DropAction.ApplyPatch;
		}
		string ext = ExtensionOf(path);
		if(ext == SaveStateExt) {
			return DropAction.LoadState;
		}
		if(isRunning && Array.IndexOf(MovieExts, ext) >= 0) {
			return DropAction.PlayMovie;
		}
		if(ext == ZipExt && packEntries != null && FindPackManifest(packEntries, false) != PackManifest.None) {
			return DropAction.InstallPack;
		}
		return DropAction.LoadRom;
	}

	//entries: a zip's entry names, or the names directly inside a dropped
	//folder (rootOnly). A manifest at the zip's root wins; otherwise, when
	//every folder entry sits under one top-level folder, that folder is read.
	//Loose root files (a readme.txt) do not hide that folder, and Finder's
	//__MACOSX/ and .DS_Store are not entries at all (as MepContentId skips
	//them), so the zips MepZipValidator's ADR-0120 fallback accepts match.
	//hires.txt counts only with a .png beside it - the #161 rule the
	//validator applies. Convention-only packs (ADR-0049: audio/, synth/
	//without pack.json/hires.txt) are not told here by the rule decided on
	//#986; such a zip keeps the ROM loader.
	public static PackManifest FindPackManifest(IEnumerable<string> entries, bool rootOnly)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		HashSet<string> imageFolders = new(StringComparer.Ordinal);
		string? topFolder = null;
		bool singleTopFolder = true;
		foreach(string entry in entries) {
			string name = entry.Replace('\\', '/');
			if(IsFinderMetadata(name)) {
				continue;
			}
			names.Add(name);
			int lastSlash = name.LastIndexOf('/');
			if(name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
				imageFolders.Add(lastSlash > 0 ? name.Substring(0, lastSlash + 1) : "");
			}
			int slash = name.IndexOf('/');
			if(slash <= 0) {
				continue;
			}
			string top = name.Substring(0, slash + 1);
			if(topFolder != null && topFolder != top) {
				singleTopFolder = false;
			}
			topFolder ??= top;
		}

		PackManifest atRoot = ManifestIn(names, imageFolders, "");
		if(atRoot != PackManifest.None || rootOnly || !singleTopFolder || topFolder == null) {
			return atRoot;
		}
		return ManifestIn(names, imageFolders, topFolder);
	}

	private static PackManifest ManifestIn(HashSet<string> names, HashSet<string> imageFolders, string prefix)
	{
		if(names.Contains(prefix + MepManifest)) {
			return PackManifest.Mep;
		}
		return names.Contains(prefix + HdManifest) && imageFolders.Contains(prefix) ? PackManifest.HdLegacy : PackManifest.None;
	}

	private static bool IsFinderMetadata(string name)
	{
		return name.StartsWith(MacOsxFolder, StringComparison.Ordinal) || name == DsStore || name.EndsWith("/" + DsStore, StringComparison.Ordinal);
	}

	public static PackInstallOutcome AfterPackInstall(string error, bool isRunning)
	{
		if(error.Length > 0) {
			return PackInstallOutcome.ShowError;
		}
		return isRunning ? PackInstallOutcome.OfferPowerCycle : PackInstallOutcome.ShowInstalled;
	}

	//"PATCH" (IPS), "BPS1" or "UPS1".
	public static bool IsPatchHeader(ReadOnlySpan<byte> header)
	{
		if(header.Length < HeaderLength) {
			return false;
		}
		if(header[0] == 'P' && header[1] == 'A' && header[2] == 'T' && header[3] == 'C' && header[4] == 'H') {
			return true;
		}
		return (header[0] == 'U' || header[0] == 'B') && header[1] == 'P' && header[2] == 'S' && header[3] == '1';
	}

	private static string ExtensionOf(string path)
	{
		int dot = path.LastIndexOf('.');
		int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
		return dot > slash ? path.Substring(dot).ToLowerInvariant() : "";
	}
}
