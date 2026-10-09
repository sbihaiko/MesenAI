using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.Interop;
using System;
using System.Collections.Generic;

namespace Mesen.ViewModels
{
	public partial class ConfigViewModel : DisposableViewModel
	{
		[ObservableProperty] public partial AudioConfigViewModel? Audio { get; set; }
		[ObservableProperty] public partial InputConfigViewModel? Input { get; set; }
		//W-P8: Player mode's Audio and Controls tabs are short essentials lists,
		//not the classic pages (those open through "More in Options…").
		[ObservableProperty] public partial PlayerAudioSettingsViewModel? PlayerAudio { get; set; }
		[ObservableProperty] public partial PlayerControlsSettingsViewModel? PlayerControls { get; set; }
		[ObservableProperty] public partial VideoConfigViewModel? Video { get; set; }
		[ObservableProperty] public partial LookConfigViewModel? Look { get; set; }
		//G.4 (W-P8): Player mode's Display tab (the window), next to Look.
		[ObservableProperty] public partial PlayerWindowSettingsViewModel? Display { get; set; }
		//ADR-0256 Decision 8: Player mode's System tab - the first run's storage
		//and keyboard-preset choices, on a surface the pad drives.
		[ObservableProperty] public partial PlayerSystemSettingsViewModel? System { get; set; }
		[ObservableProperty] public partial PreferencesConfigViewModel? Preferences { get; set; }
		[ObservableProperty] public partial EmulationConfigViewModel? Emulation { get; set; }

		[ObservableProperty] public partial NesConfigViewModel? Nes { get; set; }
		[ObservableProperty] public partial GameboyConfigViewModel? Gameboy { get; set; }
		[ObservableProperty] public partial GbaConfigViewModel? Gba { get; set; }
		[ObservableProperty] public partial SmsConfigViewModel? Sms { get; set; }

		[ObservableProperty] public partial ConfigWindowTab SelectedIndex { get; set; }
		//The TabControl's position: tab ids have holes, positions do not
		//(ConfigWindowTabOrder). Binding the id directly opened the wrong tab
		//for Game Boy, GBA, SMS and Preferences.
		[ObservableProperty] public partial int SelectedTabIndex { get; set; }
		//G.4 (W-P8): the Player strip's position (PlayerSettingsEssentials.Tabs).
		//-1 outside Player mode, as SelectedTabIndex is -1 inside it, so only one
		//of the two strips realizes a tab's content.
		[ObservableProperty, NotifyPropertyChangedFor(nameof(IsPlayerVideoTab)), NotifyPropertyChangedFor(nameof(IsPlayerHintTab)), NotifyPropertyChangedFor(nameof(IsPlayerMoreTab)), NotifyPropertyChangedFor(nameof(PlayerSheetHeight)), NotifyPropertyChangedFor(nameof(ShowsPlayerExitFullscreen))] public partial int PlayerTabIndex { get; set; } = -1;
		//W-P8: the line under the group is Display's hint, or Audio's and
		//Controls' "More in Options…" link; Look has neither (Hold to Compare).
		public bool IsPlayerMoreTab => PlayerSettingsEssentials.TabAt(PlayerTabIndex) is ConfigWindowTab.Audio or ConfigWindowTab.Input;
		public bool IsPlayerHintTab => !IsPlayerVideoTab && !IsPlayerMoreTab;
		//ADR-0249 (W-P10): Look's footer is Hold to Compare; the Options hint
		//shows on the other tabs.
		public bool IsPlayerVideoTab => PlayerTabIndex == PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Look);
		//#910: Display's Exit fullscreen, in Done's row while the window is fullscreen.
		public bool ShowsPlayerExitFullscreen => PlayerSettingsEssentials.ShowsExitFullscreen(PlayerSettingsEssentials.TabAt(PlayerTabIndex), Display?.IsFullscreen == true);
		//ADR-0249 (W-P8, W-P10): the Settings sheet is as high as its tab needs.
		public double PlayerSheetHeight => PlayerSettingsEssentials.TabAt(PlayerTabIndex) is ConfigWindowTab tab ? PlayerSettingsEssentials.SheetHeight(tab) : PlayerSettingsEssentials.SheetHeight(ConfigWindowTab.Display);

		//Video and Look edit the same VideoConfig, so they share one snapshot
		//for Cancel/IsDirty, taken when the first of them opens.
		private VideoConfig? _originalVideo;
		//PRD Part B §6, W-P8: Player mode's Settings page shows only the
		//essentials strip (Display, Look, Audio, Controls); the window hides the
		//Advanced tab list.
		[ObservableProperty] public partial bool PlayerMode { get; set; }
		public bool AlwaysOnTop { get; }

