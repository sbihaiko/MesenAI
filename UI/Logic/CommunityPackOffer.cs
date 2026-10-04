using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#736: an accepted community pack exists for the loaded ROM (a live row of
//docs/community-packs.json, matched like the auto-install does - ADR-0146,
//ADR-0145) but is not the pack the core renders. Play says so on W-P4's Pack
//row and in W-P6, and offers it. The offer never forces anything: auto-install
//off and a per-pack disable are the user's own choices (ADR-0146), so the pack
//is only turned on, chosen or installed when the player asks for it.

//The catalog row matched for the loaded ROM, reduced to what identifies the
//pack once installed. Container is the install's folder / DisabledPacks key
//(CommunityPackContainerName).
public sealed record CommunityPackCatalogPick(string Name, string PackId, string ContentId, string Container);

//What the window knows besides the pack list: the matched catalog row (from
//the on-disk catalog copy, no network), the ADR-0147 install registry's
//container for this ROM, and whether an install or Restore is running.
public sealed record CommunityPackOfferContext(CommunityPackCatalogPick? Pick, string? RegistryContainer, bool InstallInFlight);

public enum CommunityPackOfferReason
{
	None,
	//Installed, but the player turned it off (DisabledPacks).
	TurnedOff,
	//Installed and on, but another pack renders (a stored choice, or a pack
	//that precedes it).
	NotChosen,
	//Not installed, and AutoInstallCommunityPacks is off.
	AutoInstallOff,
	//Not installed although auto-install is on (a failed download, a renamed
	//or deleted folder until the next load).
	NotInstalled
}

public enum CommunityPackOfferAction
{
	None,
	//Re-enable the container, choose it for this ROM, apply.
	TurnOn,
	//Choose it for this ROM (P.3 preference), apply.
	Choose,
	//Install it for this ROM on request (the master switch stays as it is).
	Install
}

//Container: the installed container to turn on or choose ("" for Install).
//RenderedContainer: the pack rendering instead, when any.
public sealed record CommunityPackOffer(CommunityPackOfferReason Reason, string PackName, string Container, string? RenderedContainer)
{
	public static CommunityPackOffer None { get; } = new(CommunityPackOfferReason.None, "", "", null);

	public bool IsShown => Reason != CommunityPackOfferReason.None;

	public CommunityPackOfferAction Action => Reason switch {
		CommunityPackOfferReason.TurnedOff => CommunityPackOfferAction.TurnOn,
		CommunityPackOfferReason.NotChosen => CommunityPackOfferAction.Choose,
		CommunityPackOfferReason.AutoInstallOff => CommunityPackOfferAction.Install,
		CommunityPackOfferReason.NotInstalled => CommunityPackOfferAction.Install,
		_ => CommunityPackOfferAction.None
	};
}

public static class CommunityPackOfferRule
{
	public static CommunityPackCatalogPick Pick(CommunityPackCatalogEntry entry)
	{
		return new CommunityPackCatalogPick(entry.Name ?? "", entry.PackId ?? "", entry.ContentId ?? "", CommunityPackContainerName.Sanitize(entry.Name, entry.Game));
	}

	//packs: the core's pack list for the loaded ROM. renderedContainer: the
	//pack the core renders (PlayerPackPicker.CurrentContainer), null for none.
	public static CommunityPackOffer Decide(CommunityPackOfferContext context, IReadOnlyList<MepPackListEntry> packs, string? renderedContainer, bool mepPacksEnabled, bool autoInstallOn)
	{
		CommunityPackCatalogPick? pick = context.Pick;
		if(pick == null || !mepPacksEnabled) {
			return CommunityPackOffer.None;
		}

		MepPackListEntry? enabledCopy = null;
		MepPackListEntry? disabledCopy = null;
		foreach(MepPackListEntry entry in packs) {
			if(!IsCommunityPack(entry, pick, context.RegistryContainer)) {
				continue;
			}
			if(renderedContainer != null && string.Equals(entry.Container, renderedContainer, StringComparison.OrdinalIgnoreCase)) {
				return CommunityPackOffer.None;
			}
			if(entry.Enabled) {
				enabledCopy ??= entry;
			} else {
				disabledCopy ??= entry;
			}
		}

		if(enabledCopy != null) {
			return new CommunityPackOffer(CommunityPackOfferReason.NotChosen, pick.Name, enabledCopy.Container, renderedContainer);
		}
		if(disabledCopy != null) {
			return new CommunityPackOffer(CommunityPackOfferReason.TurnedOff, pick.Name, disabledCopy.Container, renderedContainer);
		}
		if(context.InstallInFlight) {
			//The W-P9 pill already says it is on its way.
			return CommunityPackOffer.None;
		}
		return new CommunityPackOffer(autoInstallOn ? CommunityPackOfferReason.NotInstalled : CommunityPackOfferReason.AutoInstallOff, pick.Name, "", renderedContainer);
	}

	//The window's entry point: derives the rendered pack from the ROM's
	//resolved preference, so W-P5's "No pack" (resolution.PrefersNoPack) is
	//honoured exactly as the picker and the pack detail do.
	public static CommunityPackOffer DecideForResolution(CommunityPackOfferContext context, IReadOnlyList<MepPackListEntry> packs, PackPreferenceResolver.Resolution resolution, bool mepPacksEnabled, bool autoInstallOn)
	{
		string? rendered = PlayerPackPicker.CurrentContainer(resolution.Candidates, resolution.PreferredContainer, resolution.PrefersNoPack);
		return Decide(context, packs, rendered, mepPacksEnabled, autoInstallOn);
	}

	//An installed container is the catalog's pack when its stamp carries the
	//row's content_id, or its pack_id ("owner/repo", or "owner/repo:slot" for
	//one slot of it - an edited install keeps it), or when it is the container
	//the install registry recorded for this ROM, or the install's own name.
	public static bool IsCommunityPack(MepPackListEntry entry, CommunityPackCatalogPick pick, string? registryContainer)
	{
		if(!string.IsNullOrEmpty(pick.ContentId) && string.Equals(entry.ContentId, pick.ContentId, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		if(!string.IsNullOrEmpty(pick.PackId) && !string.IsNullOrEmpty(entry.PackId)) {
			string id = entry.PackId;
			int slot = id.IndexOf(':');
			string owner = slot >= 0 ? id.Substring(0, slot) : id;
			if(string.Equals(id, pick.PackId, StringComparison.OrdinalIgnoreCase) || string.Equals(owner, pick.PackId, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}
		if(!string.IsNullOrEmpty(registryContainer) && string.Equals(entry.Container, registryContainer, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return !string.IsNullOrEmpty(pick.Container) && string.Equals(entry.Container, pick.Container, StringComparison.OrdinalIgnoreCase);
	}
}
