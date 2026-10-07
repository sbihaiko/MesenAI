using System;
using System.Collections.Generic;
using System.IO;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//G.5 (PRD Part B §8, §13.5.2 W-P12-W-P14, W-P16, §13.5.5 W-X1/W-X2): the
	//host-free rules of the Play edge flows. W-P15 has its own file.
	//
	//ADR-0256 Decision 8: the first-run sheet's host-free rules are down to the
	//choice itself (W-P12's two questions), the storage switch the Settings
	//surface performs and the startup rule the wizard used to be. The sheet's own
	//rules - dismissing, closing, the desktop checkboxes and their count - left
	//with the SetupWizardWindow.
	public class FirstRunChoiceTests
	{
		[Fact]
		public void Defaults_are_todays_wizard_defaults()
		{
			FirstRunChoice d = PlayFirstRun.Defaults;
			Assert.True(d.StoreInUserProfile);
			Assert.Equal(FirstRunKeyboard.ArrowKeys, d.Keyboard);
			//#672: off while the fork has no update feed (UpdateChannelTests)
			Assert.Equal(UpdateChannel.HasFeed, d.CheckForUpdates);
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

		//ADR-0256 Decision 8: the Settings surface's keyboard row reads the
		//running config's DefaultKeyMappings, the other way round from Mappings.
		[Theory]
		[InlineData(false, false, FirstRunKeyboard.ArrowKeys)]
		[InlineData(false, true, FirstRunKeyboard.ArrowKeys)]
		[InlineData(true, false, FirstRunKeyboard.Wasd)]
		//Both flags is not a state the first run writes; the default is what the
		//row shows rather than guessing which half won.
		[InlineData(true, true, FirstRunKeyboard.ArrowKeys)]
		public void The_keyboard_row_reads_the_preset_the_config_is_on(bool wasdKeys, bool arrowKeys, FirstRunKeyboard expected)
		{
			Assert.Equal(expected, PlayFirstRun.KeyboardOf(wasdKeys, arrowKeys));
		}

		[Theory]
		[InlineData(true, "/home/me/.config/MesenAI")]
		[InlineData(false, "/opt/mesen")]
		public void A_storage_choice_names_the_folder_the_settings_file_moves_to(bool storeInUserProfile, string expected)
		{
			Assert.Equal(expected, PlayFirstRun.Folder(storeInUserProfile, "/home/me/.config/MesenAI", "/opt/mesen"));
		}

		[Theory]
		[InlineData("/home/me/.config/MesenAI", true)]
		[InlineData("/opt/mesen", false)]
		//The folder next to the app, spelled the way Windows does.
		[InlineData("/Opt/Mesen", false)]
		public void The_storage_row_opens_on_the_folder_the_process_runs_from(string currentFolder, bool inUserFolder)
		{
			Assert.Equal(inUserFolder, PlayFirstRun.InUserFolder(currentFolder, "/opt/mesen"));
		}

		//ADR-0256 Decision 8: the home folder is resolved once, before the main
		//window exists, so a folder the process is not on can only take effect
		//after the relaunch the wizard's flow ended with. The folder the process
		//is already on is not a change.
		[Theory]
		[InlineData("/home/me/.config/MesenAI", "/opt/mesen", true)]
		[InlineData("/home/me/.config/MesenAI", "/home/me/.config/MesenAI", false)]
		[InlineData("/home/me/.config/MesenAI", "/Home/Me/.config/MesenAI", false)]
		[InlineData("/home/me/.config/MesenAI", "/opt/mesen/", true)]
		public void A_storage_switch_needs_the_relaunch_and_the_same_folder_does_not(string current, string chosen, bool needsRestart)
		{
			Assert.Equal(needsRestart, PlayFirstRun.NeedsRestart(current, chosen));
		}
	}

	//ADR-0256 Decision 8: what the startup path is made of, now that the wizard
	//is out of it. Not a rule inside the app (Program.Main starts a real
	//application), so it reads the entry point itself - the same way
	//HudToastStyleRuleTests reads the Core's header and the UI's mirror.
	public class FirstRunStartupTests
	{
		//The wizard's other job: DependencyHelper.ExtractNativeDependencies
		//unpacks MesenCore.* next to the settings file, and it used to run only
		//in the branch that show the wizard, before the core could exist. On the
		//normal path it has to run too - and nothing may return between the home
		//folder being fixed and it running, or a fresh install boots without the
		//core.
		[Fact]
		public void The_startup_path_extracts_the_native_dependencies_before_it_can_return()
		{
			string program = ReadSource("UI", "Program.cs");
			int home = program.IndexOf("Environment.CurrentDirectory = ConfigManager.HomeFolder;", StringComparison.Ordinal);
			int extract = program.IndexOf("DependencyHelper.ExtractNativeDependencies(ConfigManager.HomeFolder)", home, StringComparison.Ordinal);
			Assert.True(home >= 0, "Program.Main no longer fixes the working directory on the home folder");
			Assert.True(extract > home, "Program.Main no longer extracts the native dependencies on the normal startup path");

			string before = program[home..extract];
			Assert.DoesNotContain("return ", before);
			Assert.DoesNotContain("StartWithClassicDesktopLifetime", before);
		}

		//The wizard left the startup path: no window is built for it, no flag
		//makes one the app's first window, and the class it lived in is gone.
		[Fact]
		public void No_setup_wizard_stands_before_the_main_window()
		{
			//Comments name the retired window (that is what they are for), so the
			//assertions read the code the compiler sees.
			string program = Code(ReadSource("UI", "Program.cs"));
			Assert.DoesNotContain("SetupWizardWindow", program);
			Assert.DoesNotContain("ShowConfigWindow", program);

			string app = Code(ReadSource("UI", "App.axaml.cs"));
			Assert.DoesNotContain("SetupWizardWindow", app);
			Assert.DoesNotContain("ShowConfigWindow", app);
			//The one window the app opens is the main one.
			Assert.Contains("desktop.MainWindow = new MainWindow();", app);
		}

		private static string Code(string source)
		{
			System.Collections.Generic.List<string> lines = new();
			foreach(string line in source.Split('\n')) {
				int comment = line.IndexOf("//", StringComparison.Ordinal);
				lines.Add(comment < 0 ? line : line[..comment]);
			}
			return string.Join('\n', lines);
		}

		private static string ReadSource(params string[] parts)
		{
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			Assert.NotNull(dir);
			return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
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
		public void Adding_the_file_applies_in_place_wherever_P9_does()
		{
			Assert.Equal(PackDepPrimaryAction.AddAndRestart, PlayPackDepPrompt.PrimaryAction(appliesInPlace: false));
			Assert.Equal(PackDepPrimaryAction.Add, PlayPackDepPrompt.PrimaryAction(appliesInPlace: true));
			//ADR-0244: the consoles measured exact keep the player's place.
			Assert.Equal(PackDepPrimaryAction.Add, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Nes, false, false)));
			Assert.Equal(PackDepPrimaryAction.Add, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Sms, false, false)));
			Assert.Equal(PackDepPrimaryAction.Add, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Gameboy, false, false)));
		}

		[Fact]
		public void Adding_the_file_restarts_where_P9_requires_it()
		{
			//A movie or shared replay, netplay, or a console never measured.
			Assert.Equal(PackDepPrimaryAction.AddAndRestart, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Nes, true, false)));
			Assert.Equal(PackDepPrimaryAction.AddAndRestart, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Nes, false, true)));
			Assert.Equal(PackDepPrimaryAction.AddAndRestart, PlayPackDepPrompt.PrimaryAction(PackChangePolicy.Plan(ConsoleType.Snes, false, false)));
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