		//Supplied by the window: Display's view-model needs the main window's
		//state and actions, which this class does not reach.
		private readonly Func<PlayerWindowSettingsViewModel>? _createDisplay;
		private readonly Func<PlayerSystemSettingsViewModel>? _createSystem;
		private readonly Func<IReadOnlyList<string>> _audioDevices;
		private readonly Func<int> _connectedPads;

		[Obsolete("For designer only")]
		public ConfigViewModel() : this(ConfigWindowTab.Audio) { }

		public ConfigViewModel(ConfigWindowTab selectedTab) : this(selectedTab, playerMode: false) { }

		public ConfigViewModel(ConfigWindowTab selectedTab, bool playerMode = false, Func<PlayerWindowSettingsViewModel>? createDisplay = null, Func<System.Collections.Generic.IReadOnlyList<string>>? audioDevices = null, Func<int>? connectedPads = null, Func<PlayerSystemSettingsViewModel>? createSystem = null)
		{
			AlwaysOnTop = ConfigManager.Config.Preferences.AlwaysOnTop;
			PlayerMode = playerMode;
			_createDisplay = createDisplay;
			_createSystem = createSystem;
			_audioDevices = audioDevices ?? (() => ConfigApi.GetAudioDevices());
			_connectedPads = connectedPads ?? (() => (int)InputApi.GetConnectedGamepadCount());
			//§6: Player starts on one of the essentials tabs; a non-essentials
			//selection (e.g. Preferences from the Advanced GUI) clamps to Display.
			SelectTab(playerMode ? PlayerSettingsEssentials.ClampToEssentials(selectedTab) : selectedTab);
		}

		partial void OnDisplayChanged(PlayerWindowSettingsViewModel? oldValue, PlayerWindowSettingsViewModel? newValue)
		{
			if(oldValue != null) {
				oldValue.PropertyChanged -= OnDisplayPropertyChanged;
			}
			if(newValue != null) {
				newValue.PropertyChanged += OnDisplayPropertyChanged;
			}
			OnPropertyChanged(nameof(ShowsPlayerExitFullscreen));
		}

