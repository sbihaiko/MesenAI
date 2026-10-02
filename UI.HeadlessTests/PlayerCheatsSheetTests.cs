using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
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
		window.Show();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = console };
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
		Assert.False(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
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
		Assert.Contains(CheatRecordingRule.AllowedNote, VisibleTexts(list));
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
}
