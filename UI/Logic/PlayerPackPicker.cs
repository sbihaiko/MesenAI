using System;
using System.Collections.Generic;

namespace Mesen.Logic
{
	//P.5 (PRD Part B §5): when the Player-mode pack picker opens for a
	//loaded ROM. The picker is the only place the player chooses among
	//competing packs - it never appears when a sibling-folder pack is present
	//(that pack always wins, §4 "artist at work"), when fewer than two distinct
	//pack_ids exist (the catalog/lexicographic slot applies; never ask 1.0 vs
	//1.2), or when an effective per-ROM preference already exists (silent apply).
	//Dismissing stores nothing, so the next launch asks again; picking stores
	//the per-ROM-sha1 preference (P.3) and applies on the power cycle.
	//
	//Host-free (BCL only) so UI.Tests dual-compiles it and exercises the
	//decision without Avalonia/EmuApi (ADR-0123).
	public static class PlayerPackPicker
	{
		public static bool ShouldOpen(bool hasSiblingPack, int distinctPackIdCount, bool hasEffectivePreference)
		{
			if(hasSiblingPack) {
				//§4: the sibling-folder pack always wins; no catalog auto-install,
				//no picker, while it is present.
				return false;
			}
			if(hasEffectivePreference) {
				//§5: a stored per-ROM choice applies silently on load.
				return false;
			}
			//§5: 2+ competing pack_ids (after the content_id merge) -> ask once.
			return distinctPackIdCount >= 2;
		}

		//Distinct effective pack_ids among the §5 content-merged candidates -
		//ADR-0140 `id`, or the rule-4 `local:<container>` fallback. Two
		//containers sharing a content_id are one pack, so this must be fed the
		//merged candidates (PackPreferenceResolver.Resolve's Candidates), never
		//the raw entry list, or a dropped copy of an installed pack would count
		//as a second choice.
		public static int DistinctPackIdCount(IReadOnlyList<PackPreferenceResolver.Candidate> candidates)
		{
			HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
			foreach(PackPreferenceResolver.Candidate c in candidates) {
				ids.Add(PackPreferenceResolver.DerivePackId(c));
			}
			return ids.Count;
		}

		//W-P5: the "No pack" row is offered whenever the picker lists a pack.
		public static bool OffersNoPack(int offeredPackCount) => offeredPackCount >= 1;

		//Whether the stored choice can be changed from W-P4/W-P6 (the picker
		//opens): 2+ packs to choose among, or "No pack" with a pack to go back
		//to. Never while a human sibling-folder pack wins (§4).
		public static bool CanChangeChoice(bool hasHumanSibling, int distinctPackIdCount, bool prefersNoPack)
		{
			if(hasHumanSibling) {
				return false;
			}
			return distinctPackIdCount >= 2 || (prefersNoPack && distinctPackIdCount >= 1);
		}

		//#693: the packs the player can choose among - the enabled ones. The
		//core never renders a disabled pack and the resolver never honors a
		//preference for one, so offering it stored a choice that reopened the
		//picker on every load. Filter before Resolve, so a disabled container
		//cannot swallow an enabled copy in the §5 content_id merge either.
		public static List<PackPreferenceResolver.Candidate> Offered(IEnumerable<PackPreferenceResolver.Candidate> candidates)
		{
			List<PackPreferenceResolver.Candidate> offered = new();
			foreach(PackPreferenceResolver.Candidate c in candidates) {
				if(c.Enabled) {
					offered.Add(c);
				}
			}
			return offered;
		}

		//#703: the pack the core renders (MepPackManager::GetPackForSection):
		//the stored choice when it resolves, else the first enabled pack in the
		//core's own order (ADR-0049: the sibling folder first, then ADR-0040's
		//lexicographic order) that has human content, else the first auto-only
		//one - so an accepted community pack wins over a bootstrap auto-only
		//sibling (ADR-0050). Never the picker's 👍-then-name display order.
		//coreOrder: the content-merged candidates, in pack-list order.
		//prefersNoPack: W-P5's "No pack" is stored - only a sibling-folder pack
		//still renders (MepPackManager::PreferenceAllowsPack).
		public static string? CurrentContainer(IReadOnlyList<PackPreferenceResolver.Candidate> coreOrder, string? preferredContainer, bool prefersNoPack = false)
		{
			if(preferredContainer != null && !prefersNoPack) {
				return preferredContainer;
			}
			string? autoOnlyFallback = null;
			foreach(PackPreferenceResolver.Candidate c in coreOrder) {
				if(!c.Enabled || (prefersNoPack && !c.IsSibling)) {
					continue;
				}
				if(!c.IsAutoOnly) {
					return c.Container;
				}
				autoOnlyFallback ??= c.Container;
			}
			return autoOnlyFallback;
		}
	}
}
