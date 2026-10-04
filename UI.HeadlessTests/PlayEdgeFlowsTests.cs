using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P12–W-P16): the Play edge flows'
//XAML wiring. The rules (first-run defaults, BIOS kinds and sizes, the load
//failure causes, the pack file check, controller detection and steps) are
//pinned host-free in UI.Tests/Play; this checks they reach the realized tree:
//control counts (rule 2), focus on open (rule 9), Esc (rule 8), and that a
//failure leaves the home on screen with its sentence (rule 5, W-X2).
//
//The W-P13–W-P16 tests need a MainWindow (EmuApi.InitDll in its constructor),
//so they self-skip on the core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public partial class PlayEdgeFlowsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-g5-" + Guid.NewGuid().ToString("N"));

	public PlayEdgeFlowsTests()
	{
		Directory.CreateDirectory(_folder);

		//#790: the core is process-global and a case that ran a game leaves its
		//console loaded, so EmuApi.IsRunning() was still true when the next case
		//started. That is not cosmetic. LoadRomHelper.ReportLoadFailure suppresses the
		//home's alert while a game is running (a failure with a game on screen stays
		//today's message, #674), so the alert cases below passed or failed on the order
		//the runner happened to pick - and the order is only stable per build.
		//
		//Cleared here rather than in Dispose because a leak can also come from another
		//class in the serial collection, and this is the side that has to be true.
		if(NativeCore.IsAvailable && EmuApi.IsRunning()) {
			EmuApi.Stop();
			WaitUntilStopped();
		}
	}

	private static void WaitUntilStopped()
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(EmuApi.IsRunning() && clock.ElapsedMilliseconds < 5000) {
			Thread.Sleep(10);
		}
		Assert.False(EmuApi.IsRunning(), "EmuApi.Stop() left the previous case's game loaded (#790)");
	}

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	//W-P12's Confirm writes a settings file into the user's folder; the tests
	//record the call instead.
	private sealed class RecordingFirstRun : SetupWizardViewModel
	{
		public int Confirms;
		public bool Succeeds = true;

		public override bool Confirm()
		{
			Confirms++;
			ErrorText = Succeeds ? "" : "MesenAI cannot write to /nowhere.";
			return Succeeds;
		}
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			RunJobsAndDueTimers();
			Thread.Sleep(20);
		}
		RunJobsAndDueTimers();
	}

	//#707: Avalonia's dispatcher promotes a due DispatcherTimer from the OS
	//timer callback, which only the main loop runs - never a test body - or
	//after it executes some other job. A paused game posts nothing, so the
	//controller poll (PlayEdgeFlowsWiring) stopped ticking in RunJobs alone.
	//The empty job stands in for the main loop's timer wake-up.
	private static void RunJobsAndDueTimers()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(Control root, string button)
	{
		root.FindNamed<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	private static int ControlsOnScreen(Control root)
	{
		return root.FindAll<Control>().Count(c => c.IsOnScreen() && c is Button or RadioButton or ComboBox or CheckBox && c.Focusable);
	}

	private static void PrepareShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PrepareShowPlay();
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");
		return (window, model);
	}

	private void RunSyntheticGame(MainWindowViewModel model)
	{
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
	}

	//W-P12: 2 radios, the keyboard popup and Start Playing (plus the two
	//desktop checkboxes off macOS); Start Playing has focus; no Cancel.
	[AvaloniaFact]
	public void First_run_sheet_shows_the_wireframe_controls_and_focuses_Start_Playing()
	{
		RecordingFirstRun model = new();
		SetupWizardWindow window = new(model);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			Assert.Equal(PlayFirstRun.ControlCount(OperatingSystem.IsMacOS()), ControlsOnScreen(window));
			Assert.True(window.FindNamed<RadioButton>("FirstRunUserFolder").IsChecked);
			Assert.Equal((int)FirstRunKeyboard.ArrowKeys, window.FindNamed<ComboBox>("FirstRunKeyboard").SelectedIndex);
			Assert.True(window.FindNamed<Button>("FirstRunStartPlaying").IsFocused);
			Assert.DoesNotContain(window.FindAll<Button>(), b => b.IsOnScreen() && (b.Content as string)?.Contains("Cancel") == true);
			Assert.False(window.FindNamed<TextBlock>("FirstRunError").IsOnScreen());

			//The popup is the one keyboard choice; picking WASD reaches the model.
			window.FindNamed<ComboBox>("FirstRunKeyboard").SelectedIndex = (int)FirstRunKeyboard.Wasd;
			Assert.Equal(FirstRunKeyboard.Wasd, model.Choice.Keyboard);
		} finally {
			model.Succeeds = true;
			window.Close();
		}
	}

	//#672: with no fork update feed, Check for updates is unchecked and
	//disabled, and says why (rule: UI.Tests UpdateChannelTests).
	[AvaloniaFact]
	public void First_run_sheet_disables_check_for_updates_while_there_is_no_feed()
	{
		RecordingFirstRun model = new();
		SetupWizardWindow window = new(model);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			CheckBox check = window.FindNamed<CheckBox>("FirstRunCheckForUpdates");
			Assert.Equal(UpdateChannel.HasFeed, check.IsEnabled);
			Assert.Equal(UpdateChannel.HasFeed, check.IsChecked);
			Assert.True(ToolTip.GetShowOnDisabled(check));
			Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(check) as string));
		} finally {
			model.Succeeds = true;
			window.Close();
		}
	}

	//Esc keeps the defaults and continues: the close applies the choice once.
	[AvaloniaFact]
	public void Esc_on_the_first_run_sheet_applies_the_choice_and_continues()
	{
		RecordingFirstRun model = new();
		SetupWizardWindow window = new(model);
		window.Show();
		Dispatcher.UIThread.RunJobs();

		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(1, model.Confirms);
		Assert.False(window.IsVisible);
		Assert.Equal(PlayFirstRun.Defaults, model.Choice);
	}

	//W-X2: a folder that cannot be written keeps the sheet with its sentence.
	[AvaloniaFact]
	public void A_storage_folder_that_cannot_be_written_keeps_the_sheet_open_with_a_sentence()
	{
		RecordingFirstRun model = new() { Succeeds = false };
		SetupWizardWindow window = new(model);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		try {
			Click(window, "FirstRunStartPlaying");
			Assert.True(window.IsVisible);
			TextBlock error = window.FindNamed<TextBlock>("FirstRunError");
			Assert.True(error.IsOnScreen());
			Assert.Contains("cannot write", error.Text);
		} finally {
			model.Succeeds = true;
			window.Close();
		}
	}

	//Confirm's two halves, without touching the real settings or Desktop.
	private sealed class ShortcutFailsFirstRun : SetupWizardViewModel
	{
		public bool FolderWritable = true;

		protected override void WriteSettings(string targetFolder, FirstRunChoice choice)
		{
			if(!FolderWritable) {
				throw new UnauthorizedAccessException(targetFolder);
			}
		}

		protected override void CreateShortcutFile() => throw new DirectoryNotFoundException("no Desktop");
	}

	//The settings are written: a Desktop shortcut that fails is not an
	//unwritable folder and must not keep the sheet open.
	[AvaloniaFact]
	public void A_failed_desktop_shortcut_does_not_block_the_first_run_sheet()
	{
		ShortcutFailsFirstRun model = new() { ShowsDesktopOptions = true, CreateShortcut = true };
		Assert.True(model.Confirm());
		Assert.Equal("", model.ErrorText);

		model.FolderWritable = false;
		Assert.False(model.Confirm());
		Assert.Contains("cannot write", model.ErrorText);
	}

	//W-P13: the sheet names the file and its size, a wrong size is an inline
	//line with the drop zone kept, and Cancel leaves the status sentence.
	[AvaloniaFact]
	public void Bios_sheet_has_three_controls_rejects_a_wrong_size_inline_and_cancel_leaves_a_status_sentence()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();

		Task<bool> request = model.RequestBios(FirmwareType.FDS, "disksys.rom", 8192, 0, "Zelda no Densetsu");
		Dispatcher.UIThread.RunJobs();

		Panel host = window.FindNamed<Panel>("BiosSheetBackdrop");
		Assert.True(host.IsOnScreen());
		Assert.Equal(PlayBiosPrompt.ControlCount, ControlsOnScreen(host));
		Assert.Equal("Drop disksys.rom here", window.FindNamed<TextBlock>("BiosSheetDropTitle").Text);
		Assert.Contains("Famicom Disk System", window.FindNamed<TextBlock>("BiosSheetBody").Text);
		Assert.True(window.FindNamed<Button>("BiosSheetChooseFile").IsFocused);

		string wrong = Path.Combine(_folder, "wrong.rom");
		File.WriteAllBytes(wrong, new byte[16 * 1024]);
		model.BiosSheet.TryFile(wrong);
		Dispatcher.UIThread.RunJobs();
		Border error = window.FindNamed<Border>("BiosSheetError");
		Assert.True(error.IsOnScreen());
		Assert.Contains("16 KB", model.BiosSheet.ErrorText);
		Assert.Contains("the FDS BIOS is 8 KB", model.BiosSheet.ErrorText);
		Assert.True(window.FindNamed<Button>("BiosSheetDropZone").IsOnScreen());
		Assert.False(request.IsCompleted);

		//A file of the right size that cannot be read is a sentence too, not a
		//throw or a message box; the Core's request stays open.
		if(!OperatingSystem.IsWindows()) {
			string unreadable = Path.Combine(_folder, "unreadable.rom");
			File.WriteAllBytes(unreadable, new byte[8192]);
			File.SetUnixFileMode(unreadable, UnixFileMode.None);
			try {
				model.BiosSheet.TryFile(unreadable);
			} finally {
				File.SetUnixFileMode(unreadable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			Dispatcher.UIThread.RunJobs();
			Assert.Contains("could not read", model.BiosSheet.ErrorText);
			Assert.False(model.BiosSheet.IsConfirmingUnknown);
			Assert.False(request.IsCompleted);
		}

		//Esc on the sheet is Cancel; the game does not load.
		model.TogglePlayerOverlay();
		WaitFor(() => request.IsCompleted, "Cancel never answered the Core's request");
		Assert.False(request.Result);
		Assert.False(host.IsOnScreen());
		Assert.False(model.IsPlayerOverlayVisible);
		Assert.Contains("Zelda no Densetsu needs the FDS BIOS", model.Shell.StatusText);

		//The next open clears the sentence.
		model.OnOpenStarted();
		Assert.DoesNotContain("needs the FDS BIOS", model.Shell.StatusText);
	}

	//W-P14: a file that is not a game leaves the home on screen with one
	//inline alert (3 controls with W-P2's 2); the next open clears it.
	[AvaloniaFact]
	public void A_file_that_is_not_a_game_shows_an_inline_alert_on_the_home()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();

		string notAGame = Path.Combine(_folder, "Contra.txt");
		File.WriteAllText(notAGame, "not a game");
		LoadRomHelper.LoadFile(notAGame);
		WaitFor(() => model.RecentGames.IsLoadAlertVisible, "the load failure never reached the home");

		Assert.True(model.RecentGames.Visible);
		Border alert = window.FindNamed<Border>("PlayHomeLoadAlert");
		Assert.True(alert.IsOnScreen());
		Assert.Equal("\u201cContra.txt\u201d is not a game MesenAI can open.", window.FindNamed<TextBlock>("PlayHomeLoadAlertTitle").Text);
		Assert.Contains("or a zip holding one", window.FindNamed<TextBlock>("PlayHomeLoadAlertBody").Text);
		Assert.True(window.FindNamed<Button>("PlayHomeOpenAnother").IsOnScreen());

		//W-P14 has no close box: any open (Open Another…, a tile, a drop) clears it.
		model.OnOpenStarted();
		Dispatcher.UIThread.RunJobs();
		Assert.False(alert.IsOnScreen());
		Assert.True(model.RecentGames.Visible);
	}

	//W-P16: the first overlay after the notice opens the sheet, once; a wrong
	//file is a sentence; Esc returns to the overlay, the next Esc resumes.
	[AvaloniaFact]
	public void Pack_file_sheet_opens_with_the_overlay_once_and_esc_returns_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			RunSyntheticGame(model);
			string drop = Path.Combine(_folder, "drop");
			string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
			model.SetPendingPackDeps("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", drop, expected) });
			Dispatcher.UIThread.RunJobs();
			Assert.Contains("a pack waits for one file", model.Shell.StatusText);

			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "Esc did not pause the game");
			Panel sheet = window.FindNamed<Panel>("PackDepSheetBackdrop");
			Assert.True(sheet.IsOnScreen());
			Assert.False(model.IsPlayerOverlayVisible);
			Assert.Equal(PlayPackDepPrompt.ControlCount, ControlsOnScreen(sheet));
			Assert.Equal("Contra Remastered needs one file", window.FindNamed<TextBlock>("PackDepSheetTitle").Text);
			Assert.Equal("License: not declared", window.FindNamed<TextBlock>("PackDepSheetLicense").Text);
			Assert.Equal("Add and Restart…", window.FindNamed<Button>("PackDepSheetChooseFile").Content);
			Assert.True(window.FindNamed<Button>("PackDepSheetChooseFile").IsFocused);

			string wrong = Path.Combine(_folder, "wrong.nes");
			File.WriteAllBytes(wrong, new byte[] { 9 });
			WaitTask(model.PackDepSheet.TryFile(wrong));
			Assert.True(window.FindNamed<Border>("PackDepSheetError").IsOnScreen());
			Assert.False(File.Exists(Path.Combine(drop, "wrong.nes")));

			//The right file, but the shared drop folder already holds another
			//file under that name: refused inline, the other file kept.
			string right = Path.Combine(_folder, "right.nes");
			File.WriteAllBytes(right, new byte[] { 1, 2, 3 });
			Directory.CreateDirectory(drop);
			File.WriteAllBytes(Path.Combine(drop, "right.nes"), new byte[] { 7 });
			WaitTask(model.PackDepSheet.TryFile(right));
			Assert.Contains("already holds a different file named \"right.nes\"", model.PackDepSheet.ErrorText);
			Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(drop, "right.nes")));
			Assert.True(model.PackDepSheet.Notice.HasPending);

			//Esc: back to the overlay, still paused.
			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Assert.False(sheet.IsOnScreen());
			Assert.True(model.IsPlayerOverlayVisible);
			Assert.True(EmuApi.IsPaused());

			//Resume, then Esc again: the overlay only - once per notice.
			model.TogglePlayerOverlay();
			WaitFor(() => !EmuApi.IsPaused(), "Esc on the overlay did not resume");
			model.TogglePlayerOverlay();
			WaitFor(() => model.IsGamePaused, "Esc did not pause the game");
			Assert.True(model.IsPlayerOverlayVisible);
			Assert.False(sheet.IsOnScreen());

			//The game is gone: its pack's pending file goes with it.
			EmuApi.Stop();
			WaitFor(() => model.RomInfo.Format == RomFormat.Unknown, "the game never stopped");
			Assert.DoesNotContain("a pack waits", model.Shell.StatusText);
			Assert.False(model.PackDepSheet.Notice.HasPending);
		} finally {
			EmuApi.Stop();
			Dispatcher.UIThread.RunJobs();
		}
	}

	//A file dropped on the sheet is hashed and copied off the UI thread; Play
	//Without It (or Esc) during that work abandons the add, so the game the
	//player went back to is not reloaded under them when the copy ends. A
	//sheet of its own: the window's FileAdded reloads the real core.
	[AvaloniaFact]
	public void Closing_the_pack_file_sheet_while_the_file_is_checked_does_not_reload_the_game()
	{
		string drop = Path.Combine(_folder, "drop");
		string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
		PlayPackDepSheetViewModel sheet = new();
		sheet.SetPending("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", drop, expected) });
		sheet.Open();
		Assert.True(sheet.IsVisible);
		int added = 0;
		sheet.FileAdded += () => added++;

		string right = Path.Combine(_folder, "right.nes");
		File.WriteAllBytes(right, new byte[] { 1, 2, 3 });
		Task adding = sheet.TryFile(right);
		sheet.PlayWithoutIt();
		WaitTask(adding);

		Assert.Equal(0, added);
		Assert.False(sheet.IsVisible);
		Assert.False(sheet.IsBusy);
	}

	//The user's rule (2026-10-03): while a dropped file is hashed and copied,
	//the sheet says so with a moving bar (the controls used to just grey out).
	[AvaloniaFact]
	public void Adding_a_pack_file_shows_a_moving_wait_until_it_is_checked()
	{
		string drop = Path.Combine(_folder, "drop");
		string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
		PlayPackDepSheetViewModel sheet = new();
		sheet.SetPending("Contra Remastered", new[] { new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", drop, expected) });
		sheet.Open();
		Window window = new() { Content = new PlayPackDepSheetView { DataContext = sheet }, Width = 1000, Height = 700 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		Control wait = window.FindNamed<Control>("PackDepSheetBusy");
		Assert.False(wait.IsOnScreen());

		string wrong = Path.Combine(_folder, "wrong.nes");
		File.WriteAllBytes(wrong, new byte[] { 9 });
		Task adding = sheet.TryFile(wrong);
		Assert.True(sheet.IsBusy);
		Assert.True(wait.IsOnScreen());
		Assert.True(wait.FindAll<ProgressBar>().Single().IsIndeterminate);
		Assert.Equal("Checking the file…", window.FindNamed<TextBlock>("PackDepSheetBusyText").Text);

		WaitTask(adding);
		Assert.False(wait.IsOnScreen());
		window.Close();
	}

	//An install's post that lands after another open started is dropped, so
	//the next game never inherits the previous game's pending file.
	[AvaloniaFact]
	public void A_pending_file_post_from_before_the_last_open_is_dropped()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow _, MainWindowViewModel model) = ShowPlay();
		CommunityPackDepPrompt[] deps = { new("contra-usa", "Contra (USA).nes", "", Path.Combine(_folder, "drop"), "AB") };

		int installGeneration = model.OpenGeneration;
		model.OnOpenStarted();
		Assert.False(model.SetPendingPackDeps("Contra Remastered", deps, installGeneration, "C0FFEE", "C0FFEE"));
		Assert.False(model.PackDepSheet.Notice.HasPending);
		Assert.DoesNotContain("a pack waits", model.Shell.StatusText);

		Assert.True(model.SetPendingPackDeps("Contra Remastered", deps, model.OpenGeneration, "C0FFEE", "c0ffee"));
		Assert.True(model.PackDepSheet.Notice.HasPending);
		model.OnOpenStarted();
		Assert.False(model.PackDepSheet.Notice.HasPending);
	}

	private static void WaitTask(Task task)
	{
		WaitFor(() => task.IsCompleted, "the file check never finished");
		task.GetAwaiter().GetResult();
	}

	//W-P15: the pill on an unknown pad's first press, Start opens the sheet,
	//each step is a press and release on that pad, and the result lands in
	//port 1's first free mapping slot without touching the bound ones.
	[AvaloniaFact]
	public void Unknown_controller_pill_opens_a_pad_driven_sheet_that_fills_the_free_slot()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		NesControllerConfig port = ConfigManager.Config.Nes.Port1;
		//Slots 1-3 bound, slot 4 free. Slots 2 and 3 bind only a turbo button and
		//the generic key: still taken, never reported free.
		NesKeyMapping[] saved = { port.Mapping1, port.Mapping2, port.Mapping3, port.Mapping4 };
		port.Mapping1 = new NesKeyMapping() { A = 0x0101 };
		port.Mapping2 = new NesKeyMapping() { TurboA = 0x0102 };
		port.Mapping3 = new NesKeyMapping() { GenericKey1 = 0x0103 };
		port.Mapping4 = new NesKeyMapping();

		PlayControllerSetupViewModel setup = model.ControllerSetup;
		int paused = 0, resumed = 0;
		const int device = 7;
		ushort Key(int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
		setup.KeyName = k => k == Key(9) ? "Pad8 Start" : "Pad8 But" + (k & 0xFF);
		setup.CurrentConsole = () => ConsoleType.Nes;
		setup.IsPaused = () => false;
		setup.Pause = () => paused++;
		setup.Resume = () => resumed++;
		string? result = null;
		setup.Finished += r => result = r;
		try {
			TimeSpan t = setup.Now;
			ushort[] none = Array.Empty<ushort>();

			setup.Tick(new[] { Key(1) }, t);
			Assert.True(setup.IsPillVisible);
			Dispatcher.UIThread.RunJobs();
			Assert.True(window.FindNamed<Border>("ControllerSetupPill").IsOnScreen());

			setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			setup.Tick(new[] { Key(9) }, t += TimeSpan.FromMilliseconds(100));
			Assert.True(setup.IsVisible);
			Assert.False(setup.IsPillVisible);
			Assert.Equal(1, paused);
			Assert.Equal("Set up \u201cPad8\u201d", setup.Title);
			Assert.Equal("Press the button you want as A", setup.Prompt);
			Assert.StartsWith("Step 1 of 8", setup.StepText);
			Dispatcher.UIThread.RunJobs();
			Panel sheet = window.FindNamed<Panel>("ControllerSetupBackdrop");
			Assert.True(sheet.IsOnScreen());
			Assert.Equal(2, ControlsOnScreen(sheet));
			//The keys are drawn dark or tinted in either theme (ADR-0249 W-P15):
			//the chip labels never inherit the theme's foreground - always white.
			TextBlock[] chips = sheet.FindAll<TextBlock>().Where(t => t.Name == "ControllerSetupChipLabel").ToArray();
			Assert.Equal(8, chips.Length);
			Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(chips[0].Foreground).Color);
			Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(chips[1].Foreground).Color);

			//Release Start (arms), then one press/release per step.
			setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			for(int step = 0; step < 8; step++) {
				setup.Tick(new[] { Key(20 + step) }, t += TimeSpan.FromMilliseconds(100));
				setup.Tick(none, t += TimeSpan.FromMilliseconds(100));
			}

			Assert.False(setup.IsVisible);
			Assert.Equal(1, resumed);
			Assert.Equal("Pad8 is set up.", result);
			Assert.Equal(Key(20), port.Mapping4.A);
			Assert.Equal(Key(27), port.Mapping4.Right);
			Assert.Equal(0x0101, port.Mapping1.A);
			Assert.Equal(0x0102, port.Mapping2.TurboA);
			Assert.Equal(0, port.Mapping2.A);
			Assert.Equal(0x0103, port.Mapping3.GenericKey1);
			Assert.Equal(0, port.Mapping3.A);
		} finally {
			port.Mapping1 = saved[0];
			port.Mapping2 = saved[1];
			port.Mapping3 = saved[2];
			port.Mapping4 = saved[3];
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
		}
	}
}
