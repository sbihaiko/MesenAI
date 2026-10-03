using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.Logic;

//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P5/W-P6): the host-free rules of the
//two Play pack sheets - which one W-P4's Pack row opens, what a picker row
//says, which choice starts selected, and what the current-pack detail shows
//and lets the player do. The owning ViewModel formats the localized strings.

public enum PackRowTarget
{
	//W-P5: 2+ packs to choose from.
	Picker,
	//W-P6: the one pack (or none) to inspect.
	Detail
}

public static class PackRowRoute
{
	//W-P4: "The Pack row opens W-P5 when 2+ packs exist, or W-P6 to inspect the
	//one pack." A human sibling-folder pack always wins (§4), so there is
	//nothing to choose while it is there - the row inspects it.
	public static PackRowTarget For(int distinctPackIds, bool hasHumanSibling)
	{
		return !hasHumanSibling && distinctPackIds >= 2 ? PackRowTarget.Picker : PackRowTarget.Detail;
	}

	//#736: W-P6 holds the community-pack offer (CommunityPackOfferRule), so
	//with one the row inspects - the picker lists only installed, enabled packs.
	public static PackRowTarget For(int distinctPackIds, bool hasHumanSibling, bool hasCommunityOffer)
	{
		return hasCommunityOffer ? PackRowTarget.Detail : For(distinctPackIds, hasHumanSibling);
	}
}

public static class PackPickerRow
{
	//W-P5's second line: "by Tastic · 1.2 · textures, audio". A pack that names
	//no author reads "author unknown" (the catalog's "?" is a table convention,
	//not a sentence). License and ids moved to W-P6 (rule 3). byAuthorFormat is
	//the localized "by {0}".
	public static string Detail(string author, string version, string layers, string byAuthorFormat, string authorUnknown)
	{
		List<string> parts = new() {
			string.IsNullOrWhiteSpace(author) ? authorUnknown : string.Format(byAuthorFormat, author.Trim())
		};
		if(!string.IsNullOrWhiteSpace(version)) {
			parts.Add(version.Trim());
		}
		if(!string.IsNullOrWhiteSpace(layers)) {
			parts.Add(layers.Trim());
		}
		return string.Join(" · ", parts);
	}

	//W-P5's "No pack" second line: the render's "Play with enhanced audio
	//only" holds while enhanced audio is on; with it off, the row must not
	//promise it.
	public static string NoPackDetailKey(bool enhancedAudioOn) => enhancedAudioOn ? "PackPickerNoPackDetail" : "PackPickerNoPackDetailOriginal";

	//The radio that starts selected: the stored choice when it is one of the
	//rows (changing the choice later, from W-P4), else the first row - the list
	//is already in 👍-then-name order (P.6).
	public static int InitialSelection(IReadOnlyList<string> containers, string? preferredContainer)
	{
		if(containers.Count == 0) {
			return -1;
		}
		if(!string.IsNullOrEmpty(preferredContainer)) {
			for(int i = 0; i < containers.Count; i++) {
				if(string.Equals(containers[i], preferredContainer, StringComparison.OrdinalIgnoreCase)) {
					return i;
				}
			}
		}
		return 0;
	}
}

//#691: where W-P5's Use This Pack goes once the choice is stored.
public enum PackPickReturn
{
	//The on-load picker: the game it opened over keeps going as it was.
	Stay,
	//Opened from W-P4 and swapped in place: the game is still paused - back to W-P4.
	Overlay,
	//Opened from W-P4 but the game restarts: back to the game (W-P7's Apply rule).
	Game
}

public static class PackPickClose
{
	public static PackPickReturn After(bool fromOverlay, bool keepsPlace)
	{
		if(!fromOverlay) {
			return PackPickReturn.Stay;
		}
		return keepsPlace ? PackPickReturn.Overlay : PackPickReturn.Game;
	}
}

//MepPackListEntry.Source values (MepPackListParser).
public static class PackOrigin
{
	public const string Folder = "folder";
	public const string Zip = "zip";
	public const string Sibling = "sibling";
}

//W-P6's three layers (one switch each, PackLayerSwitches). Patch is not a
//pack section: it is a bundled ROM patch wired by the pack (PackAudioNotice's
//meaning), read from the folder.
public sealed record PackLayerChips(bool Textures, bool Audio, bool Patch);

public enum PackDetailNotice
{
	None,
	//ADR-0240 Option 1 in plain words: "N of M tracks have no audio file".
	MissingMusic
}

public sealed record PackDetailModel(
	bool HasPack,
	PackLayerChips Chips,
	PackDetailNotice Notice,
	int MissingTracks,
	int TotalTracks,
	bool CanChangePack,
	bool ShowsRestore,
	string Folder
);

