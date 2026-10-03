using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 final audit, group "no classic": nothing reached from a Player flow
//opens a classic Mesen window. Quit game and closing the window ask in place
//with the shared stop banner (W-X1); a message box, an archive's game list and
//a shader's parameters take the Player look (W-X1/W-X2, the W-P5 sheet shape);
//Settings › Look's Art row opens W-P6 in the main window; the BIOS sheet
//(W-P13) asks in every workspace. Advanced keeps every classic window. Each
//test saves a PNG of what it asserts (PlayerRender.OutputFolder).
[Collection(NativeCoreCollection.Name)]
public class PlayerNoClassicDialogTests : IDisposable
{
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Card = Colors.White;
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color BannerStop = Color.Parse("#FAF0F0");
	private static readonly Color BannerWarning = Color.Parse("#FFF8EC");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _shader = ConfigManager.Config.Video.ShaderFile;
	private readonly IApplicationLifetime? _lifetime = Application.Current?.ApplicationLifetime;
	private string? _scratch;

	public void Dispose()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Video.ShaderFile = _shader;
		InstallLifetime(_lifetime);
		if(_scratch != null && Directory.Exists(_scratch)) {
			Directory.Delete(_scratch, true);
		}
	}

	private string Scratch()
	{
		_scratch ??= Path.Combine(Path.GetTempPath(), "mesenai-nocl-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_scratch);
		return _scratch;
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(UiMode mode, bool confirm = false)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = mode;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = confirm;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//Avalonia.Headless installs no application lifetime, so the shipped
	//ApplicationHelper.GetMainWindow() answers null; the archive list and the
	//Art row reach the main window through it (CopyAsMepSheetCellTests does the same).
	private static void InstallLifetime(IApplicationLifetime? lifetime)
	{
		FieldInfo field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
			.First(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
		field.SetValue(Application.Current, lifetime);
	}

	private static void UseAsMainWindow(Window window)
	{
		ClassicDesktopStyleApplicationLifetime lifetime = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
		InstallLifetime(lifetime);
		lifetime.MainWindow = window;
	}

	private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

	private static TextBlock LabelOf(Control control) => control.FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));

	private static T OwnedDialog<T>(Window owner) where T : Window
	{
		WaitFor(() => owner.OwnedWindows.OfType<T>().Any(), $"no {typeof(T).Name} opened over {owner.GetType().Name}");
		return owner.OwnedWindows.OfType<T>().First();
	}

	private static Bitmap SaveRender(TopLevel top, string name)
	{
		Bitmap frame = PlayerRender.Capture(top);
		PlayerRender.Save(frame, name);
		return frame;
	}

	private static void AssertPlayerButton(Button button, Color background)
	{
		Assert.Equal(new CornerRadius(8), button.CornerRadius);
		TextBlock label = LabelOf(button);
		Assert.Equal("Inter", label.FontFamily.Name);
		Assert.Equal(background, PlayerRender.SolidColor(button.Background));
	}

	//W-X1: W-P4's Quit game asks on the card itself - the pale red stop banner
	//with Keep Playing and the dark Quit Game - never in a message box. Esc
	//answers Keep Playing; Keep Playing brings the button back.
	[AvaloniaFact]
	public void Quit_game_asks_in_place_on_the_overlay_card()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, confirm: true);
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();

		Click(window.FindNamed<Button>("OverlayQuitGameButton"));
		Dispatcher.UIThread.RunJobs();

		Assert.Empty(window.OwnedWindows.OfType<MessageBox>());
		Border banner = window.FindNamed<Border>("QuitGameConfirmBanner");
		Assert.True(banner.IsOnScreen(), "Quit game did not ask on the overlay card");
		Assert.Contains("banner", banner.Classes);
		Assert.Contains("stop", banner.Classes);
		Assert.Equal(BannerStop, PlayerRender.SolidColor(banner.Background));
		Assert.Equal(new CornerRadius(12), banner.CornerRadius);
		Assert.False(window.FindNamed<Button>("OverlayQuitGameButton").IsOnScreen());
		TextBlock text = window.FindNamed<TextBlock>("QuitGameConfirmText");
		Assert.Equal("Quit Contra (USA)? Progress since your last save state is lost.", text.Text);
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(Text, PlayerRender.SolidColor(text.Foreground));
		Button keep = window.FindNamed<Button>("QuitGameKeepButton");
		Button go = window.FindNamed<Button>("QuitGameGoButton");
		Assert.Equal("Keep Playing", LabelOf(keep).Text);
		Assert.Equal("Quit Game", LabelOf(go).Text);
		AssertPlayerButton(keep, Card);
		AssertPlayerButton(go, Color.Parse("#1D1D1F"));
		Assert.True(model.IsPlayerOverlayVisible);
		SaveRender(window, "W-X1-quit-game");

		//Esc answers it as Keep Playing, and the overlay stays.
		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(banner.IsOnScreen());
		Assert.True(model.IsPlayerOverlayVisible);
		Assert.True(window.FindNamed<Button>("OverlayQuitGameButton").IsOnScreen());

		Click(window.FindNamed<Button>("OverlayQuitGameButton"));
		Dispatcher.UIThread.RunJobs();
		Click(keep);
		Dispatcher.UIThread.RunJobs();
		Assert.False(banner.IsOnScreen());
		Assert.True(window.FindNamed<Button>("OverlayQuitGameButton").IsOnScreen());
	}

	//W-X1: closing the window in Player mode asks with the stop banner above
	//the profile (the shared InterruptionBar), not ConfirmExit's message box;
	//Quit closes the window.
	[AvaloniaFact]
	public void Closing_the_window_asks_in_place_in_player_mode()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, confirm: true);
		int released = 0;
		window.ReleaseCore = () => released++;

		window.Close();
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.IsVisible, "the window closed without asking");
		Assert.Empty(window.OwnedWindows.OfType<MessageBox>());
		Assert.True(model.Interruption.IsVisible);
		Border bar = window.FindNamed<Border>("InterruptionBarBorder");
		Assert.True(bar.IsOnScreen());
		Assert.Contains("stop", bar.Classes);
		Assert.Equal(BannerStop, PlayerRender.SolidColor(bar.Background));
		Assert.Equal("Quit MesenAI?", window.FindNamed<TextBlock>("InterruptionText").Text);
		Assert.Equal("Quit", LabelOf(window.FindNamed<Button>("InterruptionGoButton")).Text);
		SaveRender(window, "W-X1-quit-app");

		Click(window.FindNamed<Button>("InterruptionKeepButton"));
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.IsVisible);
		Assert.False(model.Interruption.IsVisible);
		Assert.Equal(0, released);

		window.Close();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("InterruptionGoButton"));
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(1, released);
	}

	//W-X1/W-X2: a message box over the Player window is the white sheet with
	//the shared banner - warning for a question, stop for an error - in Inter,
	//the safe button first and the primary one last.
	[AvaloniaFact]
	public void A_message_box_over_the_player_window_has_the_player_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Player);

		Task<DialogResult> question = MesenMsgBox.Show(window, "PatchAndReset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		MessageBox box = OwnedDialog<MessageBox>(window);
		Border root = box.FindNamed<Border>("PlayerMsgRoot");
		Assert.True(root.IsOnScreen(), "the message box kept the classic look over the Player window");
		Assert.False(box.FindNamed<StackPanel>("ClassicMsgRoot").IsOnScreen());
		Assert.Equal(Card, PlayerRender.SolidColor(root.Background));
		Border banner = box.FindNamed<Border>("PlayerMsgBanner");
		Assert.Contains("warning", banner.Classes);
		Assert.Equal(BannerWarning, PlayerRender.SolidColor(banner.Background));
		TextBlock text = box.FindNamed<TextBlock>("PlayerMsgText");
		Assert.Equal("Patch and reset the current game?", text.Text);
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(13.5, text.FontSize);
		Button[] buttons = box.FindNamed<StackPanel>("PlayerMsgButtons").Children.OfType<Button>().ToArray();
		Assert.Equal(new[] { "No", "Yes" }, buttons.Select(b => LabelOf(b).Text).ToArray());
		AssertPlayerButton(buttons[0], Card);
		AssertPlayerButton(buttons[1], PlayTint);
		SaveRender(box, "W-X2-message-box-question");
		Click(buttons[0]);
		WaitFor(() => question.IsCompleted, "the message box did not close");
		Assert.Equal(DialogResult.No, question.Result);

		Task<DialogResult> error = MesenMsgBox.Show(window, "UnexpectedError", MessageBoxButtons.OK, MessageBoxIcon.Error, "File not found.");
		MessageBox errorBox = OwnedDialog<MessageBox>(window);
		Border errorBanner = errorBox.FindNamed<Border>("PlayerMsgBanner");
		Assert.Contains("stop", errorBanner.Classes);
		Assert.Equal(BannerStop, PlayerRender.SolidColor(errorBanner.Background));
		SaveRender(errorBox, "W-X2-message-box-error");
		Click(errorBox.FindNamed<StackPanel>("PlayerMsgButtons").Children.OfType<Button>().Single());
		WaitFor(() => error.IsCompleted, "the error box did not close");
	}

	//Decision 3: Advanced mode, and a classic owner window in Player mode, keep
	//the classic box.
	[AvaloniaFact]
	public void A_message_box_stays_classic_in_advanced_mode_and_over_a_classic_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Advanced);
		Task<DialogResult> advanced = MesenMsgBox.Show(window, "PatchAndReset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		MessageBox box = OwnedDialog<MessageBox>(window);
		Assert.True(box.FindNamed<StackPanel>("ClassicMsgRoot").IsOnScreen());
		Assert.False(box.FindNamed<Border>("PlayerMsgRoot").IsOnScreen());
		box.Close();
		WaitFor(() => advanced.IsCompleted, "the classic box did not close");

		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		Window classic = new() { Content = new TextBlock { Text = "a debugger window" } };
		classic.Show();
		Task<DialogResult> overClassic = MesenMsgBox.Show(classic, "PatchAndReset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		MessageBox classicBox = OwnedDialog<MessageBox>(classic);
		Assert.True(classicBox.FindNamed<StackPanel>("ClassicMsgRoot").IsOnScreen());
		Assert.False(classicBox.FindNamed<Border>("PlayerMsgRoot").IsOnScreen());
		classicBox.Close();
		WaitFor(() => overClassic.IsCompleted, "the classic box did not close");
		classic.Close();
	}

	//The W-P5 sheet shape for a zip holding two games: the heading names the
	//archive, the games are rows of an inset list, Cancel then Open.
	[AvaloniaFact]
	public void An_archive_with_two_games_opens_the_player_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = Show(UiMode.Player);
		UseAsMainWindow(window);
		string zip = Path.Combine(Scratch(), "Contra Collection.zip");
		using(ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
			foreach(string name in new[] { "Contra (USA).nes", "Super C (USA).nes" }) {
				using Stream entry = archive.CreateEntry(name).Open();
				entry.Write(SyntheticNrom.Build());
			}
		}

		Task<ResourcePath?> pick = SelectRomWindow.Show(zip);
		SelectRomWindow dialog = OwnedDialog<SelectRomWindow>(window);
		Border root = dialog.FindNamed<Border>("PlayerSelectRomRoot");
		Assert.True(root.IsOnScreen(), "the archive list kept the classic look over the Player window");
		Assert.Contains("player", root.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(root.Background));
		TextBlock title = dialog.FindNamed<TextBlock>("PlayerSelectRomTitle");
		Assert.Equal("Choose a game in Contra Collection.zip", title.Text);
		Assert.Equal("Inter", title.FontFamily.Name);
		Assert.Equal(19, title.FontSize);
		Border inset = dialog.FindNamed<Border>("PlayerSelectRomInset");
		Assert.Equal(Color.Parse("#F8F8FA"), PlayerRender.SolidColor(inset.Background));
		Assert.Equal(new CornerRadius(12), inset.CornerRadius);
		ListBoxItem[] rows = dialog.FindAll<ListBoxItem>().Where(i => i.IsOnScreen()).ToArray();
		Assert.Equal(2, rows.Length);
		Assert.All(rows, r => Assert.Equal(40, r.Bounds.Height, 0.5));
		AssertPlayerButton(dialog.FindNamed<Button>("PlayerSelectRomCancel"), Card);
		AssertPlayerButton(dialog.FindNamed<Button>("PlayerSelectRomOpen"), PlayTint);
		Assert.Equal("Open", LabelOf(dialog.FindNamed<Button>("PlayerSelectRomOpen")).Text);
		SaveRender(dialog, "W-P5-select-rom");

		Click(dialog.FindNamed<Button>("PlayerSelectRomCancel"));
		WaitFor(() => pick.IsCompleted, "Cancel did not close the archive list");
		Assert.Null(pick.Result);
	}

	//ADR-0249 (W-P8, W-P10): Player's Settings is a sheet in the main window,
	//opened from W-P4's Settings row; then Look is picked (LookSettingsTabTests).
	private static LookConfigView ShowLookSettings(MainWindow window, MainWindowViewModel model)
	{
		model.RomInfo = new RomInfo() { RomPath = "/roms/Contra (USA).nes", ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("OverlaySettingsButton"));
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsPlayerSettingsVisible);
		return window.FindAll<LookConfigView>().First(v => v.IsOnScreen());
	}

	private static void Invoke(object target, string handler)
	{
		MethodInfo method = target.GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new XunitException(handler + " not found");
		method.Invoke(target, new object?[] { null, new RoutedEventArgs(Button.ClickEvent) });
		Dispatcher.UIThread.RunJobs();
	}

	//W-P10 › W-P6: in Player mode the Art row swaps the Settings sheet for
	//the pack detail sheet in the main window, never Enhancement Packs.
	[AvaloniaFact]
	public void Looks_art_row_opens_the_pack_detail_sheet_in_the_main_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		LookConfigView look = ShowLookSettings(window, model);

		Invoke(look, "OnPackDetails");

		Assert.False(model.IsPlayerSettingsVisible, "the Settings sheet stayed over the pack detail");
		Assert.True(model.IsPackDetailVisible, "the Art row did not open W-P6");
		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.Empty(window.OwnedWindows);
		SaveRender(window, "W-P6-from-look");
	}

	//W-P10's Adjust… in Player mode: the shader's parameters on the theme's
	//white sheet - its name as the heading, the rows on an inset list, Reset
	//on the left, Cancel then OK on the right.
	[AvaloniaFact]
	public void Looks_adjust_opens_the_shader_parameters_in_the_player_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		string folder = Scratch();
		File.WriteAllText(Path.Combine(folder, "nocl-test.slang"),
			"#version 450\n" +
			"#pragma parameter NOCL_BRIGHT \"Brightness\" 1.0 0.0 2.0 0.05\n" +
			"#pragma parameter NOCL_GLOW \"Glow\" 0.0 0.0 1.0 1.0\n" +
			"#pragma stage vertex\nvoid main() {}\n#pragma stage fragment\nvoid main() {}\n");
		string preset = Path.Combine(folder, "nocl-test.slangp");
		File.WriteAllText(preset, "shaders = 1\nshader0 = nocl-test.slang\n");
		ConfigManager.Config.Video.ShaderFile = preset;
		LookConfigView look = ShowLookSettings(window, model);

		Invoke(look, "OnAdjust");
		ShaderConfigWindow adjust = OwnedDialog<ShaderConfigWindow>(window);
		Border root = adjust.FindNamed<Border>("ShaderConfigRoot");
		Assert.Contains("player", root.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(root.Background));
		TextBlock title = adjust.FindNamed<TextBlock>("ShaderConfigPlayerTitle");
		Assert.True(title.IsOnScreen());
		Assert.Equal("nocl-test.slangp", title.Text);
		Assert.Equal("Inter", title.FontFamily.Name);
		Border list = adjust.FindNamed<Border>("ShaderConfigList");
		Assert.Equal(Color.Parse("#F8F8FA"), PlayerRender.SolidColor(list.Background));
		Assert.Equal(new CornerRadius(12), list.CornerRadius);
		Button[] actions = adjust.FindNamed<StackPanel>("ShaderConfigActions").Children.OfType<Button>().ToArray();
		Assert.Equal(new[] { "ShaderConfigCancel", "ShaderConfigOk" }, actions.Select(b => b.Name).ToArray());
		AssertPlayerButton(actions[0], Card);
		AssertPlayerButton(actions[1], PlayTint);
		AssertPlayerButton(adjust.FindNamed<Button>("ShaderConfigReset"), Card);
		adjust.Close();
		model.ClosePlayerSettings();

		//The rows themselves, from parameters given directly (librashader does
		//not parse a synthetic preset in every environment): a slider, a
		//switch and a label line, on the inset list in Inter.
		ShaderConfigWindow rows = new(playerLook: true) {
			DataContext = new ShaderConfigViewModel(false, "") {
				Config = new ShaderConfig {
					ShaderFile = preset,
					Params = new() {
						new ShaderParam { Name = "NOCL_BRIGHT", Description = "Brightness", Min = 0, Max = 2, Step = 0.05m, Initial = 1, Value = 1 },
						new ShaderParam { Name = "NOCL_GLOW", Description = "Glow", Min = 0, Max = 1, Step = 1, Initial = 0, Value = 1 },
						new ShaderParam { Name = "NOCL_MASK", Description = "Mask strength", Min = 0, Max = 1, Step = 0.1m, Initial = 0.3m, Value = 0.3m },
					},
				},
			},
		};
		rows.Show();
		Dispatcher.UIThread.RunJobs();
		TextBlock[] names = rows.FindAll<TextBlock>().Where(t => t.Classes.Contains("shaderParam") && t.IsOnScreen()).ToArray();
		Assert.Equal(new[] { "Brightness", "Glow", "Mask strength" }, names.Select(t => t.Text).ToArray());
		Assert.All(names, t => Assert.Equal("Inter", t.FontFamily.Name));
		Assert.Single(rows.FindAll<CheckBox>(), c => c.IsOnScreen() && c.Classes.Contains("switch"));
		SaveRender(rows, "W-P10-adjust");
		rows.Close();
	}

	//The classic entry point (the Video menu's shader parameters,
	//ShaderMenuHelper) keeps the classic window: no Player class, no heading,
	//OK before Cancel.
	[AvaloniaFact]
	public void The_classic_shader_window_stays_classic()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShaderConfigWindow classic = new() {
			DataContext = new ShaderConfigViewModel(false, "") { Config = new ShaderConfig { ShaderFile = "nocl-classic.slangp" } },
		};
		classic.Show();
		Dispatcher.UIThread.RunJobs();
		Assert.DoesNotContain("player", classic.FindNamed<Border>("ShaderConfigRoot").Classes);
		Assert.False(classic.FindNamed<TextBlock>("ShaderConfigPlayerTitle").IsOnScreen());
		Button[] actions = classic.FindNamed<StackPanel>("ShaderConfigActions").Children.OfType<Button>().ToArray();
		Assert.Equal(new[] { "ShaderConfigOk", "ShaderConfigCancel" }, actions.Select(b => b.Name).ToArray());
		classic.Close();
	}
}
