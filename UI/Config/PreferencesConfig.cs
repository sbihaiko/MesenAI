using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Config
{
	public partial class PreferencesConfig : BaseConfig<PreferencesConfig>
	{
		[ObservableProperty] public partial MesenTheme Theme { get; set; } = MesenTheme.Light;
		[ObservableProperty] public partial bool AutomaticallyCheckForUpdates { get; set; } = true;
		[ObservableProperty] public partial bool SingleInstance { get; set; } = true;
		[ObservableProperty] public partial bool AutoLoadPatches { get; set; } = true;

		//ADR-0254 (user's choice, 2026-10-04: "Ligado por padrão no Play"): on by
		//default. The pause is one global preference, so Classic and Advanced get
		//it too - they keep the silent pause, since W-P4 is a Play surface. A
		//configuration written before this keeps whatever it stored; nothing here
		//can tell "never chose" from "chose off".
		[ObservableProperty] public partial bool PauseWhenInBackground { get; set; } = true;
		[ObservableProperty] public partial bool PauseWhenInMenusAndConfig { get; set; } = false;
		[ObservableProperty] public partial bool AllowBackgroundInput { get; set; } = false;
		[ObservableProperty] public partial bool PauseOnMovieEnd { get; set; } = true;
		[ObservableProperty] public partial bool ShowMovieIcons { get; set; } = true;
		[ObservableProperty] public partial bool ShowTurboRewindIcons { get; set; } = true;
		[ObservableProperty] public partial bool ConfirmExitResetPower { get; set; } = false;

		[ObservableProperty] public partial bool AssociateNesRomFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateNesMusicFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateGbRomFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateGbMusicFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateGbaRomFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateSmsRomFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateGameGearRomFiles { get; set; } = false;
		[ObservableProperty] public partial bool AssociateSgRomFiles { get; set; } = false;

		[ObservableProperty] public partial bool EnableAutoSaveState { get; set; } = true;
		[ObservableProperty] public partial UInt32 AutoSaveStateDelay { get; set; } = 5;

		[ObservableProperty] public partial bool EnableRewind { get; set; } = true;
		[ObservableProperty] public partial UInt32 RewindBufferSize { get; set; } = 300;

		[ObservableProperty] public partial bool AlwaysOnTop { get; set; } = false;

		[ObservableProperty] public partial bool AutoHideMenu { get; set; } = false;

		//P.4 (PRD Part B §6): Player vs Advanced chrome. Defaults to
		//Advanced - the upgrade-safe value when an existing settings.json has
		//no UiMode key yet (the key is always written on first save, so this
		//initializer only ever matters once). A fresh unzip (no settings.json)
		//starts in Player, set explicitly in Configuration.CreateConfig.
		//ADR-0250: the Classic door owns UiMode.Advanced and a task door
		//UiMode.Player (MainWindowViewModel.OnWorkspaceChanged is the writer).
		//AutoHideMenu applies only in Classic, the one door with a menu bar.
		[ObservableProperty] public partial UiMode UiMode { get; set; } = UiMode.Advanced;

		//G.1 (ADR-0241, PRD Part B §13.2), ADR-0250: the active door (Play,
		//Remaster, Share, Classic), shown one at a time. A missing or unknown
		//key is Play, except that an Advanced install opens in Classic
		//(WorkspaceShell.InitialDoor).
		[ObservableProperty] public partial Workspace Workspace { get; set; } = Workspace.Play;

		//ADR-0251: how many game starts have shown "· Esc for the menu" in the
		//W-P3 entry toast (PlayMenuHint). A missing key is 0 for an install and
		//an upgrade alike, and nothing resets it.
		[ObservableProperty] public partial int PlayMenuHintsShown { get; set; } = 0;

		[ObservableProperty] public partial bool ShowFps { get; set; } = false;
		[ObservableProperty] public partial bool ShowFrameCounter { get; set; } = false;
		[ObservableProperty] public partial bool ShowGameTimer { get; set; } = false;
		[ObservableProperty] public partial bool ShowLagCounter { get; set; } = false;
		[ObservableProperty] public partial bool ShowTitleBarInfo { get; set; } = false;
		[ObservableProperty] public partial bool ShowDebugInfo { get; set; } = false;
		[ObservableProperty] public partial bool DisableOsd { get; set; } = false;
		[ObservableProperty] public partial HudDisplaySize HudSize { get; set; } = HudDisplaySize.Fixed;
		[ObservableProperty] public partial GameSelectionMode GameSelectionScreenMode { get; set; } = GameSelectionMode.ResumeState;

		[ObservableProperty] public partial FontAntialiasing FontAntialiasing { get; set; } = FontAntialiasing.SubPixelAntialias;
		[ObservableProperty] public partial FontConfig MesenFont { get; set; } = new FontConfig() { FontFamily = "Microsoft Sans Serif", FontSize = 11 };
		[ObservableProperty] public partial FontConfig MesenMenuFont { get; set; } = new FontConfig() { FontFamily = "Segoe UI", FontSize = 12 };

		[ObservableProperty] public partial List<ShortcutKeyInfo> ShortcutKeys { get; set; } = new List<ShortcutKeyInfo>();

		[ObservableProperty] public partial bool OverrideGameFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideAviFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideMovieFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideSaveDataFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideSaveStateFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideScreenshotFolder { get; set; } = false;
		[ObservableProperty] public partial bool OverrideWaveFolder { get; set; } = false;

		[ObservableProperty] public partial string GameFolder { get; set; } = "";
		[ObservableProperty] public partial string AviFolder { get; set; } = "";
		[ObservableProperty] public partial string MovieFolder { get; set; } = "";
		[ObservableProperty] public partial string SaveDataFolder { get; set; } = "";
		[ObservableProperty] public partial string SaveStateFolder { get; set; } = "";
		[ObservableProperty] public partial string ScreenshotFolder { get; set; } = "";
		[ObservableProperty] public partial string WaveFolder { get; set; } = "";

		public PreferencesConfig()
		{
		}

		private void AddShortcut(ShortcutKeyInfo shortcut)
		{
			if(!ShortcutKeys.Exists(a => a.Shortcut == shortcut.Shortcut)) {
				ShortcutKeys.Add(shortcut);
			}
		}

		public void InitializeDefaultShortcuts()
		{
			UInt16 ctrl = InputApi.GetKeyCode("Left Ctrl");
			UInt16 alt = InputApi.GetKeyCode("Left Alt");
			UInt16 shift = InputApi.GetKeyCode("Left Shift");

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.FastForward, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("Tab") }, KeyCombination2 = new KeyCombination() { Key1 = InputApi.GetKeyCode("Pad1 R2") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.Rewind, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("Backspace") }, KeyCombination2 = new KeyCombination() { Key1 = InputApi.GetKeyCode("Pad1 L2") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.IncreaseSpeed, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("=") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.DecreaseSpeed, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("-") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.MaxSpeed, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F9") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.IncreaseVolume, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("=") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.DecreaseVolume, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("-") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ToggleFps, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F10") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ToggleFullscreen, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F11") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.TakeScreenshot, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F12") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.Reset, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("R") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.PowerCycle, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("T") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ReloadRom, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = shift, Key3 = InputApi.GetKeyCode("R") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.Pause, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("Esc") } });
			//P.4 (PRD Part B §6): the overlay shortcut defaults to the
			//same Esc as Pause; in Player mode UiModeShortcutPrecedence gives
			//the overlay ownership of that key (the Pause binding is suppressed
			//on apply), so Esc opens the overlay instead of pausing. Binding it
			//to a controller button is a config choice - then Esc keeps meaning
			//Pause.
			//ADR-0251: the second slot is the controller's way into W-P4 -
			//Home/Guide where the platform reports one, else Select+Start.
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.ToggleOverlay, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("Esc") }, KeyCombination2 = DefaultOverlayControllerCombination() });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.RunSingleFrame, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("`") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale1x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("1") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale2x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("2") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale3x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("3") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale4x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("4") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale5x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("5") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale6x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("6") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale7x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("7") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale8x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("8") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale9x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("9") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SetScale10x, KeyCombination = new KeyCombination() { Key1 = alt, Key2 = InputApi.GetKeyCode("0") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.OpenFile, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("O") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot1, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F1") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot2, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F2") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot3, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F3") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot4, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F4") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot5, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F5") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot6, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F6") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateSlot7, KeyCombination = new KeyCombination() { Key1 = shift, Key2 = InputApi.GetKeyCode("F7") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.SaveStateToFile, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("S") } });

			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot1, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F1") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot2, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F2") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot3, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F3") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot4, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F4") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot5, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F5") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot6, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F6") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlot7, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F7") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateSlotAuto, KeyCombination = new KeyCombination() { Key1 = InputApi.GetKeyCode("F8") } });
			AddShortcut(new ShortcutKeyInfo { Shortcut = EmulatorShortcut.LoadStateFromFile, KeyCombination = new KeyCombination() { Key1 = ctrl, Key2 = InputApi.GetKeyCode("L") } });

			foreach(EmulatorShortcut value in Enum.GetValues<EmulatorShortcut>()) {
				if(value < EmulatorShortcut.LastValidValue) {
					AddShortcut(new ShortcutKeyInfo { Shortcut = value });
				}
			}
		}

		//ADR-0251: ToggleOverlay's default controller binding (PlayMenuHint).
		//keyCode defaults to the platform's key manager (InputApi.GetKeyCode).
		private static KeyCombination DefaultOverlayControllerCombination(Func<string, UInt16>? keyCode = null)
		{
			Func<string, UInt16> code = keyCode ?? InputApi.GetKeyCode;
			IReadOnlyList<string> keys = PlayMenuHint.DefaultControllerKeys(name => code(name) != 0);
			return new KeyCombination(keys.Select(code).ToList());
		}

		//ADR-0251, on upgrade: an existing settings.json already has its
		//ToggleOverlay entry, so AddShortcut keeps it as it was. The controller
		//binding goes into its second slot only when that slot is empty and no
		//other shortcut uses the combination.
		public void SeedOverlayControllerBinding(Func<string, UInt16>? keyCode = null)
		{
			ShortcutKeyInfo? overlay = ShortcutKeys.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay);
			if(overlay == null) {
				return;
			}
			Func<string, UInt16> code = keyCode ?? InputApi.GetKeyCode;
			IReadOnlyList<string> keys = PlayMenuHint.DefaultControllerKeys(name => code(name) != 0);
			KeyCombination combo = DefaultOverlayControllerCombination(code);
			string signature = ShortcutSignature(combo);
			//ADR-0255 slice 4: the pad slot is a binding too, so a combination is
			//already in use when any of a shortcut's three slots holds it.
			bool taken = ShortcutKeys.Any(sk => ShortcutSignatures(sk).Contains(signature));
			if(PlayMenuHint.SeedsControllerBinding(overlay.KeyCombination2.IsEmpty, taken, keys)) {
				overlay.KeyCombination2 = combo;
			}
		}

		//ADR-0251: the ToggleOverlay slots as key names, for the entry toast.
		//ADR-0255 slice 4: the pad slot is one of them - a shortcut may hold a
		//button and no key, so all three travel and PlayMenuHint.BindingName picks
		//the one that speaks for the device in the player's hand ("Home for the
		//menu" on a pad whose overlay binding is Home alone, "Esc for the menu"
		//when the pad count says keyboard).
		public (List<string> First, List<string> Second, List<string> Pad) OverlayBindingKeyNames()
		{
			ShortcutKeyInfo? overlay = ShortcutKeys.Find(sk => sk.Shortcut == EmulatorShortcut.ToggleOverlay);
			return (KeyNames(overlay?.KeyCombination), KeyNames(overlay?.KeyCombination2), PadKeyNames(overlay?.PadBinding));
		}

		private static List<string> PadKeyNames(PadShortcutBinding? pad)
		{
			if(pad == null || pad.IsEmpty) {
				return new List<string>();
			}
			string name = InputApi.GetKeyName(pad.KeyCode);
			return string.IsNullOrWhiteSpace(name) ? new List<string>() : new List<string>() { name };
		}

		private static List<string> KeyNames(KeyCombination? combo)
		{
			if(combo == null) {
				return new List<string>();
			}
			return new[] { combo.Key1, combo.Key2, combo.Key3 }.Where(code => code != 0)
				.Select(code => InputApi.GetKeyName(code)).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
		}

		public void UpdateFileAssociations()
		{
			FileAssociationHelper.UpdateFileAssociations();
		}

		public void ApplyFontOptions()
		{
			UpdateFonts();
		}

		private void UpdateFonts()
		{
			if(Application.Current != null) {
				string mesenFont = Configuration.GetValidFontFamily(MesenFont.FontFamily, false);
				string menuFont = Configuration.GetValidFontFamily(MesenMenuFont.FontFamily, false);

				if(Application.Current.Resources["MesenFont"] is FontFamily curMesenFont && curMesenFont.Name != mesenFont) {
					Application.Current.Resources["MesenFont"] = new FontFamily(mesenFont);
				}
				if(Application.Current.Resources["MesenMenuFont"] is FontFamily curMesenMenuFont && curMesenMenuFont.Name != menuFont) {
					Application.Current.Resources["MesenMenuFont"] = new FontFamily(menuFont);
				}

				if(Application.Current.Resources["MesenFontSize"] is double curMesenFontSize && curMesenFontSize != MesenFont.FontSize) {
					Application.Current.Resources["MesenFontSize"] = (double)MesenFont.FontSize;
				}
				if(Application.Current.Resources["MesenMenuFontSize"] is double curMesenMenuFontSize && curMesenMenuFontSize != MesenMenuFont.FontSize) {
					Application.Current.Resources["MesenMenuFontSize"] = (double)MesenMenuFont.FontSize;
				}
			}
		}

		public void InitializeFontDefaults()
		{
			MesenFont = Configuration.GetDefaultFont();
			MesenMenuFont = Configuration.GetDefaultMenuFont();
			ApplyFontOptions();
		}

		public static void UpdateTheme()
		{
			if(Application.Current != null) {
				ThemeVariant newTheme = ConfigManager.Config.Preferences.Theme == MesenTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
				if(Application.Current.RequestedThemeVariant != newTheme) {
					ConfigManager.ActiveTheme = ConfigManager.Config.Preferences.Theme;
					Application.Current.RequestedThemeVariant = newTheme;
				}
			}
		}

		public void ApplyConfig()
		{
			UpdateFonts();

			//P.4 (PRD Part B §6): in Player mode the overlay shortcut owns
			//its key(s) - any Pause/other binding on the same combination is
			//suppressed here, so the core never fires pause+overlay together
			//(it matches every pressed shortcut; identical-key shortcuts are
			//not subsets of each other). Advanced mode filters nothing - the
			//overlay binding stays configured but is ignored by ShortcutHandler.
			HashSet<string> overlayOwned = UiModeShortcutPrecedence.OverlayOwnedSignatures(
				UiMode,
				ShortcutKeys.Select(sk => new ShortcutBinding(
					sk.Shortcut == EmulatorShortcut.ToggleOverlay,
					ShortcutSignature(sk.KeyCombination)
				)).Where(b => !string.IsNullOrEmpty(b.Signature))
			);

			List<InteropShortcutKeyInfo> shortcutKeys = new List<InteropShortcutKeyInfo>();
			//ADR-0255 slice 4: the thresholds of the directions those shortcuts'
			//spare bindings name, pushed in the same call pair below.
			List<InteropPadAxisThreshold> axisThresholds = new List<InteropPadAxisThreshold>();
			//The core holds two key sets per shortcut - EmuSettings::SetShortcutKeys
			//fills sets 0 and 1 and ShortcutKeyHandler polls exactly those two, so a
			//third binding for one shortcut would overwrite the second instead of
			//adding to it. The pad slot has to fit under that ceiling.
			const int coreKeySetsPerShortcut = 2;
			foreach(ShortcutKeyInfo shortcutInfo in ShortcutKeys) {
				bool isOverlay = shortcutInfo.Shortcut == EmulatorShortcut.ToggleOverlay;
				int pushed = 0;
				if(!shortcutInfo.KeyCombination.IsEmpty && (isOverlay || !overlayOwned.Contains(ShortcutSignature(shortcutInfo.KeyCombination)))) {
					shortcutKeys.Add(new InteropShortcutKeyInfo(shortcutInfo.Shortcut, shortcutInfo.KeyCombination.ToInterop()));
					pushed++;
				}
				if(!shortcutInfo.KeyCombination2.IsEmpty && (isOverlay || !overlayOwned.Contains(ShortcutSignature(shortcutInfo.KeyCombination2)))) {
					shortcutKeys.Add(new InteropShortcutKeyInfo(shortcutInfo.Shortcut, shortcutInfo.KeyCombination2.ToInterop()));
					pushed++;
				}
				//ADR-0255 slice 4: the pad slot is a third binding, so it takes
				//whichever key set the combinations left free - a shortcut with a
				//button and no key has both empty and lands in set 0, which is the
				//case the ADR is about - and is dropped when there is none, never
				//silently replacing a key.
				//
				//An axis direction is pushed like any other button now that its
				//threshold travels with it (axisThresholds below): the core answers
				//the direction as this shortcut's key, and the threshold is what
				//decides when that key reads as pressed, so the player's setting is
				//no longer stored and ignored.
				if(pushed < coreKeySetsPerShortcut && shortcutInfo.PadBinding is PadShortcutBinding pad && !pad.IsEmpty) {
					shortcutKeys.Add(new InteropShortcutKeyInfo(shortcutInfo.Shortcut, pad.ToKeyCombination().ToInterop()));
					pushed++;

					//The threshold that governs the direction, pushed only for a
					//direction a spare binding actually names - every other axis
					//keeps the backend's own deadzone-derived magnitude, which is
					//what makes this feature invisible to a config that never used
					//it. Keyed by the direction with its device cleared, so it is the
					//direction's threshold and not that one pad's (ADR-0256
					//Decision 5).
					if(pad.IsAxis) {
						axisThresholds.Add(new InteropPadAxisThreshold() {
							Direction = PadAxisAction.DirectionKey(pad.KeyCode, InputApi.GetKeyName(pad.KeyCode)),
							ThresholdUnits = PadAxisAction.ThresholdUnits(pad.EffectiveThresholdPercent)
						});
					}
				}
			}
			ConfigApi.SetShortcutKeys(shortcutKeys.ToArray(), (UInt32)shortcutKeys.Count);
			ConfigApi.SetPadAxisThresholds(axisThresholds.ToArray(), (UInt32)axisThresholds.Count);

			ConfigApi.SetPreferences(new InteropPreferencesConfig() {
				ShowFps = ShowFps,
				ShowFrameCounter = ShowFrameCounter,
				ShowGameTimer = ShowGameTimer,
				ShowDebugInfo = ShowDebugInfo,
				ShowLagCounter = ShowLagCounter,
				DisableOsd = DisableOsd,
				AllowBackgroundInput = AllowBackgroundInput,
				PauseOnMovieEnd = PauseOnMovieEnd,
				ShowMovieIcons = ShowMovieIcons,
				ShowTurboRewindIcons = ShowTurboRewindIcons,
				DisableGameSelectionScreen = GameSelectionScreenMode == GameSelectionMode.Disabled,
				HudSize = HudSize,
				//The Core draws its toasts as the Player card only in Player mode.
				ToastStyle = HudToastStyleRule.For(UiMode),
				SaveFolderOverride = OverrideSaveDataFolder ? SaveDataFolder : "",
				SaveStateFolderOverride = OverrideSaveStateFolder ? SaveStateFolder : "",
				ScreenshotFolderOverride = OverrideScreenshotFolder ? ScreenshotFolder : "",
				RewindBufferSize = EnableRewind ? RewindBufferSize : 0,
				AutoSaveStateDelay = EnableAutoSaveState ? AutoSaveStateDelay : 0
			});
		}

		//P.4 (PRD Part B §6): the identity of one KeyCombination for
		//UiModeShortcutPrecedence - the ordered scan codes. Two shortcuts on
		//the identical combination share the same signature.
		private static string ShortcutSignature(KeyCombination combo)
		{
			return combo.Key1 + "-" + combo.Key2 + "-" + combo.Key3;
		}

		//ADR-0255 slice 4: a shortcut holds three bindings now - two key
		//combinations and the pad slot - and anything asking "is this combination
		//already used" has to see all three, or a seeded default could shadow a
		//button the player bound.
		private static IEnumerable<string> ShortcutSignatures(ShortcutKeyInfo info)
		{
			yield return ShortcutSignature(info.KeyCombination);
			yield return ShortcutSignature(info.KeyCombination2);
			if(info.PadBinding is PadShortcutBinding pad && !pad.IsEmpty) {
				yield return ShortcutSignature(pad.ToKeyCombination());
			}
		}
	}

	public enum MesenTheme
	{
		Light = 0,
		Dark = 1
	}

	public enum FontAntialiasing
	{
		Disabled,
		Antialias,
		SubPixelAntialias
	}

	public enum GameSelectionMode
	{
		Disabled,
		ResumeState,
		PowerOn
	}

	public enum HudDisplaySize
	{
		Fixed,
		Scaled,
	}

	public struct InteropPreferencesConfig
	{
		[MarshalAs(UnmanagedType.I1)] public bool ShowFps;
		[MarshalAs(UnmanagedType.I1)] public bool ShowFrameCounter;
		[MarshalAs(UnmanagedType.I1)] public bool ShowGameTimer;
		[MarshalAs(UnmanagedType.I1)] public bool ShowLagCounter;
		[MarshalAs(UnmanagedType.I1)] public bool ShowDebugInfo;
		[MarshalAs(UnmanagedType.I1)] public bool DisableOsd;
		[MarshalAs(UnmanagedType.I1)] public bool AllowBackgroundInput;
		[MarshalAs(UnmanagedType.I1)] public bool PauseOnMovieEnd;
		[MarshalAs(UnmanagedType.I1)] public bool ShowMovieIcons;
		[MarshalAs(UnmanagedType.I1)] public bool ShowTurboRewindIcons;
		[MarshalAs(UnmanagedType.I1)] public bool DisableGameSelectionScreen;

		public HudDisplaySize HudSize;
		public HudToastStyle ToastStyle;

		public UInt32 AutoSaveStateDelay;
		public UInt32 RewindBufferSize;

		public string SaveFolderOverride;
		public string SaveStateFolderOverride;
		public string ScreenshotFolderOverride;
	}
}
