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
		public void Chips_come_from_the_sections_and_the_wired_patch()
		{
			Assert.Equal(new PackLayerChips(true, true, false), PackDetail.Chips("textures,audio,border", null));
			Assert.Equal(new PackLayerChips(false, true, true), PackDetail.Chips("audio", new PackAudioScan(0, 4, true)));
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
	}
}
