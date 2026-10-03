using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//W-P6 (PRD Part B §13.5.2): the current pack's three layers - Textures, Audio
//and its ROM patch - each with a switch that turns it off for this game only.
//Stored per ROM sha1 beside the per-ROM pack choice
//(EnhancementPackConfig.RomLayersOff, next to RomPackPreference) and pushed to
//the core (MepPackManager::SetRomLayersOff), which then neither serves the
//section nor applies the patch for that ROM. The global defaults (Remaster ⋯ ›
//Enhancement Packs: Textures, Music, ROM patch) still apply on top: either one off
//turns the layer off.
public enum PackLayer
{
	Textures,
	Audio,
	Patch
}

//Why a W-P6 switch cannot be flipped, shown under it.
public enum PackLayerNote
{
	None,
	//The pack has no such layer (the render's grey chip).
	NotInPack,
	//The global switch has it off for every game.
	OffEverywhere
}

public sealed record PackLayerSwitch(bool Present, bool On, bool Enabled, PackLayerNote Note);

public static class PackLayerSwitches
{
	//The core's words (MepPackManager::ParseRomLayersOff).
	public static string Key(PackLayer layer) => layer switch {
		PackLayer.Textures => "textures",
		PackLayer.Audio => "audio",
		_ => "patch"
	};

	public static bool IsOn(IReadOnlyCollection<string>? layersOff, PackLayer layer)
	{
		return layersOff == null || !layersOff.Contains(Key(layer), StringComparer.OrdinalIgnoreCase);
	}

	//Turns one layer on or off for one ROM. A ROM with every layer back on
	//loses its key, like a removed pack choice.
	public static void Set(Dictionary<string, List<string>> layersOffByRom, string romSha1, PackLayer layer, bool on)
	{
		if(string.IsNullOrWhiteSpace(romSha1)) {
			return;
		}
		layersOffByRom.TryGetValue(romSha1, out List<string>? off);
		List<string> next = (off ?? new()).Where(k => !k.Equals(Key(layer), StringComparison.OrdinalIgnoreCase)).ToList();
		if(!on) {
			next.Add(Key(layer));
		}
		if(next.Count == 0) {
			layersOffByRom.Remove(romSha1);
		} else {
			layersOffByRom[romSha1] = next;
		}
	}

	public static string ToCoreList(IEnumerable<string>? layersOff)
	{
		return layersOff == null ? "" : string.Join(",", layersOff.Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct());
	}

	//One W-P6 row. present: the pack has the layer (PackLayerChips).
	//globalOn: its global switch. romOn: this game's switch. applying: a
	//change is being applied (the switches wait for it).
	public static PackLayerSwitch Row(bool present, bool globalOn, bool romOn, bool applying)
	{
		if(!present) {
			return new PackLayerSwitch(false, false, false, PackLayerNote.NotInPack);
		}
		if(!globalOn) {
			return new PackLayerSwitch(true, false, false, PackLayerNote.OffEverywhere);
		}
		return new PackLayerSwitch(true, romOn, !applying, PackLayerNote.None);
	}
}
