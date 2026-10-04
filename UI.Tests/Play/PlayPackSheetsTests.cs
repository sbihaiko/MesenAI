using System;
using System.IO;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.4 (PRD Part B §8, §13.5.2 W-P5/W-P6): which pack sheet W-P4's Pack row
	//opens, what a picker row says, and what the current-pack detail offers.
	public class PlayPackSheetsTests
	{
		[Theory]
		[InlineData(2, false, PackRowTarget.Picker)]
		[InlineData(3, false, PackRowTarget.Picker)]
		[InlineData(1, false, PackRowTarget.Detail)]
		[InlineData(0, false, PackRowTarget.Detail)]
		//§4: a human sibling pack always wins - nothing to choose, inspect it.
		[InlineData(2, true, PackRowTarget.Detail)]
		public void The_pack_row_opens_the_picker_only_with_a_choice(int distinct, bool sibling, PackRowTarget expected)
		{
			Assert.Equal(expected, PackRowRoute.For(distinct, sibling));
		}

		[Fact]
		public void A_picker_row_reads_by_author_version_and_layers()
		{
			Assert.Equal("by Tastic · 1.2 · textures, audio", PackPickerRow.Detail("Tastic", "1.2", "textures, audio", "by {0}", "author unknown"));
		}

		[Fact]
		public void A_pack_naming_no_author_reads_author_unknown()
		{
			Assert.Equal("author unknown · textures", PackPickerRow.Detail("  ", "", "textures", "by {0}", "author unknown"));
		}

		[Fact]
		public void The_stored_choice_starts_selected_else_the_first_row()
		{
			string[] rows = { "/p/a", "/p/b" };
			Assert.Equal(1, PackPickerRow.InitialSelection(rows, "/P/B"));
			Assert.Equal(0, PackPickerRow.InitialSelection(rows, null));
			Assert.Equal(0, PackPickerRow.InitialSelection(rows, "/p/gone"));
			Assert.Equal(-1, PackPickerRow.InitialSelection(new string[0], "/p/a"));
		}

		[Fact]
		public void The_detail_byline_reads_author_version_and_license()
		{
			Assert.Equal("by Tastic · version 1.2 · CC BY-NC 4.0", PackDetail.Byline("Tastic", "1.2", "CC BY-NC 4.0", a => "by " + a, "author unknown", v => "version " + v));
		}

		//The catalog install writes "license": "unknown" into pack.json when the
		//catalog row names none (CommunityPackCatalogEntry.LicenseOrUnknown); the
		//byline read it back as a bare trailing "unknown".
		[Theory]
		[InlineData("unknown")]
		[InlineData(" Unknown ")]
		[InlineData("unspecified")]
		[InlineData(" Unspecified ")]
		[InlineData("")]
		public void A_pack_naming_no_license_leaves_it_out_of_the_byline(string license)
		{
			Assert.Equal("author unknown · version 1.0.0", PackDetail.Byline("", "1.0.0", license, a => "by " + a, "author unknown", v => "version " + v));
		}

		[Fact]
		public void Chips_come_from_the_sections_and_the_wired_patch()
		{
			Assert.Equal(new PackLayerChips(true, true, false), PackDetail.Chips("textures,audio,border", null));
			Assert.Equal(new PackLayerChips(false, true, true), PackDetail.Chips("audio", new PackAudioScan(0, 4, true)));
		}

		//W-P6's Audio switch: an HDNes-style pack's <bgm> lines sit beside its
		//textures, with no audio section - its music is still the pack's audio.
		[Fact]
		public void Tracks_next_to_the_textures_count_as_audio()
		{
			Assert.Equal(new PackLayerChips(true, true, false), PackDetail.Chips("textures", new PackAudioScan(0, 3, false)));
			Assert.Equal(new PackLayerChips(true, false, false), PackDetail.Chips("textures", new PackAudioScan(0, 0, false)));
		}

		[Fact]
		public void The_folder_follows_where_the_pack_lives()
		{
			Assert.Equal(Path.Combine("/packs", "Contra 80s"), PackDetail.FolderFor(PackOrigin.Folder, "Contra 80s", "/packs", "/roms/Contra"));
			Assert.Equal("/packs", PackDetail.FolderFor(PackOrigin.Zip, "Contra 80s", "/packs", "/roms/Contra"));
			Assert.Equal("/roms/Contra", PackDetail.FolderFor(PackOrigin.Sibling, "Contra", "/packs", "/roms/Contra"));
			Assert.False(PackDetail.CanScan(PackOrigin.Zip));
			Assert.True(PackDetail.CanScan(PackOrigin.Folder));
		}

		//ADR-0147: a sibling MEP pack roots its human layer at mep/ (the sibling
		//of auto/), so the folder button opens mep/; a legacy sibling without one
		//still opens the sibling root.
		[Fact]
		public void The_sibling_folder_follows_the_mep_layer()
		{
			string mep = NewTempSibling("pack.json");
			string bare = NewTempSibling(null);
			try {
				Assert.Equal(Path.Combine(mep, "mep"), PackDetail.FolderFor(PackOrigin.Sibling, "Contra", "/packs", mep));
				Assert.Equal(bare, PackDetail.FolderFor(PackOrigin.Sibling, "Contra", "/packs", bare));
			} finally {
				Directory.Delete(mep, true);
				Directory.Delete(bare, true);
			}
		}

		//A throwaway sibling folder; probe != null writes <sibling>/mep/<probe>.
		private static string NewTempSibling(string? mepProbe)
		{
			string sibling = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mep-layer-" + Guid.NewGuid().ToString("N"))).FullName;
			if(mepProbe != null) {
				string mep = Directory.CreateDirectory(Path.Combine(sibling, "mep")).FullName;
				File.WriteAllText(Path.Combine(mep, mepProbe), "{}");
			}
			return sibling;
		}

		//ADR-0240 Option 1, shown where it is useful: missing music only when a
		//wired patch redeems it (the install notice's own rule).
		[Fact]
		public void Missing_music_is_shown_with_its_counts_only_under_the_notice_rule()
		{
			PackDetailModel shown = PackDetail.Build(true, "textures,audio", new PackAudioScan(3, 17, true), 1, false, true, "/f");
			Assert.Equal(PackDetailNotice.MissingMusic, shown.Notice);
			Assert.Equal((3, 17), (shown.MissingTracks, shown.TotalTracks));

			PackDetailModel noPatch = PackDetail.Build(true, "audio", new PackAudioScan(3, 17, false), 1, false, true, "/f");
			Assert.Equal(PackDetailNotice.None, noPatch.Notice);
		}

		//Restore (ADR-0147) needs the catalog install record: a local or sibling
		//pack has nothing to restore from, so the button is absent there.
		[Fact]
		public void Restore_is_offered_only_for_a_catalog_install()
		{
			Assert.True(PackDetail.Build(true, "textures", null, 1, false, installedFromCatalog: true, "/f").ShowsRestore);
			Assert.False(PackDetail.Build(true, "textures", null, 1, false, installedFromCatalog: false, "/f").ShowsRestore);
			Assert.False(PackDetail.Build(false, "", null, 0, false, installedFromCatalog: true, "").ShowsRestore);
		}

		//#691: Use This Pack from W-P4 returns to W-P4 when the pack swaps in
		//place (the game is still paused), and to the game when it restarts -
		//the same split as W-P7's Apply. From the on-load picker it goes nowhere.
		[Theory]
		[InlineData(true, true, PackPickReturn.Overlay)]
		[InlineData(true, false, PackPickReturn.Game)]
		[InlineData(false, true, PackPickReturn.Stay)]
		[InlineData(false, false, PackPickReturn.Stay)]
		public void Using_a_pack_returns_where_the_picker_came_from(bool fromOverlay, bool keepsPlace, PackPickReturn expected)
		{
			Assert.Equal(expected, PackPickClose.After(fromOverlay, keepsPlace));
		}

		[Fact]
		public void Change_pack_works_only_when_there_is_a_choice()
		{
			Assert.False(PackDetail.Build(true, "textures", null, 1, false, false, "/f").CanChangePack);
			Assert.True(PackDetail.Build(true, "textures", null, 2, false, false, "/f").CanChangePack);
			Assert.False(PackDetail.Build(true, "textures", null, 2, true, false, "/f").CanChangePack);
		}

		[Fact]
		public void No_pack_shows_no_chips_and_no_notice()
		{
			PackDetailModel none = PackDetail.Build(false, "textures", new PackAudioScan(2, 2, true), 0, false, false, "");
			Assert.False(none.HasPack);
			Assert.Equal(new PackLayerChips(false, false, false), none.Chips);
			Assert.Equal(PackDetailNotice.None, none.Notice);
		}

		//Rule 7: Restore confirms once, in place.
		[Fact]
		public void Restore_asks_once_then_runs_and_cancel_backs_out()
		{
			Assert.Equal(RestoreStep.Confirming, RestoreFlow.Press(RestoreStep.Idle));
			Assert.Equal(RestoreStep.Running, RestoreFlow.Press(RestoreStep.Confirming));
			Assert.Equal(RestoreStep.Running, RestoreFlow.Press(RestoreStep.Running));
			Assert.Equal(RestoreStep.Idle, RestoreFlow.Cancel(RestoreStep.Confirming));
			Assert.Equal(RestoreStep.Running, RestoreFlow.Cancel(RestoreStep.Running));
		}

		//#643: the restore downloads up to 300 MB; the game it restarts must be
		//the one it restored for - no open since it started, same MEP ROM SHA-1.
		[Fact]
		public void A_finished_restore_power_cycles_only_the_load_it_was_for()
		{
			Assert.Equal(RestoreOutcome.PowerCycle, RestoreFlow.After(ok: true, restoreOpenGeneration: 3, currentOpenGeneration: 3, restoreRomSha1: "AB12", currentRomSha1: "ab12"));
			Assert.Equal(RestoreOutcome.Failed, RestoreFlow.After(ok: false, 3, 3, "AB12", "AB12"));
			Assert.Equal(RestoreOutcome.Stale, RestoreFlow.After(true, restoreOpenGeneration: 3, currentOpenGeneration: 4, "AB12", "AB12"));
			Assert.Equal(RestoreOutcome.Stale, RestoreFlow.After(true, 3, 3, "AB12", currentRomSha1: "CD34"));
			Assert.Equal(RestoreOutcome.Stale, RestoreFlow.After(true, 3, 3, "AB12", currentRomSha1: ""));
			Assert.Equal(RestoreOutcome.Stale, RestoreFlow.After(true, 3, 3, restoreRomSha1: "", currentRomSha1: ""));
		}
	
		//W-P6: macOS names the Finder (the render); elsewhere a folder.
		[Theory]
		[InlineData(true, "btnPackDetailShowInFinder")]
		[InlineData(false, "btnPackDetailShowFolder")]
		public void The_folder_button_names_the_platforms_file_browser(bool isMacOS, string key)
		{
			Assert.Equal(key, PackDetail.ShowFolderLabelKey(isMacOS));
		}

		//The Patch chip is lit by a patch alone, with or without missing music.
		[Fact]
		public void The_patch_chip_follows_the_patch_not_the_music_notice()
		{
			Assert.True(PackDetail.Chips("audio", new PackAudioScan(0, 4, true)).Patch);
			Assert.False(PackDetail.Chips("audio", new PackAudioScan(3, 17, false)).Patch);
			Assert.False(PackDetail.Build(true, "audio", new PackAudioScan(3, 17, false), 1, false, true, "/f").Chips.Patch);
		}

		//The local automatic upscale (F5 bootstrap, an auto-only folder pack) is
		//not a pack someone made: no author, version or license, and its
		//recorded music fingerprints/MIDI are not tracks to play.
		[Fact]
		public void The_automatic_layer_has_textures_but_no_music_to_switch()
		{
			Assert.Equal(new PackLayerChips(true, false, false), PackDetail.Chips("textures,audio", null, automatic: true));
			Assert.Equal(new PackLayerChips(true, false, false), PackDetail.Chips("textures,audio", new PackAudioScan(0, 0, false), automatic: true));
		}

		[Fact]
		public void The_automatic_layer_shows_music_only_when_it_has_playable_tracks()
		{
			Assert.True(PackDetail.Chips("textures,audio", new PackAudioScan(2, 5, false), automatic: true).Audio);
		}

		[Fact]
		public void The_detail_model_flags_the_automatic_layer()
		{
			PackDetailModel model = PackDetail.Build(true, "textures,audio", null, 1, false, false, "/f", automatic: true);
			Assert.True(model.IsAutomatic);
			Assert.False(model.Chips.Audio);
			Assert.False(PackDetail.Build(true, "textures,audio", null, 1, false, false, "/f").IsAutomatic);
		}

		[Fact]
		public void The_automatic_layer_byline_names_the_game_and_how_it_was_made()
		{
			Assert.Equal("Dr. Mario (1990) (Nintendo)\nMade on this computer from what you played", PackDetail.AutoByline("Dr. Mario (1990) (Nintendo)", null, s => "Made (" + s + ")", "Made on this computer from what you played"));
			Assert.Equal("Dr. Mario\nMade (xBRZ 4×)", PackDetail.AutoByline("Dr. Mario", "xBRZ 4×", s => "Made (" + s + ")", "Made"));
		}

		[Fact]
		public void The_automatic_layer_is_named_by_what_it_is_never_by_the_rom()
		{
			Assert.Equal("Automatic upscale", PackDetail.DisplayName("Dr. Mario (1990) (Nintendo)", autoOnly: true, "Automatic upscale"));
			Assert.Equal("Contra 80s", PackDetail.DisplayName("Contra 80s", autoOnly: false, "Automatic upscale"));
		}
}
}
