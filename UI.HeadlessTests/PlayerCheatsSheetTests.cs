using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.10 (ADR-0245 §1-§3, PRD Part B §13 W-P4/W-P11): the Cheats row in today's
//player overlay and the sheet it opens. The rules (rows per hash, search,
//not-in-list fallback, recording-art refusal, console scope) are pinned
//host-free in UI.Tests/Cheats; this checks the crossing into XAML: the row
//shows "N on" from the stored list, the sheet replaces the overlay, a toggle
//clicked in the sheet is the state the classic cheat window loads, a refused
//Game Genie row is disabled with its reason on screen, and Esc closes the
//sheet back to the overlay (rule 8).
//
//MainWindow's constructor calls EmuApi.InitDll(), so these run only where the
//native core is built (NativeCore). The stored list is the real CheatCodes file
//of the current game under the portable test home (TestAppBuilder); each test
//removes it before and after.
[Collection(NativeCoreCollection.Name)]
public class PlayerCheatsSheetTests : IDisposable
{
	private static string CheatFile => Path.Combine(ConfigManager.CheatFolder, EmuApi.GetRomInfo().GetRomName() + ".json");

	public PlayerCheatsSheetTests()
	{
		if(NativeCore.IsAvailable) {
			DeleteCheatFile();
		}
	}

	public void Dispose()
	{
		if(NativeCore.IsAvailable) {
			DeleteCheatFile();
		}
	}

	private static void DeleteCheatFile()
	{
		if(File.Exists(CheatFile)) {
			File.Delete(CheatFile);
		}
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlayer(ConsoleType console)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Cheats.DisableAllCheats = false;
		MainWindow window = new();
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		//G.2: Esc opens the pause overlay only over a loaded game (PlayEsc), so the
		//stand-in RomInfo carries a format as well as the console.
		model.RomInfo = new RomInfo() { ConsoleType = console, Format = console == ConsoleType.Gameboy ? RomFormat.Gb : RomFormat.iNes };
		//R.4: no test reaches the network; the community catalog is fixed here.
		model.CommunityCheatsLastKnown = () => Array.Empty<CommunityCheatGame>();
		model.CommunityCheatsSource = () => Task.FromResult<IReadOnlyList<CommunityCheatGame>?>(Array.Empty<CommunityCheatGame>());
		return (window, model);
	}

	private static void Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static string[] VisibleTexts(Control root) => root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();

