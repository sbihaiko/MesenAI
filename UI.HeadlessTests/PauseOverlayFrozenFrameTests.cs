using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//User's decisions, 2026-10-03. "Sim, quadro congelado": with the native
//picture hidden under W-P4 (PlayGameLayer), the game's last frame shows behind
//the scrim as an Avalonia image, at the live picture's size, and goes away when
//the game resumes. "Seguir o render": a sheet opened from W-P4 leaves the card
//on screen, dimmed behind the sheet (W-P5 … W-P16). The rules are pinned
//host-free in UI.Tests/Play/PlayPausedPictureTests; this checks the realized
//window against the real core with a running game.
[Collection(NativeCoreCollection.Name)]
public class PauseOverlayFrozenFrameTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-frozen-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		EmuApi.Stop();
		Dispatcher.UIThread.RunJobs();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
		ConfigManager.Config.Save();
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowRunningGame()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;

		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus (MainMenuViewModel.Initialize).");

		Directory.CreateDirectory(_folder);
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning() && model.RomInfo.Format != RomFormat.Unknown, "the ROM never reported as loaded");
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused() && !model.IsGamePaused && !model.RecentGames.Visible, "the game never ran unpaused");
		WaitFor(() => FrameCaptureApi.HeadlessGetFrameCount() > 5, "the game never rendered a frame");
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

	private static void Click(MainWindow window, string button)
	{
		window.FindNamed<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	[AvaloniaFact]
	public void W_p4_shows_the_frozen_frame_at_the_live_pictures_size_until_the_game_resumes()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowRunningGame();
		Image frame = window.FindNamed<Image>("PausedGameFrame");
		Assert.False(frame.IsOnScreen());

		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
		Assert.False(model.IsNativeRendererVisible);
		Assert.True(frame.IsOnScreen());
		Bitmap bitmap = Assert.IsAssignableFrom<Bitmap>(frame.Source);
		Assert.True(bitmap.PixelSize.Width >= 256 && bitmap.PixelSize.Height >= 224, $"the frozen frame is {bitmap.PixelSize}");
		//Same box as the live picture, so nothing jumps when the game pauses.
		Assert.Equal(model.SoftwareRenderer.Width, frame.Bounds.Width, 2);
		Assert.Equal(model.SoftwareRenderer.Height, frame.Bounds.Height, 2);
		Assert.Equal(Stretch.Fill, frame.Stretch);
		//Under the scrim, in the same Play layer.
		Panel workspace = window.FindNamed<Panel>("PlayWorkspace");
		Assert.True(workspace.Children.IndexOf(frame) < workspace.Children.IndexOf(window.FindNamed<Border>("PlayerOverlayScrim")));

		model.TogglePlayerOverlay();
		WaitFor(() => !EmuApi.IsPaused(), "Esc on the overlay did not resume");
		Assert.False(frame.IsOnScreen());
		Assert.Null(frame.Source);
		Assert.True(model.IsNativeRendererVisible);
	}

	[AvaloniaFact]
	public void A_sheet_from_w_p4_keeps_the_card_dimmed_behind_it_and_the_same_frame()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowRunningGame();
		Border card = window.FindNamed<Border>("PlayerOverlay");

		model.TogglePlayerOverlay();
		WaitFor(() => model.IsGamePaused, "the overlay did not pause the game");
		Assert.True(card.IsOnScreen());
		Assert.True(card.IsHitTestVisible);

		foreach(string row in new[] { "OverlayCheatsButton", "OverlayEnhancementsButton", "OverlaySaveStatesButton" }) {
			Click(window, row);
			Assert.True(card.IsOnScreen(), $"{row}'s sheet hid the card");
			Border dim = window.FindNamed<Border>("PlayerSheetScrim");
			IImage? frozen = window.FindNamed<Image>("PausedGameFrame").Source;
			Assert.NotNull(frozen);
			Assert.False(card.IsHitTestVisible, $"the card behind {row}'s sheet still takes the pointer");
			Assert.True(dim.IsOnScreen(), $"the card behind {row}'s sheet is not dimmed");
			Panel workspace = window.FindNamed<Panel>("PlayWorkspace");
			Assert.True(workspace.Children.IndexOf(card) < workspace.Children.IndexOf(dim));
			Color color = Assert.IsAssignableFrom<ISolidColorBrush>(dim.Background).Color;
			Assert.Equal((Colors.Black.R, Colors.Black.G, Colors.Black.B, PauseCard.DimAlpha), (color.R, color.G, color.B, color.A));

			//The keyboard stays in the sheet: arrows never land on the card.
			for(int i = 0; i < 12; i++) {
				window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
				Dispatcher.UIThread.RunJobs();
				Visual? focused = window.FocusManager?.GetFocusedElement() as Visual;
				Assert.False(focused != null && (focused == card || focused.GetVisualAncestors().Contains(card)), $"ArrowDown in {row}'s sheet reached the dimmed card");
			}

			model.TogglePlayerOverlay();
			Dispatcher.UIThread.RunJobs();
			Assert.True(card.IsOnScreen());
			Assert.True(card.IsHitTestVisible);
			Assert.False(dim.IsOnScreen());
			//W-P4 and its sheets show one frame: going back keeps it.
			Assert.Same(frozen, window.FindNamed<Image>("PausedGameFrame").Source);
		}
	}
}
