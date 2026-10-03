using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.13 (ADR-0246): Settings › Look. The rules (which choice is offered, enabled,
//marked, selected, and what picking it writes) are pinned host-free in
//UI.Tests/Look; this checks the crossing into XAML: the Look tab sits after
//Display in the overlay's Settings strip (W-P8, G.4), it shows Art/Pixels/Screen with a mark on the
//selected choice, a value set in Options shows as the current item and
//survives, a pick writes VideoConfig and Cancel restores it, Hold to Compare
//follows the pointer, and the two moved settings are gone from their old homes.
//
//The Look view-model reads the loaded game from MainWindowViewModel.Instance and
//asks the core whether a pack draws the art, so these need the built core.
//Pack art itself (Pixels disabled) cannot be produced headless without a ROM
//and a pack; scripts/check_look_pack_art.py proves the core signal on NES, GB
//and SMS, and UI.Tests the disabled Pixels row it drives.
[Collection(NativeCoreCollection.Name)]
public class LookSettingsTabTests : IDisposable
{
	private readonly VideoFilterType _filter;
	private readonly string _shader;
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public LookSettingsTabTests()
	{
		_filter = ConfigManager.Config.Video.VideoFilter;
		_shader = ConfigManager.Config.Video.ShaderFile;
	}

	public void Dispose()
	{
		ConfigManager.Config.Video.VideoFilter = _filter;
		ConfigManager.Config.Video.ShaderFile = _shader;
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
	}

	//#724: the Play sheets only show in the Play workspace, and the workspace is
	//persisted (a Remaster/Share test's switch saves it to the test home's
	//settings.json), so a filtered run could start in Remaster. Set it here
	//rather than rely on what an earlier test or run left behind.
	private MainWindow? _main;

	private (MainWindow Window, MainWindowViewModel Model) ShowPlayer(ConsoleType console, RomFormat format)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		//The renders' window: the Settings sheet (480 x 480) fits under the bar.
		MainWindow main = new() { Width = 1100, Height = 740 };
		main.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(main.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = console, Format = format };
		_main = main;
		return (main, model);
	}

	//ADR-0249 (W-P8, W-P10): Player's Settings is a sheet in the main window,
	//opened from W-P4's Settings row, then the tab is picked.
	private (MainWindow Window, ConfigViewModel Model) ShowSettings(ConfigWindowTab tab)
	{
		MainWindow main = Assert.IsType<MainWindow>(_main);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(main.DataContext);
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		main.FindNamed<Button>("OverlaySettingsButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		main.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(tab);
		Dispatcher.UIThread.RunJobs();
		return (main, Assert.IsType<ConfigViewModel>(model.PlayerSettings));
	}

	//A MainWindow is never closed in a test (closing it shuts the core down for
	//the rest of the run): the sheet is.
	private void CloseSettings() => (_main?.DataContext as MainWindowViewModel)?.ClosePlayerSettings();

	private static ConfigWindow ShowOptions(ConfigWindowTab tab)
	{
		ConfigWindow window = new(tab);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	private static string[] VisibleTexts(Visual root) => root.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();

	private static List<ComboBoxItem> OpenItems(ComboBox combo)
	{
		combo.IsDropDownOpen = true;
		Dispatcher.UIThread.RunJobs();
		List<ComboBoxItem> items = Enumerable.Range(0, combo.ItemCount).Select(i => Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(i))).ToList();
		combo.IsDropDownOpen = false;
		Dispatcher.UIThread.RunJobs();
		return items;
	}

	[AvaloniaFact]
	public void Look_is_the_tab_after_display_and_shows_art_pixels_and_screen_with_their_marks()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, _) = ShowSettings(ConfigWindowTab.Look);
		//G.4 (W-P8): Player mode's own strip, Display | Look | Audio | Controls.
		List<TabItem> tabs = window.FindNamed<TabControl>("PlayerSettingsTabs").Items.Cast<TabItem>().ToList();
		TabItem look = tabs[PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look)];
		Assert.Equal("tabPlayerLook", look.Name);
		Assert.True(look.IsSelected);
		Assert.True(look.IsOnScreen());
		Assert.Equal(PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Display) + 1, tabs.IndexOf(look));

		string[] texts = VisibleTexts(window);
		Assert.Contains("ART", texts);
		Assert.Contains("PIXELS", texts);
		Assert.Contains("SCREEN", texts);
		Assert.Contains("The game's own art (no pack)", texts);
		Assert.Equal("◉ Shows in screenshots and videos", window.FindNamed<TextBlock>("txtLookPixelsMark").Text);
		Assert.Equal("Adds nothing to the picture", window.FindNamed<TextBlock>("txtLookScreenMark").Text);
		Assert.DoesNotContain(texts, t => t.Contains("[["));
		Assert.False(window.FindNamed<Button>("btnLookPackDetails").IsOnScreen());
		Assert.True(window.FindNamed<ComboBox>("cboLookPixels").IsEnabled);
		CloseSettings();
	}

	[AvaloniaFact]
	public void A_pick_writes_the_video_config_and_cancel_restores_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, ConfigViewModel model) = ShowSettings(ConfigWindowTab.Look);
		ComboBox pixels = window.FindNamed<ComboBox>("cboLookPixels");
		pixels.SelectedItem = pixels.Items.Cast<LookChoice>().First(c => c.Pixels?.Kind == PixelsItemKind.SmoothHq4x);
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(VideoFilterType.HQ4x, ConfigManager.Config.Video.VideoFilter);
		Assert.True(model.IsDirty());
		model.RevertConfig();
		Assert.Equal(VideoFilterType.None, ConfigManager.Config.Video.VideoFilter);
		CloseSettings();
	}

	[AvaloniaFact]
	public void A_filter_set_in_options_shows_as_the_current_item_and_is_kept()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.Scale2x;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, ConfigViewModel model) = ShowSettings(ConfigWindowTab.Look);
		ComboBox pixels = window.FindNamed<ComboBox>("cboLookPixels");
		LookChoice selected = Assert.IsType<LookChoice>(pixels.SelectedItem);
		Assert.Equal(PixelsItemKind.Current, selected.Pixels?.Kind);
		Assert.EndsWith("(set in Options)", selected.Label);
		Assert.False(model.IsDirty());
		Assert.Equal(VideoFilterType.Scale2x, ConfigManager.Config.Video.VideoFilter);
		CloseSettings();
	}

	[AvaloniaFact]
	public void Ntsc_reads_disabled_with_its_reason_on_a_game_boy_game()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Gameboy, RomFormat.Gb);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, _) = ShowSettings(ConfigWindowTab.Look);
		ComboBox screen = window.FindNamed<ComboBox>("cboLookScreen");
		int ntsc = screen.Items.Cast<LookChoice>().ToList().FindIndex(c => c.Screen?.Kind == ScreenItemKind.Ntsc);
		ComboBoxItem item = OpenItems(screen)[ntsc];
		Assert.False(item.IsEnabled);
		Assert.Equal("TV signal (NTSC) — NES games only", ((LookChoice)item.Content!).Label);
		CloseSettings();
	}

	[AvaloniaFact]
	public void Hold_to_compare_follows_the_pointer()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.HQ4x;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, ConfigViewModel model) = ShowSettings(ConfigWindowTab.Look);
		Button compare = window.FindNamed<Button>("btnLookHoldToCompare");
		Assert.True(compare.IsEnabled);
		Point center = compare.TranslatePoint(new Point(compare.Bounds.Width / 2, compare.Bounds.Height / 2), window) ?? throw new InvalidOperationException("button is not in the window");

		window.MouseDown(center, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.Look?.IsComparing);

		window.MouseUp(center, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.Look?.IsComparing);
		CloseSettings();
	}

	//#663: a Remaster recording (or the HD Pack Builder) stops the pack's art
	//and its Stop brings it back, with the same game and pack name; the Pixels
	//lock follows the switch while the tab stays open.
	[AvaloniaFact]
	public void Pixels_follow_a_runtime_pack_art_switch_while_the_tab_is_open()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		(MainWindow window, ConfigViewModel model) = ShowSettings(ConfigWindowTab.Look);
		LookConfigViewModel look = Assert.IsType<LookConfigViewModel>(model.Look);
		bool drawing = true;
		look.DrawingPackArt = () => drawing;
		look.Refresh();
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<ComboBox>("cboLookPixels").IsEnabled);

		drawing = false;
		PackArtSwitch.Raise();
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<ComboBox>("cboLookPixels").IsEnabled);

		drawing = true;
		PackArtSwitch.Raise();
		Dispatcher.UIThread.RunJobs();
		Assert.False(window.FindNamed<ComboBox>("cboLookPixels").IsEnabled);
		CloseSettings();
	}

	[AvaloniaFact]
	public void Hold_to_compare_is_off_with_its_reason_when_nothing_would_change()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigManager.Config.Video.VideoFilter = VideoFilterType.None;
		ConfigManager.Config.Video.ShaderFile = "";

		(MainWindow window, _) = ShowSettings(ConfigWindowTab.Look);
		Assert.False(window.FindNamed<Button>("btnLookHoldToCompare").IsEnabled);
		Assert.Equal("Nothing to compare: Pixels and Screen are off", window.FindNamed<TextBlock>("txtLookCompareReason").Text);
		CloseSettings();
	}

	[AvaloniaFact]
	public void The_shader_selector_has_left_video_settings()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		//Video is Options-only since W-P8 (Display + Look replace it in Play).
		ConfigWindow window = ShowOptions(ConfigWindowTab.Video);
		Mesen.Views.VideoConfigView video = Assert.Single(window.FindAll<Mesen.Views.VideoConfigView>());
		//The selector lived on the Picture page; only the selected page is realized.
		TabControl pages = video.FindAll<TabControl>().First();
		pages.SelectedIndex = 1;
		Dispatcher.UIThread.RunJobs();
		Assert.Equal("Picture", ((TabItem)pages.SelectedItem!).Header);
		Assert.NotEmpty(video.FindAll<MesenSlider>());
		Assert.Empty(window.FindAll<ShaderSelector>());
		window.Close();
	}

	[AvaloniaFact]
	public void Hi_res_filter_has_left_the_enhancements_panel()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow main, MainWindowViewModel model) = ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		model.OpenEnhancementsPanel();
		Dispatcher.UIThread.RunJobs();

		Control panel = main.FindNamed<Control>("PlayerEnhancementsPanel");
		Assert.True(panel.IsOnScreen());
		string[] boxes = panel.FindAll<CheckBox>().Select(c => c.Content as string ?? "").ToArray();
		Assert.Contains("Widescreen", boxes);
		//One place per switch (ADR-0250): the pack's layers (Textures, Music)
		//moved to the pack detail sheet (W-P6), per game; this panel keeps the
		//synth and the renderer/overclock switches.
		Assert.Equal(new[] { "Modern instruments", "Border", "Widescreen", "Overclock" }, boxes);
		//...and the sheet points at the place for the look of the picture (W-P7).
		Assert.Contains(panel.FindAll<TextBlock>(), t => t.Text == "How the picture looks: Settings › Look");
	}

	//The window used to bind the tab id as the TabControl index; ids have holes
	//(removed consoles), so these opened the wrong tab.
	[AvaloniaTheory]
	[InlineData(ConfigWindowTab.Gameboy, typeof(GameboyConfigViewModel))]
	[InlineData(ConfigWindowTab.Sms, typeof(SmsConfigViewModel))]
	[InlineData(ConfigWindowTab.Preferences, typeof(PreferencesConfigViewModel))]
	public void Each_tab_id_opens_its_own_tab(ConfigWindowTab tab, Type content)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ShowPlayer(ConsoleType.Nes, RomFormat.iNes);
		ConfigWindow window = new(tab);
		window.Show();
		Dispatcher.UIThread.RunJobs();
		TabItem selected = window.FindNamed<TabControl>("AdvancedSettingsTabs").Items.Cast<TabItem>().Single(t => t.IsSelected);
		Assert.IsType(content, selected.Content);
		window.Close();
	}
}
