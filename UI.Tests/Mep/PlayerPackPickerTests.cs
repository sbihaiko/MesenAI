using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Mep
{
	//P.5 (PRD Part B §5): the Player picker decision - it opens only for
	//2+ competing pack_ids (after the content_id merge) with no effective
	//stored choice and no sibling-folder pack; it stays silent otherwise.
	public class PlayerPackPickerTests
	{
		private static PackPreferenceResolver.Candidate Candidate(string container, string packId = "", string contentId = "")
		{
			return new() { Container = container, PackId = packId, ContentId = contentId };
		}

		[Fact]
		public void TwoCompetingPackIds_NoPreference_Opens()
		{
			Assert.True(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: false, distinctPackIdCount: 2, hasEffectivePreference: false));
		}

		[Fact]
		public void SiblingPack_AlwaysSuppresses()
		{
			//§4: the sibling-folder pack always wins - no picker even with 2+ ids.
			Assert.False(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: true, distinctPackIdCount: 2, hasEffectivePreference: false));
		}

		[Fact]
		public void StoredPreference_SilentApply()
		{
			//§5: a stored per-ROM choice applies silently; the picker never re-opens.
			Assert.False(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: false, distinctPackIdCount: 2, hasEffectivePreference: true));
		}

		[Fact]
		public void SinglePackId_NeverAsks()
		{
			//§5: 1 pack_id (any number of revisions) applies the slot - never ask
			//1.0 vs 1.2.
			Assert.False(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: false, distinctPackIdCount: 1, hasEffectivePreference: false));
		}

		[Fact]
		public void StalePreference_AsksAgain()
		{
			//The stored pack_id matches no candidate -> no effective preference ->
			//the picker asks again (the game plays un-enhanced this session).
			Assert.True(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: false, distinctPackIdCount: 2, hasEffectivePreference: false));
		}

		[Fact]
		public void DistinctPackIdCount_DuplicateContentId_IsOnePack()
		{
			//A dropped copy of an installed pack shares its content_id -> one choice.
			var merged = new List<PackPreferenceResolver.Candidate> {
				Candidate("catalog-pack", packId: "contra80s", contentId: "AAA"),
				Candidate("dropped-copy", packId: "local:dropped-copy", contentId: "AAA")
			};
			//Fed the RAW list the count would be 2; fed the merged list it is 1.
			//Here both share content_id "AAA", so a caller that merged would see 1.
			var deduped = PackPreferenceResolver.Resolve(merged, null).Candidates;
			Assert.Single(deduped);
			Assert.Equal(1, PlayerPackPicker.DistinctPackIdCount(deduped));
		}

		[Fact]
		public void DistinctPackIdCount_TwoLocalContainers_IsTwo()
		{
			var merged = new List<PackPreferenceResolver.Candidate> {
				Candidate("pack-a", contentId: "AAA"),
				Candidate("pack-b", contentId: "BBB")
			};
			Assert.Equal(2, PlayerPackPicker.DistinctPackIdCount(merged));
		}


		[Fact]
		public void DistinctPackIdCount_StampedAndLocal_DifferentProducts()
		{
			//A stamped pack_id and an unrelated local drop are two choices.
			var merged = new List<PackPreferenceResolver.Candidate> {
				Candidate("stamped", packId: "contra80s", contentId: "AAA"),
				Candidate("local-drop", contentId: "BBB")
			};
			Assert.Equal(2, PlayerPackPicker.DistinctPackIdCount(merged));
		}

		// Issue #150: the §4 "sibling wins" rule targets a human-authored sibling.
		// A sibling produced exclusively by the F5 bootstrap (auto/ only, no
		// HasHuman) is not a user choice and must not suppress the picker.
		// The caller (BuildPackPickerData) sets hasSiblingPack = Source=="sibling" && !IsAutoOnly,
		// so the picker decision itself is still governed only by hasSiblingPack.
		[Fact]
		public void AutoOnlySibling_DoesNotSuppressPicker_WhenCallerSetsHasSiblingFalse()
		{
			//hasSiblingPack=false because the caller already excluded the auto-only
			//sibling from the hasSibling flag (Source=="sibling" && !IsAutoOnly).
			Assert.True(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: false, distinctPackIdCount: 2, hasEffectivePreference: false));
		}

		[Fact]
		public void HumanSibling_StillSuppressesPicker()
		{
			//A sibling with human content (IsAutoOnly=false) still sets hasSiblingPack=true.
			Assert.False(PlayerPackPicker.ShouldOpen(
				hasSiblingPack: true, distinctPackIdCount: 2, hasEffectivePreference: false));
		}

		//#693: the core never renders a disabled pack and the resolver never
		//honours a preference for one, so the picker neither offers nor counts it.
		[Fact]
		public void DisabledPack_IsNeitherOfferedNorCounted()
		{
			var candidates = new List<PackPreferenceResolver.Candidate> {
				Candidate("pack-a", packId: "issue-1"),
				new() { Container = "pack-b", PackId = "issue-2", Enabled = false }
			};
			List<PackPreferenceResolver.Candidate> offered = PlayerPackPicker.Offered(candidates);
			Assert.Equal(new[] { "pack-a" }, offered.ConvertAll(c => c.Container));
			Assert.Equal(1, PlayerPackPicker.DistinctPackIdCount(PackPreferenceResolver.Resolve(offered, null).Candidates));
		}

		//#693: a disabled container first in core order must not swallow an
		//enabled copy of the same content in the §5 merge.
		[Fact]
		public void DisabledPack_DoesNotShadowAnEnabledCopyInTheMerge()
		{
			var candidates = new List<PackPreferenceResolver.Candidate> {
				new() { Container = "copy-a", PackId = "issue-1", ContentId = "AAA", Enabled = false },
				Candidate("copy-b", packId: "issue-1", contentId: "AAA")
			};
			var merged = PackPreferenceResolver.Resolve(PlayerPackPicker.Offered(candidates), null).Candidates;
			Assert.Equal("copy-b", Assert.Single(merged).Container);
		}

		//#703: with no stored choice the core renders the first enabled pack in
		//its own order (ADR-0049: the sibling folder first) that has human
		//content, else the first auto-only one - never the most-voted one.
		[Fact]
		public void Current_IsTheHumanSibling_FirstInCoreOrder()
		{
			var coreOrder = new List<PackPreferenceResolver.Candidate> {
				Candidate("Contra"),
				Candidate("contra-community", packId: "issue-7")
			};
			Assert.Equal("Contra", PlayerPackPicker.CurrentContainer(coreOrder, preferredContainer: null));
		}

		//#703 / CLAUDE.md policy: an accepted community pack wins over a local
		//bootstrap auto-only sibling (ADR-0049, ADR-0050).
		[Fact]
		public void Current_SkipsAnAutoOnlySibling()
		{
			var coreOrder = new List<PackPreferenceResolver.Candidate> {
				new() { Container = "Contra", IsAutoOnly = true },
				Candidate("contra-community", packId: "issue-7")
			};
			Assert.Equal("contra-community", PlayerPackPicker.CurrentContainer(coreOrder, null));
		}

		[Fact]
		public void Current_FallsBackToTheAutoOnlyPack_AndFollowsAStoredChoice()
		{
			var autoOnly = new List<PackPreferenceResolver.Candidate> { new() { Container = "Contra", IsAutoOnly = true } };
			Assert.Equal("Contra", PlayerPackPicker.CurrentContainer(autoOnly, null));

			var coreOrder = new List<PackPreferenceResolver.Candidate> { Candidate("a"), Candidate("b") };
			Assert.Equal("b", PlayerPackPicker.CurrentContainer(coreOrder, "b"));
			Assert.Null(PlayerPackPicker.CurrentContainer(new List<PackPreferenceResolver.Candidate>(), null));
		}
	}
}
