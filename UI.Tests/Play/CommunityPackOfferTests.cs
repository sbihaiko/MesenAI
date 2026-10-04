using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#736: Play says when an accepted community pack exists for the loaded ROM
	//but is not the one rendering - turned off, not chosen, or not installed
	//(auto-install off is the user's choice: offered, never forced) - and what
	//"Use Community Pack" does about it.
	public class CommunityPackOfferTests
	{
		//The catalog row for Contra (docs/community-packs.json, issue #137).
		private static readonly CommunityPackCatalogPick Contra = new(
			"Contra (USA) — community submission", "tastichacks/contra80s",
			"a2d644ff4994c8c5bc7e40eebfe754744c3fdcbc0dc87798e91d726b673f6dc7",
			"Contra (USA) — community submission");

		private static MepPackListEntry Entry(string container, string packId = "", string contentId = "", bool enabled = true, bool autoOnly = false, string source = PackOrigin.Folder)
		{
			return new MepPackListEntry {
				Container = container, Name = container, PackId = packId, ContentId = contentId,
				Enabled = enabled, IsAutoOnly = autoOnly, Source = source, Sections = "textures"
			};
		}

		//The installed community pack as the core lists it: MEP-ized into the
		//ROM's sibling mep/, stamped with the install's pack_id/content_id.
		private static MepPackListEntry InstalledContra(bool enabled = true)
		{
			return Entry("Contra (1988) (Konami)", "tastichacks/contra80s:contra-usa", Contra.ContentId, enabled, source: PackOrigin.Sibling);
		}

		private static readonly MepPackListEntry Bootstrap = Entry("Contra (1988) (Konami) auto", autoOnly: true, source: PackOrigin.Sibling);

		private static CommunityPackOffer Decide(CommunityPackCatalogPick? pick, IReadOnlyList<MepPackListEntry> packs, string? rendered,
			bool autoInstall = true, bool mepPacks = true, bool inFlight = false, string? registryContainer = null)
		{
			return CommunityPackOfferRule.Decide(new CommunityPackOfferContext(pick, registryContainer, inFlight), packs, rendered, mepPacks, autoInstall);
		}

		[Fact]
		public void The_issue_repro_offers_to_install_when_auto_install_is_off()
		{
			//mep/ renamed mep.off, AutoInstallCommunityPacks=false, the bootstrap's
			//xBRZ layer renders instead.
			CommunityPackOffer offer = Decide(Contra, new[] { Bootstrap }, Bootstrap.Container, autoInstall: false);

			Assert.Equal(CommunityPackOfferReason.AutoInstallOff, offer.Reason);
			Assert.Equal(CommunityPackOfferAction.Install, offer.Action);
			Assert.Equal(Contra.Name, offer.PackName);
			Assert.True(offer.IsShown);
		}

		[Fact]
		public void Not_installed_with_auto_install_on_is_offered_unless_the_install_is_running()
		{
			Assert.Equal(CommunityPackOfferReason.NotInstalled, Decide(Contra, new[] { Bootstrap }, Bootstrap.Container).Reason);
			//The W-P9 pill already says it is on its way.
			Assert.False(Decide(Contra, new[] { Bootstrap }, Bootstrap.Container, inFlight: true).IsShown);
		}

		[Fact]
		public void Nothing_to_say_when_the_community_pack_is_the_one_rendering()
		{
			MepPackListEntry contra = InstalledContra();
			Assert.False(Decide(Contra, new[] { contra, Bootstrap }, contra.Container).IsShown);
			Assert.False(Decide(Contra, new[] { contra, Bootstrap }, contra.Container, autoInstall: false).IsShown);
		}

		[Fact]
		public void The_rendering_pack_is_recognised_by_pack_id_when_the_content_changed()
		{
			//An edited install keeps its stamp's pack_id ("owner/repo:slot"),
			//whose owner/repo is the catalog's pack_id.
			MepPackListEntry edited = Entry("Contra (1988) (Konami)", "TasticHacks/Contra80s:contra-usa", "edited", source: PackOrigin.Sibling);
			Assert.False(Decide(Contra, new[] { edited }, edited.Container).IsShown);
		}

		[Fact]
		public void The_rendering_pack_is_recognised_by_the_install_registry_container()
		{
			MepPackListEntry stampless = Entry("Contra 80s");
			Assert.False(Decide(Contra, new[] { stampless }, stampless.Container, registryContainer: "contra 80s").IsShown);
		}

		[Fact]
		public void A_turned_off_community_pack_is_offered_to_turn_back_on()
		{
			MepPackListEntry contra = InstalledContra(enabled: false);
			CommunityPackOffer offer = Decide(Contra, new[] { contra, Bootstrap }, Bootstrap.Container);

			Assert.Equal(CommunityPackOfferReason.TurnedOff, offer.Reason);
			Assert.Equal(CommunityPackOfferAction.TurnOn, offer.Action);
			Assert.Equal(contra.Container, offer.Container);
		}

		[Fact]
		public void An_installed_community_pack_shadowed_by_another_pack_is_offered_to_choose()
		{
			MepPackListEntry contra = InstalledContra();
			MepPackListEntry local = Entry("My xBRZ", "local:my xbrz");
			CommunityPackOffer offer = Decide(Contra, new[] { contra, local }, local.Container, autoInstall: false);

			Assert.Equal(CommunityPackOfferReason.NotChosen, offer.Reason);
			Assert.Equal(CommunityPackOfferAction.Choose, offer.Action);
			Assert.Equal(contra.Container, offer.Container);
			Assert.Equal(local.Container, offer.RenderedContainer);
		}

		[Fact]
		public void An_enabled_copy_wins_over_a_turned_off_one()
		{
			MepPackListEntry off = Entry("Contra copy", "tastichacks/contra80s", Contra.ContentId, enabled: false);
			MepPackListEntry on = InstalledContra();
			MepPackListEntry local = Entry("My xBRZ");
			CommunityPackOffer offer = Decide(Contra, new[] { off, on, local }, local.Container);

			Assert.Equal(CommunityPackOfferAction.Choose, offer.Action);
			Assert.Equal(on.Container, offer.Container);
		}

		[Fact]
		public void Nothing_is_offered_without_a_catalog_row_or_with_packs_off()
		{
			Assert.False(Decide(null, new[] { Bootstrap }, Bootstrap.Container).IsShown);
			Assert.False(Decide(Contra, new[] { Bootstrap }, Bootstrap.Container, mepPacks: false).IsShown);
			Assert.Equal(CommunityPackOfferAction.None, CommunityPackOffer.None.Action);
		}

		[Fact]
		public void An_unrelated_local_pack_is_not_taken_for_the_community_pack()
		{
			MepPackListEntry other = Entry("Contra Remix", "someone/contra-remix", "c-other");
			CommunityPackOffer offer = Decide(Contra, new[] { other }, other.Container);
			Assert.Equal(CommunityPackOfferReason.NotInstalled, offer.Reason);
		}

		[Fact]
		public void The_pick_carries_the_install_container_name()
		{
			CommunityPackCatalogEntry row = new() { Name = "Contra: 80s / USA", Game = "Contra (USA)", PackId = "tastichacks/contra80s", ContentId = "c1" };
			CommunityPackCatalogPick pick = CommunityPackOfferRule.Pick(row);
			Assert.Equal("Contra: 80s / USA", pick.Name);
			Assert.Equal(CommunityPackContainerName.Sanitize(row.Name, row.Game), pick.Container);
			Assert.Equal("tastichacks/contra80s", pick.PackId);
			Assert.Equal("c1", pick.ContentId);
		}

		[Theory]
		//W-P6 holds the offer, so with one the Pack row inspects instead of
		//opening the picker (which lists only installed, enabled packs).
		[InlineData(2, false, true, PackRowTarget.Detail)]
		[InlineData(2, false, false, PackRowTarget.Picker)]
		[InlineData(1, false, true, PackRowTarget.Detail)]
		public void An_offer_routes_the_pack_row_to_the_detail(int distinct, bool sibling, bool offer, PackRowTarget expected)
		{
			Assert.Equal(expected, PackRowRoute.For(distinct, sibling, offer));
		}
	
		//The player picked "No pack" for the ROM: only a sibling still renders
		//(PlayerPackPicker.CurrentContainer, MepPackManager::PreferenceAllowsPack),
		//so an enabled non-sibling community pack is NOT rendering and is offered.
		[Fact]
		public void No_pack_choice_makes_a_non_sibling_community_pack_an_offer()
		{
			MepPackListEntry contra = Entry("Contra community", "tastichacks/contra80s:contra-usa", Contra.ContentId, source: PackOrigin.Folder);
			PackPreferenceResolver.Resolution resolution = PackPreferenceResolver.Resolve(new[] {
				new PackPreferenceResolver.Candidate { Container = contra.Container, PackId = contra.PackId, ContentId = contra.ContentId, Enabled = true, IsSibling = false }
			}, PackPreferenceResolver.NoPack);
			Assert.True(resolution.PrefersNoPack);

			CommunityPackOffer offer = CommunityPackOfferRule.DecideForResolution(
				new CommunityPackOfferContext(Contra, null, false), new[] { contra }, resolution, true, true);

			Assert.Equal(CommunityPackOfferReason.NotChosen, offer.Reason);
			Assert.Equal(contra.Container, offer.Container);
			Assert.Null(offer.RenderedContainer);
		}
	}
}