		private void OnDisplayPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if(e.PropertyName == nameof(PlayerWindowSettingsViewModel.IsFullscreen)) {
				OnPropertyChanged(nameof(ShowsPlayerExitFullscreen));
			}
		}

		partial void OnSelectedIndexChanged(ConfigWindowTab value)
		{
			SelectTab(value);
		}

		partial void OnSelectedTabIndexChanged(int value)
		{
			if(ConfigWindowTabOrder.TabAt(value) is ConfigWindowTab tab) {
				SelectTab(tab);
			}
		}

		partial void OnPlayerTabIndexChanged(int value)
		{
			if(PlayerMode && PlayerSettingsEssentials.TabAt(value) is ConfigWindowTab tab) {
				SelectTab(tab);
			}
		}

		//"More in Options…": Look's opens Video, Audio's opens its own classic
		//page. The window watches PlayerMode and opens the Options window on
		//SelectedIndex (MainWindow.OnPlayerSettingsChanged).
		//
		//Controls' row no longer lands here when the window is listening: it asks
		//for the Play Controller sheet instead (ADR-0255 slice 1), and this is
		//the fallback a view nobody wired still gets.
		public void OpenInOptions(ConfigWindowTab essentialsTab)
		{
			if(PlayerSettingsEssentials.OptionsTabFor(essentialsTab) is ConfigWindowTab options) {
				LeaveEssentials();
				SelectTab(options);
			}
		}

		private void LeaveEssentials()
		{
			PlayerMode = false;
			PlayerTabIndex = -1;
		}

		public void SelectTab(ConfigWindowTab tab)
		{
			//W-P8: a tab outside the Player strip (Look's "More in Options…" opens
			//Video) expands the window to the full Options page.
			if(PlayerMode && !PlayerSettingsEssentials.IsEssentials(tab)) {
				LeaveEssentials();
			}

			//Create each view model when the corresponding tab is clicked, for performance
			switch(tab) {
				case ConfigWindowTab.Audio:
					if(PlayerMode) {
						PlayerAudio ??= AddDisposable(new PlayerAudioSettingsViewModel(ConfigManager.Config.Audio, _audioDevices()));
					} else {
						//Expanded from Settings: Cancel still restores what the sheet opened with.
						Audio ??= AddDisposable(new AudioConfigViewModel());
						if(PlayerAudio != null) {
							Audio.OriginalConfig = PlayerAudio.OriginalConfig;
						}
					}
					break;
				case ConfigWindowTab.Emulation: Emulation ??= AddDisposable(new EmulationConfigViewModel()); break;
				case ConfigWindowTab.Input:
					if(PlayerMode) {
						PlayerControls ??= AddDisposable(new PlayerControlsSettingsViewModel(ConfigManager.Config.Input, _connectedPads()));
					} else {
						Input ??= AddDisposable(new InputConfigViewModel());
						if(PlayerControls != null) {
							Input.OriginalConfig = PlayerControls.OriginalConfig;
						}
					}
					break;
				case ConfigWindowTab.Video:
					_originalVideo ??= ConfigManager.Config.Video.Clone();
					Video ??= AddDisposable(new VideoConfigViewModel() { OriginalConfig = _originalVideo });
					break;
				case ConfigWindowTab.Display:
					_originalVideo ??= ConfigManager.Config.Video.Clone();
					Display ??= AddDisposable(_createDisplay?.Invoke() ?? new PlayerWindowSettingsViewModel(ConfigManager.Config.Video, false, 0, () => { }, _ => { }, ConfigManager.Config.Preferences));
					break;
				case ConfigWindowTab.Look:
					_originalVideo ??= ConfigManager.Config.Video.Clone();
					Look ??= AddDisposable(new LookConfigViewModel() { OpenTab = SelectTab });
					Look.Refresh();
					break;
				case ConfigWindowTab.System:
					//ADR-0256 Decision 8: the three folders are functions, not
					//values, so the row reads the home folder the process is
					//actually on - and so a test can hand it a temp folder and a
					//recording write instead of the real ones.
					System ??= AddDisposable(_createSystem?.Invoke() ?? new PlayerSystemSettingsViewModel(
						() => ConfigManager.HomeFolder,
						() => ConfigManager.DefaultDocumentsFolder,
						() => ConfigManager.DefaultPortableFolder
					));
					break;

				case ConfigWindowTab.Nes:
					//TODOv2 fix this patch
					Preferences ??= AddDisposable(new PreferencesConfigViewModel());
					Nes ??= AddDisposable(new NesConfigViewModel(Preferences.Config));
					break;

				case ConfigWindowTab.Gameboy: Gameboy ??= AddDisposable(new GameboyConfigViewModel()); break;
				case ConfigWindowTab.Gba: Gba ??= AddDisposable(new GbaConfigViewModel()); break;
				case ConfigWindowTab.Sms: Sms ??= AddDisposable(new SmsConfigViewModel()); break;

				case ConfigWindowTab.Preferences: Preferences ??= AddDisposable(new PreferencesConfigViewModel()); break;
			}

			SelectedIndex = tab;
			if(PlayerMode) {
				SelectedTabIndex = -1;
				PlayerTabIndex = PlayerSettingsEssentials.IndexOf(tab);
			} else {
				SelectedTabIndex = ConfigWindowTabOrder.IndexOf(tab);
			}
		}

		public void SaveConfig()
		{
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			ConfigManager.Config.Preferences.UpdateFileAssociations();
		}

		public void RevertConfig()
		{
			ConfigManager.Config.Audio = Audio?.OriginalConfig ?? PlayerAudio?.OriginalConfig ?? ConfigManager.Config.Audio;
			ConfigManager.Config.Input = Input?.OriginalConfig ?? PlayerControls?.OriginalConfig ?? ConfigManager.Config.Input;
			ConfigManager.Config.Video = _originalVideo ?? ConfigManager.Config.Video;
			ConfigManager.Config.Preferences = Preferences?.OriginalConfig ?? ConfigManager.Config.Preferences;
			ConfigManager.Config.Emulation = Emulation?.OriginalConfig ?? ConfigManager.Config.Emulation;
			ConfigManager.Config.Nes = Nes?.OriginalConfig ?? ConfigManager.Config.Nes;
			ConfigManager.Config.Gameboy = Gameboy?.OriginalConfig ?? ConfigManager.Config.Gameboy;
			ConfigManager.Config.Gba = Gba?.OriginalConfig ?? ConfigManager.Config.Gba;
			ConfigManager.Config.Sms = Sms?.OriginalConfig ?? ConfigManager.Config.Sms;
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
		}

		public bool IsDirty()
		{
			return (
				(Audio?.OriginalConfig ?? PlayerAudio?.OriginalConfig)?.IsIdentical(ConfigManager.Config.Audio) == false ||
				(Input?.OriginalConfig ?? PlayerControls?.OriginalConfig)?.IsIdentical(ConfigManager.Config.Input) == false ||
				_originalVideo?.IsIdentical(ConfigManager.Config.Video) == false ||
				Preferences?.OriginalConfig.IsIdentical(ConfigManager.Config.Preferences) == false ||
				Emulation?.OriginalConfig.IsIdentical(ConfigManager.Config.Emulation) == false ||
				Nes?.OriginalConfig.IsIdentical(ConfigManager.Config.Nes) == false ||
				Gameboy?.OriginalConfig.IsIdentical(ConfigManager.Config.Gameboy) == false ||
				Gba?.OriginalConfig.IsIdentical(ConfigManager.Config.Gba) == false ||
				Sms?.OriginalConfig.IsIdentical(ConfigManager.Config.Sms) == false
			);
		}
	}
}
