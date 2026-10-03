using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.5 (PRD Part B §8, §13.5.2 W-P12-W-P14, W-P16, §13.5.5 W-X1/W-X2): the
	//host-free rules of the Play edge flows. W-P15 has its own file.
	public class FirstRunSheetTests
	{
		[Fact]
		public void Defaults_are_todays_wizard_defaults()
		{
			FirstRunChoice d = PlayFirstRun.Defaults;
			Assert.True(d.StoreInUserProfile);
			Assert.Equal(FirstRunKeyboard.ArrowKeys, d.Keyboard);
			Assert.True(d.CheckForUpdates);
			Assert.True(d.CreateShortcut);
		}

		[Theory]
		[InlineData(FirstRunKeyboard.ArrowKeys, false, true)]
		[InlineData(FirstRunKeyboard.Wasd, true, false)]
		public void Both_gamepad_presets_always_apply_and_the_keyboard_is_one_of_two(FirstRunKeyboard keyboard, bool wasd, bool arrows)
		{
			FirstRunMappings m = PlayFirstRun.Mappings(keyboard);
			Assert.True(m.Xbox);
			Assert.True(m.PlayStation);
			Assert.Equal(wasd, m.Wasd);
			Assert.Equal(arrows, m.Arrows);
		}

		[Fact]
		public void Desktop_options_exist_only_off_macOS_and_the_count_follows()
		{
			Assert.False(PlayFirstRun.ShowsDesktopOptions(isMacOS: true));
			Assert.True(PlayFirstRun.ShowsDesktopOptions(isMacOS: false));
			Assert.Equal(4, PlayFirstRun.ControlCount(isMacOS: true));
			Assert.Equal(6, PlayFirstRun.ControlCount(isMacOS: false));
			Assert.True(PlayFirstRun.ControlCount(isMacOS: false) <= 7);
		}

		[Fact]
		public void Closing_the_sheet_continues_with_what_is_selected()
		{
			FirstRunChoice picked = PlayFirstRun.Defaults with { StoreInUserProfile = false, Keyboard = FirstRunKeyboard.Wasd };
			Assert.Equal(picked, PlayFirstRun.OnDismiss(picked));
		}

		//#661: the close button and Esc apply the choice (and an unwritable
		//folder keeps the sheet); quitting the app or shutting the OS down
		//writes nothing and never blocks - the sheet shows again next launch.
		[Theory]
		[InlineData(false, true)]
		[InlineData(true, false)]
		public void Closing_applies_the_choice_unless_the_app_or_the_OS_shuts_down(bool shuttingDown, bool confirms)
		{
			Assert.Equal(confirms, PlayFirstRun.ConfirmsOnClose(shuttingDown));
		}
	}

	public class BiosPromptTests
	{
		[Theory]
		[InlineData("FDS", BiosKind.Fds)]
		[InlineData("StudyBox", BiosKind.Fds)]
		[InlineData("GameboyAdvance", BiosKind.GbaBios)]
		[InlineData("SmsBootRom", BiosKind.SmsBootRom)]
		[InlineData("GgBootRom", BiosKind.GgBootRom)]
		[InlineData("Gameboy", BiosKind.GameBoyBootRom)]
		[InlineData("GameboyColor", BiosKind.GameBoyBootRom)]
		[InlineData("DSP1", BiosKind.Other)]
		[InlineData("", BiosKind.Other)]
		public void Each_firmware_gets_its_own_sentence(string firmware, BiosKind expected)
		{
			Assert.Equal(expected, PlayBiosPrompt.Classify(firmware));
		}

		[Theory]
		[InlineData(8192L, "8 KB")]
		[InlineData(16384L, "16 KB")]
		[InlineData(256L, "256 bytes")]
		[InlineData(1536L, "1.5 KB")]
		[InlineData(262144L, "256 KB")]
		[InlineData(2097152L, "2 MB")]
		public void Sizes_read_in_plain_units(long bytes, string text)
		{
			Assert.Equal(text, PlayBiosPrompt.SizeText(bytes));
		}

		[Fact]
		public void A_file_of_a_known_size_is_accepted()
		{
			BiosSizeCheck ok = PlayBiosPrompt.CheckSize(8192, new long[] { 8192 });
			Assert.True(ok.Accepted);
			Assert.True(PlayBiosPrompt.CheckSize(0x4000, new long[] { 0x2000, 0x4000 }).Accepted);
		}

		[Fact]
		public void A_wrong_size_is_refused_with_the_expected_size_for_the_inline_line()
		{
			BiosSizeCheck bad = PlayBiosPrompt.CheckSize(16384, new long[] { 8192 });
			Assert.False(bad.Accepted);
			Assert.Equal(16384, bad.ActualSize);
			Assert.Equal(8192, bad.ExpectedSize);
		}

		[Fact]
		public void Size_check_with_no_known_size_accepts()
		{
			Assert.True(PlayBiosPrompt.CheckSize(123, Array.Empty<long>()).Accepted);
		}

		[Fact]
		public void The_sheet_has_three_controls()
		{
			Assert.Equal(3, PlayBiosPrompt.ControlCount);
		}
	}

	public class LoadFailureTests
	{
		[Theory]
		[InlineData("Contra.txt", false, false, false, LoadFailureCause.NotAGame)]
		[InlineData("Contra.nes", true, false, false, LoadFailureCause.Damaged)]
		[InlineData("Pack.zip", false, true, false, LoadFailureCause.ZipWithoutGame)]
		[InlineData("Pack.7z", false, true, false, LoadFailureCause.ZipWithoutGame)]
		[InlineData("Pack.zip", false, true, true, LoadFailureCause.Damaged)]
		public void One_sentence_per_cause(string file, bool knownGameExtension, bool isArchive, bool archiveHasGame, LoadFailureCause expected)
		{
			Assert.Equal(expected, PlayLoadFailure.Classify(file, knownGameExtension, isArchive, archiveHasGame));
		}

		[Fact]
		public void A_cancelled_bios_prompt_is_not_a_broken_file()
		{
			Assert.False(PlayLoadFailure.ShowsAlert(biosPromptCancelled: true));
			Assert.True(PlayLoadFailure.ShowsAlert(biosPromptCancelled: false));
		}

		[Fact]
		public void The_alert_stays_until_the_next_open_or_its_close_button()
		{
			PlayHomeAlert alert = new();
			alert.Show(LoadFailureCause.NotAGame, "Contra.txt");
			Assert.True(alert.IsVisible);
			Assert.Equal("Contra.txt", alert.FileName);
			alert.OnOpenStarted();
			Assert.False(alert.IsVisible);
			alert.Show(LoadFailureCause.Damaged, "Contra.nes");
			alert.Dismiss();
			Assert.False(alert.IsVisible);
		}

		//Under Remaster or Share the Play home is hidden: a failure there keeps
		//today's on-screen message instead of an alert nobody sees.
		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void Only_the_visible_Play_home_stays_through_a_load(bool playerMode, bool playWorkspace, bool expected)
		{
			Assert.Equal(expected, PlayLoadFailure.KeepsHomeDuringLoad(playerMode, playWorkspace));
		}
	}

	public class PackDepPromptTests
	{
		[Fact]
		public void Esc_on_the_pack_file_sheet_closes_back_to_the_overlay()
		{
			Assert.Equal(PlayEscAction.CloseSheetToOverlay, PlayEsc.Next(true, PlaySheet.PackDep, false));
		}

		[Fact]
		public void A_file_matches_by_content_hash_ignoring_case()
		{
			Assert.Equal(PackDepFileCheck.Accepted, PlayPackDepPrompt.Check("ABCDEF", "abcdef"));
			Assert.Equal(PackDepFileCheck.WrongFile, PlayPackDepPrompt.Check("abcdee", "abcdef"));
			//A dep with no declared hash is never prompted (CommunityPackDepPlan),
			//but if one reaches the sheet nothing can prove the file right.
			Assert.Equal(PackDepFileCheck.WrongFile, PlayPackDepPrompt.Check("abcdef", ""));
		}

		[Fact]
		public void Until_P9_adding_restarts_the_game()
		{
			Assert.Equal(PackDepPrimaryAction.AddAndRestart, PlayPackDepPrompt.PrimaryAction(appliesInPlace: false));
			Assert.Equal(PackDepPrimaryAction.Add, PlayPackDepPrompt.PrimaryAction(appliesInPlace: true));
			Assert.False(PlayPackDepPrompt.AppliesInPlace);
		}

		[Fact]
		public void The_sheet_opens_with_the_overlay_once_per_notice()
		{
			PackDepNoticeState state = new();
			Assert.False(state.ShouldOpenWithOverlay());
			state.Pending("Contra Arcade Music", 1);
			Assert.True(state.HasPending);
			Assert.True(state.ShouldOpenWithOverlay());
			state.MarkShown();
			Assert.False(state.ShouldOpenWithOverlay());
			Assert.True(state.HasPending);
			state.Pending("Contra Arcade Music", 1);
			Assert.True(state.ShouldOpenWithOverlay());
			state.Clear();
			Assert.False(state.HasPending);
			Assert.False(state.ShouldOpenWithOverlay());
		}

		[Fact]
		public void Copied_file_keeps_its_name_inside_the_drop_folder()
		{
			string target = PlayPackDepPrompt.TargetPath("/tmp/drop", "/Users/me/Music/arcade.zip");
			Assert.Equal(System.IO.Path.Combine("/tmp/drop", "arcade.zip"), target);
		}

		[Fact]
		public void The_sheet_has_four_controls()
		{
			Assert.Equal(4, PlayPackDepPrompt.ControlCount);
		}

		//The drop folder is shared by every pack: a same-named file is reused
		//when it is this one and never replaced when it is another.
		[Fact]
		public void A_same_named_file_in_the_drop_folder_is_reused_or_refused_never_replaced()
		{
			Assert.Equal(PackDepDropTarget.Copy, PlayPackDepPrompt.CheckTarget(false, "", "abcdef"));
			Assert.Equal(PackDepDropTarget.AlreadyThere, PlayPackDepPrompt.CheckTarget(true, "ABCDEF", "abcdef"));
			Assert.Equal(PackDepDropTarget.NameTaken, PlayPackDepPrompt.CheckTarget(true, "123456", "abcdef"));
		}

		//An install's late post for the previous game is dropped: another open
		//started, or the loaded ROM is not the one the install was for.
		[Fact]
		public void Pending_files_apply_only_to_the_load_the_install_was_for()
		{
			Assert.True(PlayPackDepPrompt.BelongsToCurrentLoad(3, 3, "AB12", "ab12"));
			Assert.False(PlayPackDepPrompt.BelongsToCurrentLoad(3, 4, "AB12", "AB12"));
			Assert.False(PlayPackDepPrompt.BelongsToCurrentLoad(3, 3, "AB12", "CD34"));
			Assert.False(PlayPackDepPrompt.BelongsToCurrentLoad(3, 3, "", ""));
		}
	}

	public class PlayStatusNoticeTests
	{
		[Fact]
		public void Without_a_game_the_notice_is_the_sentence()
		{
			Assert.Equal("Zelda needs the FDS BIOS", PlayStatusNotice.Compose("No game loaded", "Zelda needs the FDS BIOS", gameLoaded: false));
		}

		[Fact]
		public void With_a_game_the_notice_is_appended()
		{
			Assert.Equal("Contra · pack X · waiting for one file", PlayStatusNotice.Compose("Contra · pack X", "waiting for one file", gameLoaded: true));
		}

		[Fact]
		public void No_notice_keeps_the_sentence()
		{
			Assert.Equal("Contra", PlayStatusNotice.Compose("Contra", "", gameLoaded: true));
		}
	}
}