public static class PackDetail
{
	//sections: the core's raw comma list ("textures,audio,border").
	public static PackLayerChips Chips(string sections, PackAudioScan? scan)
	{
		HashSet<string> present = new(StringComparer.OrdinalIgnoreCase);
		foreach(string part in (sections ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			present.Add(part);
		}
		//An HDNes-style pack plays its music from <bgm>/<sfx> lines next to its
		//textures, with no audio section: that is audio too (W-P6's switch).
		return new PackLayerChips(present.Contains("textures"), present.Contains("audio") || scan?.Total > 0, scan?.HasWiredPatch == true);
	}

	//W-P6's byline: "by Tastic · version 1.2 · CC BY-NC 4.0". byAuthor and
	//versionText format the localized "by {0}" and "version {0}".
	public static string Byline(string author, string version, string license, Func<string, string> byAuthor, string authorUnknown, Func<string, string> versionText)
	{
		List<string> parts = new() {
			string.IsNullOrWhiteSpace(author) ? authorUnknown : byAuthor(author.Trim())
		};
		if(!string.IsNullOrWhiteSpace(version)) {
			parts.Add(versionText(version.Trim()));
		}
		if(NamesLicense(license)) {
			parts.Add(license.Trim());
		}
		return string.Join(" · ", parts);
	}

	//The catalog install writes "license": "unknown" into pack.json when the
	//catalog row names none (CommunityPackCatalogEntry.LicenseOrUnknown) - a
	//placeholder, not a license, so the byline leaves it out like an empty one.
	public static bool NamesLicense(string license)
	{
		return !string.IsNullOrWhiteSpace(license) && !license.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase);
	}

	//W-P6's folder button: the render's "Show Pack in Finder" names macOS's
	//file browser; on Windows and Linux it is "Show Pack Folder".
	public static string ShowFolderLabelKey(bool isMacOS) => isMacOS ? "btnPackDetailShowInFinder" : "btnPackDetailShowFolder";

	//Where "Show pack folder" goes: a folder pack is EnhancementPacks/<container>,
	//a zip pack lives in EnhancementPacks itself, a sibling pack is the folder
	//next to the ROM. Empty when there is nothing to show.
	public static string FolderFor(string origin, string container, string packsFolder, string siblingFolder)
	{
		return origin switch {
			PackOrigin.Sibling => siblingFolder ?? "",
			PackOrigin.Zip => packsFolder ?? "",
			_ => string.IsNullOrEmpty(container) || string.IsNullOrEmpty(packsFolder) ? "" : Path.Combine(packsFolder, container)
		};
	}

	//The audio scan only reads a folder; a zip pack has none to read.
	public static bool CanScan(string origin) => origin != PackOrigin.Zip;

	//hasPack: a pack serves this game. distinctPackIds: after the §5 merge.
	//installedFromCatalog: the install registry holds a source sha256 for this
	//ROM - Restore (ADR-0147) re-downloads it, so a local or sibling pack has
	//nothing to restore from and the button is absent there, not disabled.
	//prefersNoPack: W-P5's "No pack" is stored - Change Pack… is the way back
	//even with one pack (PlayerPackPicker.CanChangeChoice).
	public static PackDetailModel Build(bool hasPack, string sections, PackAudioScan? scan, int distinctPackIds, bool hasHumanSibling, bool installedFromCatalog, string folder, bool prefersNoPack = false)
	{
		bool missingMusic = hasPack && scan != null && scan.ShowsNotice;
		return new PackDetailModel(
			hasPack,
			hasPack ? Chips(sections, scan) : new PackLayerChips(false, false, false),
			missingMusic ? PackDetailNotice.MissingMusic : PackDetailNotice.None,
			missingMusic ? scan!.Missing : 0,
			missingMusic ? scan!.Total : 0,
			PlayerPackPicker.CanChangeChoice(hasHumanSibling, distinctPackIds, prefersNoPack),
			hasPack && installedFromCatalog,
			folder ?? ""
		);
	}
}

//W-P6's Restore confirms once, in place (rule 7): the first press asks, the
//second runs it; anything else (Keep my files, Esc, closing) cancels.
public enum RestoreStep
{
	Idle,
	Confirming,
	Running
}

public static class RestoreFlow
{
	public static RestoreStep Press(RestoreStep step)
	{
		return step switch {
			RestoreStep.Idle => RestoreStep.Confirming,
			RestoreStep.Confirming => RestoreStep.Running,
			_ => step
		};
	}

	public static RestoreStep Cancel(RestoreStep step) => step == RestoreStep.Running ? step : RestoreStep.Idle;

	//#643: the restore downloads up to 300 MB; meanwhile the player may quit
	//and open another game. The restart that loads the restored files runs
	//only for the load the restore was for (the W-P16 rule,
	//PlayPackDepPrompt.BelongsToCurrentLoad).
	public static RestoreOutcome After(bool ok, int restoreOpenGeneration, int currentOpenGeneration, string restoreRomSha1, string currentRomSha1)
	{
		if(!ok) {
			return RestoreOutcome.Failed;
		}
		return PlayPackDepPrompt.BelongsToCurrentLoad(restoreOpenGeneration, currentOpenGeneration, restoreRomSha1, currentRomSha1) ? RestoreOutcome.PowerCycle : RestoreOutcome.Stale;
	}
}

public enum RestoreOutcome
{
	Failed,
	//Another game was opened (or the game was quit) during the restore: the
	//files are restored, but the game now loaded is left alone.
	Stale,
	//Restart the game so it loads the restored files - the confirm said so.
	PowerCycle
}