	[AvaloniaFact]
	public void The_overlay_cheats_row_shows_how_many_cheats_are_on()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		new CheatCodes() {
			Cheats = new List<CheatCode> {
				new() { Description = "a", Type = CheatType.NesCustom, Codes = "0032:09", Enabled = true },
				new() { Description = "b", Type = CheatType.NesCustom, Codes = "0033:09", Enabled = true },
				new() { Description = "c", Type = CheatType.NesCustom, Codes = "0034:09", Enabled = false },
			}
		}.Save();

		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
		Assert.True(window.FindNamed<Button>("OverlayCheatsButton").IsOnScreen());
		Assert.Equal("2 on", window.FindNamed<TextBlock>("OverlayCheatsSummary").Text);
	}

	//The stop condition: a toggle clicked in W-P11 and the classic cheat window
	//show the same state. Drives the real database (CheatDb.Nes.json) through
	//the not-in-list fallback - the test copy has no cheat hash in the list.
	[AvaloniaFact]
	public void A_toggle_in_the_sheet_is_the_state_the_classic_cheat_window_loads()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Click(window.FindNamed<Button>("OverlayCheatsButton"));

		Assert.True(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());
		Assert.False(window.IsPauseCardActive());
		Assert.Equal(CheatSheet.NotInListLine, window.FindNamed<TextBlock>("CheatsStatusLine").Text);

		//Search by game name, pick the game: its codes are listed, marked.
		window.FindNamed<TextBox>("CheatsSearchBox").Text = "Contra (USA)";
		Dispatcher.UIThread.RunJobs();
		Button game = window.FindNamed<ItemsControl>("CheatsGameResults").FindAll<Button>().First(b => (b.Content as string) == "Contra (USA)");
		Click(game);

		CheckBox first = window.FindNamed<ItemsControl>("CheatsList").FindAll<CheckBox>().First();
		Assert.Contains(CheatSheet.AnotherCopyMark, VisibleTexts(window.FindNamed<ItemsControl>("CheatsList")));
		Assert.False(first.IsChecked);
		string description = first.Content as string ?? "";

		first.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<ItemsControl>("CheatsList").FindAll<CheckBox>().First().IsChecked);
		CheatListWindowViewModel classic = new();
		CheatCode stored = Assert.Single(classic.Cheats);
		Assert.Equal(description, stored.Description);
		Assert.True(stored.Enabled);
	}

	//ADR-0245 §3: in the recording-art context (the Remaster game view passes
	//recordingArt: true) a Game Genie row is disabled with its reason, a RAM row is not.
	[AvaloniaFact]
	public void While_recording_art_a_game_genie_row_is_disabled_with_its_reason_on_screen()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		CheatDbGame contra = new("Contra (USA)", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
			new CheatDbCode("Invincibility (star effect)", "00B0:FF"),
		});

		model.CheatsSheet.Open(ConsoleType.Nes, contra.Sha1, new[] { contra }, Array.Empty<StoredCheat>(), recordingArt: true, disableAll: false, _ => { });
		Dispatcher.UIThread.RunJobs();

		ItemsControl list = window.FindNamed<ItemsControl>("CheatsList");
		CheckBox[] boxes = list.FindAll<CheckBox>().ToArray();
		Assert.Equal(2, boxes.Length);
		Assert.False(boxes[0].IsEffectivelyEnabled);
		Assert.True(boxes[1].IsEffectivelyEnabled);
		Assert.Contains(CheatRecordingRule.RefusedReason, VisibleTexts(list));
		//W-P11's last row: the source, then the recording note.
		Assert.Contains(CheatSheet.FromListMark + " · " + CheatRecordingRule.AllowedNote, VisibleTexts(list));
	}

	//ADR-0245 §5, rule 4: GB has no list - the sheet still opens, the search is
	//disabled (not hidden) and the reason is written; Add a Code… stays.
	[AvaloniaFact]
	public void On_game_boy_the_sheet_offers_manual_entry_only_with_the_reason()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Gameboy);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Click(window.FindNamed<Button>("OverlayCheatsButton"));

		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		Assert.True(search.IsOnScreen());
		Assert.False(search.IsEnabled);
		Assert.Equal(CheatConsoleScope.NoListReason, window.FindNamed<TextBlock>("CheatsStatusLine").Text);

		Click(window.FindNamed<Button>("CheatsAddCodeButton"));
		window.FindNamed<TextBox>("CheatsNewCodeBox").Text = "01FF16D0";
		Click(window.FindNamed<Button>("CheatsAddCodeConfirm"));

		CheckBox added = Assert.Single(window.FindNamed<ItemsControl>("CheatsList").FindAll<CheckBox>());
		Assert.True(added.IsChecked);
		Assert.Equal(CheatType.GbGameShark, Assert.Single(CheatCodes.LoadCheatCodes().Cheats).Type);
	}

	//Rule 8: Esc closes the sheet back to the overlay; the next Esc resumes.
	[AvaloniaFact]
	public void Esc_closes_the_sheet_back_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("OverlayCheatsButton"));
		Assert.True(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//R.4 (ADR-0248 §2, §5): the community rows of docs/community-cheats.json.
	private const string CopySha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string GbSha1 = "1111111111111111111111111111111111111111";

	private static readonly CommunityCheatGame[] CommunityCatalog = {
		new(CopySha1, "Contra (USA)", new[] {
			new CommunityCheat(201, "nes", "0436:09", "Start with 9 lives", 2),
			new CommunityCheat(202, "nes", "SXIOPO", "Infinite energy", 12),
		}),
		new(GbSha1, "Test GB game", new[] { new CommunityCheat(204, "gb", "01FF16D0", "Infinite health", 1) }),
	};

	private static readonly CheatDbGame BundledContra = new("Contra (USA)", CopySha1, new[] { new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG") });

	private static string[] RowDescriptions(Window window) => window.FindNamed<ItemsControl>("CheatsList").FindAll<CheckBox>().Select(c => c.Content as string ?? "").ToArray();

	//The stop rule, client half: a valid issue's code shows for the matching
	//copy, below the bundled list, marked with its votes; the count opens the issue.
	[AvaloniaFact]
	public void Community_rows_show_below_the_bundled_list_and_the_count_opens_the_issue()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		List<string> opened = new();

		model.CheatsSheet.Open(ConsoleType.Nes, CopySha1, new[] { BundledContra }, Array.Empty<StoredCheat>(), recordingArt: false, disableAll: false, _ => { },
			gameName: "Contra (USA)", openUrl: opened.Add, community: CommunityCatalog);
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new[] { "Infinite lives - 1P game", "Infinite energy", "Start with 9 lives" }, RowDescriptions(window));
		ItemsControl list = window.FindNamed<ItemsControl>("CheatsList");
		Assert.Contains(CommunityCheatCatalog.CommunityMark, VisibleTexts(list));
		Button[] votes = list.FindAll<Button>().Where(b => b.Name == "CheatsVotesButton" && b.IsOnScreen()).ToArray();
		Assert.Equal(new[] { "👍 12 ↗", "👍 2 ↗" }, votes.Select(b => b.Content as string));
		//Bundled and community rows are already shared: no share action on screen.
		Assert.DoesNotContain(list.FindAll<Button>(), b => b.Name == "CheatsShareButton" && b.IsOnScreen());

		Click(votes[0]);
		Assert.Equal(new[] { "https://github.com/sbihaiko/MesenAI/issues/202" }, opened);
	}

	//...and not for another copy: the overlay opens the sheet with no game
	//running (no cheat hash), so no catalog row matches, by name or otherwise.
	[AvaloniaFact]
	public void Community_rows_never_show_for_another_copy()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.CommunityCheatsLastKnown = () => CommunityCatalog;
		model.CommunityCheatsSource = () => Task.FromResult<IReadOnlyList<CommunityCheatGame>?>(CommunityCatalog);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Click(window.FindNamed<Button>("OverlayCheatsButton"));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());
		Assert.Empty(RowDescriptions(window));
		Assert.Equal(CheatSheet.NotInListLine, window.FindNamed<TextBlock>("CheatsStatusLine").Text);

		model.CheatsSheet.Open(ConsoleType.Nes, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", new[] { BundledContra }, Array.Empty<StoredCheat>(), false, false, _ => { }, community: CommunityCatalog);
		Dispatcher.UIThread.RunJobs();
		Assert.Empty(RowDescriptions(window));
	}

	//The fetch returns after the sheet opened: its rows join the list in place.
	[AvaloniaFact]
	public void A_catalog_fetched_after_the_sheet_opened_adds_its_rows()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.CheatsSheet.Open(ConsoleType.Nes, CopySha1, new[] { BundledContra }, Array.Empty<StoredCheat>(), false, false, _ => { });
		Dispatcher.UIThread.RunJobs();
		Assert.Single(RowDescriptions(window));

		model.CheatsSheet.SetCommunityCatalog(CommunityCatalog);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(3, RowDescriptions(window).Length);
	}

	//GB has no bundled list: community rows appear, and the "no list yet"
	//line shows only when there are none.
	[AvaloniaFact]
	public void On_game_boy_community_rows_replace_the_no_list_line()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Gameboy);
		model.CheatsSheet.Open(ConsoleType.Gameboy, GbSha1, Array.Empty<CheatDbGame>(), Array.Empty<StoredCheat>(), false, false, _ => { }, community: CommunityCatalog);
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new[] { "Infinite health" }, RowDescriptions(window));
		Assert.NotEqual(CheatConsoleScope.NoListReason, window.FindNamed<TextBlock>("CheatsStatusLine").Text);
		Assert.True(window.FindNamed<TextBox>("CheatsSearchBox").IsEnabled);

		model.CheatsSheet.Open(ConsoleType.Gameboy, "2222222222222222222222222222222222222222", Array.Empty<CheatDbGame>(), Array.Empty<StoredCheat>(), false, false, _ => { }, community: CommunityCatalog);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(CheatConsoleScope.NoListReason, window.FindNamed<TextBlock>("CheatsStatusLine").Text);
	}

	//ADR-0248 §2: a code the user added carries *Share This Cheat ↗*, which
	//opens the pre-filled `[Cheat]` form.
	[AvaloniaFact]
	public void Share_this_cheat_opens_the_prefilled_form_for_the_users_own_code()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		List<string> opened = new();
		StoredCheat mine = new("Start with 30 lives", CheatType.NesCustom, "0032:1D", true);
		model.CheatsSheet.Open(ConsoleType.Nes, CopySha1, new[] { BundledContra }, new[] { mine }, false, false, _ => { },
			gameName: "Contra (USA)", openUrl: opened.Add, community: CommunityCatalog, romFile: "Contra (USA).nes");
		Dispatcher.UIThread.RunJobs();

		Button share = Assert.Single(window.FindNamed<ItemsControl>("CheatsList").FindAll<Button>(), b => b.Name == "CheatsShareButton" && b.IsOnScreen());
		Assert.Equal("Share This Cheat ↗", share.Content as string);
		Assert.Equal("Start with 30 lives", (share.DataContext as PlayerCheatRow)?.Description);
		Click(share);

		Assert.Equal(new[] { CheatShare.BuildIssueUrl(CopySha1, "Contra (USA)", ConsoleType.Nes, "Contra (USA).nes", "0032:1D", "Start with 30 lives") }, opened);
		Assert.Contains("template=cheat-code.yml", opened[0]);
	}

	//#662: the sheet hands the running game's file name to the share form, so
	//a GBC game on the Game Boy core pre-fills "Game Boy Color".
	[AvaloniaFact]
	public void Share_this_cheat_prefills_game_boy_color_for_a_gbc_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Gameboy);
		List<string> opened = new();
		StoredCheat mine = new("Max lines", CheatType.GbGameShark, "01FF34C1", true);
		model.CheatsSheet.Open(ConsoleType.Gameboy, GbSha1, Array.Empty<CheatDbGame>(), new[] { mine }, false, false, _ => { },
			gameName: "Tetris DX (World)", openUrl: opened.Add, romFile: "Tetris DX (World).gbc");
		Dispatcher.UIThread.RunJobs();

		Click(Assert.Single(window.FindNamed<ItemsControl>("CheatsList").FindAll<Button>(), b => b.Name == "CheatsShareButton" && b.IsOnScreen()));

		Assert.Contains("&console=Game%20Boy%20Color&", Assert.Single(opened));
	}

	//#639: opening another ROM directly (A → B, no EmulationStopped) changes
	//RomInfo from one game to another. The overlay and the sheet belonged to A:
	//both close, and closing the sheet does not bring A's overlay back.
	[AvaloniaFact]
	public void Opening_another_rom_directly_closes_the_overlay_and_the_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes, RomPath = "/roms/a.nes" };
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("OverlayCheatsButton"));
		Assert.True(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());

		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes, RomPath = "/roms/b.nes" };
		Dispatcher.UIThread.RunJobs();

		Assert.False(window.FindNamed<Border>("PlayerCheatsSheet").IsOnScreen());
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}

	//#639, defense in depth: CheatCodes saves to the running game's file. A
	//sheet opened for one copy refuses to save once another copy runs.
	[AvaloniaFact]
	public void A_sheet_opened_for_another_copy_never_writes_the_running_games_cheats()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		string running = CopySha1;
		model.CheatRomSha1 = () => running;
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("OverlayCheatsButton"));

		running = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
		Click(window.FindNamed<Button>("CheatsAddCodeButton"));
		window.FindNamed<TextBox>("CheatsNewCodeBox").Text = "0032:09";
		Click(window.FindNamed<Button>("CheatsAddCodeConfirm"));

		Assert.False(File.Exists(CheatFile), "the sheet wrote another copy's cheat file");
		Assert.Empty(CheatCodes.LoadCheatCodes().Cheats);
	}

	//#641: Esc on the sheet opens the overlay once. With a pack-file notice
	//waiting, that one open shows the pack-file sheet instead of the overlay;
	//the two are never on screen together (replace-not-stack).
	[AvaloniaFact]
	public void Esc_on_the_sheet_with_a_pack_file_waiting_shows_the_pack_file_sheet_alone()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes);
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("OverlayCheatsButton"));
		model.SetPendingPackDeps("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", Path.GetTempPath()) });
		Dispatcher.UIThread.RunJobs();

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Panel>("PackDepSheetBackdrop").IsOnScreen());
		Assert.False(window.IsPauseCardActive());
		Assert.False(model.IsPlayerOverlayVisible);
	}
}
