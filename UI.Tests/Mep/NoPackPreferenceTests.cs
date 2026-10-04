using Mesen.Logic;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace Mesen.Tests.Mep
{
	//W-P5's "No pack" row (PRD Part B §13.5.2): an explicit per-ROM "no pack"
	//preference, stored in the same RomPackPreference map as a pack_id. The
	//sentinel must never equal a real pack_id (ADR-0140), the resolver and the
	//picker must treat it as an effective choice (silent next load, nothing
	//renders), only a human sibling-folder pack still wins (§4, ADR-0049), and
	//auto-install must not force a pack back on (ADR-0146: a user disable
	//overrides the blanket rule).
	public class NoPackPreferenceTests
	{
		private static PackPreferenceResolver.Candidate Pack(string container, string packId = "", string contentId = "") =>
			new() { Container = container, PackId = packId, ContentId = contentId };

		[Fact]
		public void The_sentinel_can_never_be_a_pack_id()
		{
			//Every ADR-0140 source starts with [a-z0-9]: the slug `id`,
			//`owner/repo[:game]`, `issue-{n}` and the rule-4 `local:<container>`.
			Regex packIdStart = new("^[a-z0-9]");
			Assert.DoesNotMatch(packIdStart, PackPreferenceResolver.NoPack);
			Assert.Equal(PackPreferenceResolver.NoPack, PackPreferenceResolver.NoPack.Trim().ToLowerInvariant());

			foreach(PackPreferenceResolver.Candidate c in new[] {
				Pack("none"), Pack(":none"), Pack("No Pack"), Pack("x", "none"), Pack("x", "tastic/contra80s:contra"), Pack("x", "issue-12")
			}) {
				string id = PackPreferenceResolver.DerivePackId(c);
				Assert.NotEqual(PackPreferenceResolver.NoPack, id);
				Assert.False(PackPreferenceResolver.IsNoPack(id));
			}
		}

		[Theory]
		[InlineData(":none", true)]
		[InlineData(" :NONE ", true)]
		[InlineData("none", false)]
		[InlineData("local:none", false)]
		[InlineData("", false)]
		[InlineData(null, false)]
		public void IsNoPack_reads_the_sentinel_only(string? preference, bool expected)
		{
			Assert.Equal(expected, PackPreferenceResolver.IsNoPack(preference));
		}

		[Fact]
		public void A_no_pack_preference_is_effective_and_picks_no_container()
		{
			var candidates = new List<PackPreferenceResolver.Candidate> { Pack("a", "issue-1"), Pack("b", "issue-2") };

			PackPreferenceResolver.Resolution resolution = PackPreferenceResolver.Resolve(candidates, PackPreferenceResolver.NoPack);

			Assert.True(resolution.PrefersNoPack);
			Assert.Null(resolution.PreferredContainer);
			Assert.True(resolution.HasEffectivePreference);
			//The packs are still listed: the picker offers them to change back.
			Assert.Equal(2, resolution.Candidates.Count);
			Assert.False(PlayerPackPicker.ShouldOpen(false, PlayerPackPicker.DistinctPackIdCount(resolution.Candidates), resolution.HasEffectivePreference));
		}

		[Fact]
		public void A_hand_written_stamp_claiming_the_sentinel_is_not_chosen()
		{
			var candidates = new List<PackPreferenceResolver.Candidate> { Pack("a", ":none"), Pack("b", "issue-2") };

			PackPreferenceResolver.Resolution resolution = PackPreferenceResolver.Resolve(candidates, PackPreferenceResolver.NoPack);

			Assert.Null(resolution.PreferredContainer);
			Assert.Null(PlayerPackPicker.CurrentContainer(resolution.Candidates, resolution.PreferredContainer, resolution.PrefersNoPack));
		}

		[Fact]
		public void A_pack_preference_is_not_no_pack()
		{
			var candidates = new List<PackPreferenceResolver.Candidate> { Pack("a", "issue-1"), Pack("b", "issue-2") };

			PackPreferenceResolver.Resolution resolution = PackPreferenceResolver.Resolve(candidates, "issue-2");

			Assert.False(resolution.PrefersNoPack);
			Assert.Equal("b", resolution.PreferredContainer);
			Assert.True(resolution.HasEffectivePreference);
			Assert.False(PackPreferenceResolver.Resolve(candidates, null).HasEffectivePreference);
		}

		[Fact]
		public void Under_no_pack_nothing_renders_but_a_human_sibling()
		{
			var installed = new List<PackPreferenceResolver.Candidate> { Pack("a", "issue-1"), Pack("b", "issue-2") };
			Assert.Null(PlayerPackPicker.CurrentContainer(installed, null, prefersNoPack: true));
			Assert.Equal("a", PlayerPackPicker.CurrentContainer(installed, null, prefersNoPack: false));

			//§4 / ADR-0049: the artist's folder beside the ROM always wins.
			var withSibling = new List<PackPreferenceResolver.Candidate> {
				new() { Container = "Contra", IsSibling = true },
				Pack("a", "issue-1")
			};
			Assert.Equal("Contra", PlayerPackPicker.CurrentContainer(withSibling, null, prefersNoPack: true));

			//An auto-only bootstrap sibling is a machine layer, not the artist's.
			var autoOnlySibling = new List<PackPreferenceResolver.Candidate> {
				new() { Container = "Contra", IsSibling = true, IsAutoOnly = true },
				Pack("a", "issue-1")
			};
			Assert.Equal("Contra", PlayerPackPicker.CurrentContainer(autoOnlySibling, null, prefersNoPack: true));
		}

		[Theory]
		[InlineData(0, false)]
		[InlineData(1, true)]
		[InlineData(2, true)]
		public void The_no_pack_row_is_offered_whenever_a_pack_is_listed(int packs, bool expected)
		{
			Assert.Equal(expected, PlayerPackPicker.OffersNoPack(packs));
		}

		[Theory]
		[InlineData(false, 2, false, true)]
		[InlineData(false, 1, false, false)]
		//"No pack" with one pack left: the way back to it is the picker.
		[InlineData(false, 1, true, true)]
		[InlineData(false, 0, true, false)]
		//§4: a human sibling always wins - nothing to choose.
		[InlineData(true, 2, true, false)]
		public void The_choice_can_be_changed_back_from_no_pack(bool sibling, int distinct, bool prefersNoPack, bool expected)
		{
			Assert.Equal(expected, PlayerPackPicker.CanChangeChoice(sibling, distinct, prefersNoPack));
			Assert.Equal(expected, PackDetail.Build(false, "", null, distinct, sibling, false, "", prefersNoPack).CanChangePack);
		}

		[Fact]
		public void The_no_pack_row_starts_selected_when_it_is_the_stored_choice()
		{
			string[] rows = { "a", "b", PackPreferenceResolver.NoPack };
			Assert.Equal(2, PackPickerRow.InitialSelection(rows, PackPreferenceResolver.NoPack));
			Assert.Equal(0, PackPickerRow.InitialSelection(rows, null));
		}

		[Theory]
		//The render's copy holds while enhanced audio is on; with it off, the
		//row must not promise it.
		[InlineData(true, "PackPickerNoPackDetail")]
		[InlineData(false, "PackPickerNoPackDetailOriginal")]
		public void The_no_pack_row_names_what_still_plays(bool enhancedAudio, string key)
		{
			Assert.Equal(key, PackPickerRow.NoPackDetailKey(enhancedAudio));
		}

		[Fact]
		public void Auto_install_skips_a_rom_whose_choice_is_no_pack()
		{
			Assert.Null(CommunityPackAutoInstallGate.SkipReason(autoInstallOn: true, containerDisabled: false, romPrefersNoPack: false));
			Assert.Equal("no pack chosen for this game", CommunityPackAutoInstallGate.SkipReason(true, false, romPrefersNoPack: true));
			Assert.Equal("pack disabled by user", CommunityPackAutoInstallGate.SkipReason(true, containerDisabled: true, false));
			Assert.Equal("AutoInstallCommunityPacks is off", CommunityPackAutoInstallGate.SkipReason(autoInstallOn: false, true, true));
		}
	}
}
