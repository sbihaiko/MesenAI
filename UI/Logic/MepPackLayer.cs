using System;
using System.IO;

namespace Mesen.Logic;

//ADR-0147: a MesenCE container roots its human layer at mep/ (the sibling of
//auto/), while a legacy sibling folder and a central
//EnhancementPacks/<container> keep the pack at the container root. The pack
//detail sheet's folder button and the audio scan both need the folder that
//holds the pack files, never the container root. Host-free (BCL only), so
//UI.Tests covers it; the rule mirrors MepPackManager::HasSiblingMepPack.
public static class MepPackLayer
{
	public const string FolderName = "mep";

	//The human layer's convention probes, relative to it: the core's
	//MepPack::DetectConventionLayout set (the bare hires.txt/border.png at the
	//layer root included) plus pack.json, which alone makes mep/ the human layer.
	private static readonly string[] Probes = {
		"pack.json",
		"hires.txt",
		"border.png",
		"textures/hires.txt",
		"audio/hires.txt",
		"audio/fingerprints.json",
		"synth/preset.cfg",
		"border/border.png",
	};

	//The folder holding the pack under containerRoot: <root>/mep when that is
	//the human layer, else containerRoot itself (legacy layout). Best effort,
	//like the callers.
	public static string Resolve(string? containerRoot)
	{
		if(string.IsNullOrEmpty(containerRoot)) {
			return "";
		}
		return IsMepLayer(containerRoot) ? Path.Combine(containerRoot, FolderName) : containerRoot;
	}

	//An empty mep/ folder is not the human layer (the core's rule): it must hold
	//pack.json or a convention probe.
	public static bool IsMepLayer(string containerRoot)
	{
		try {
			string mep = Path.Combine(containerRoot, FolderName);
			if(!Directory.Exists(mep)) {
				return false;
			}
			foreach(string probe in Probes) {
				if(File.Exists(Path.Combine(mep, probe.Replace('/', Path.DirectorySeparatorChar)))) {
					return true;
				}
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
		}
		return false;
	}
}
