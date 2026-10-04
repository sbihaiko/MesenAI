using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mesen.Config
{
	public partial class Configuration : ObservableObject
	{
		private string _fileData = "";

		public string Version { get; set; } = "2.2.1";
		public int ConfigUpgrade { get; set; } = 0;
		public bool EnableTestMode { get; set; } = false;

		[ObservableProperty] public partial VideoConfig Video { get; set; } = new();
		[ObservableProperty] public partial AudioConfig Audio { get; set; } = new();
		[ObservableProperty] public partial InputConfig Input { get; set; } = new();
		[ObservableProperty] public partial EmulationConfig Emulation { get; set; } = new();
		[ObservableProperty] public partial NesConfig Nes { get; set; } = new();
		[ObservableProperty] public partial GameboyConfig Gameboy { get; set; } = new();
		[ObservableProperty] public partial SmsConfig Sms { get; set; } = new();
		[ObservableProperty] public partial EnhancementPackConfig EnhancementPacks { get; set; } = new();
		[ObservableProperty] public partial PlayerEnhancementsConfig PlayerEnhancements { get; set; } = new();
		[ObservableProperty] public partial GbaConfig Gba { get; set; } = new();
		[ObservableProperty] public partial PreferencesConfig Preferences { get; set; } = new();
		[ObservableProperty] public partial AudioPlayerConfig AudioPlayer { get; set; } = new();
		[ObservableProperty] public partial DebugConfig Debug { get; set; } = new();
		[ObservableProperty] public partial RecentItems RecentFiles { get; set; } = new();
		[ObservableProperty] public partial VideoRecordConfig VideoRecord { get; set; } = new();
		[ObservableProperty] public partial MovieRecordConfig MovieRecord { get; set; } = new();
		[ObservableProperty] public partial HdPackBuilderConfig HdPackBuilder { get; set; } = new();
		[ObservableProperty] public partial CheatWindowConfig Cheats { get; set; } = new();
		[ObservableProperty] public partial NetplayConfig Netplay { get; set; } = new();
		[ObservableProperty] public partial HistoryViewerConfig HistoryViewer { get; set; } = new();
		[ObservableProperty] public partial MainWindowConfig MainWindow { get; set; } = new();
		[ObservableProperty] public partial RemasterConfig Remaster { get; set; } = new();

		public DefaultKeyMappingType DefaultKeyMappings { get; set; } = DefaultKeyMappingType.Xbox | DefaultKeyMappingType.ArrowKeys;

		public Configuration()
		{
			//Used by JSON deserializer, don't call directly - use CreateConfig
		}

		public static Configuration CreateConfig()
		{
			return CreateConfig(settingsFileExists: false);
		}

		//settingsFileExists: a settings.json was there but could not be read
		//(#678). The key mappings and fonts were lost with it, so ConfigUpgrade
		//stays FirstRun either way; only the keys below differ.
		public static Configuration CreateConfig(bool settingsFileExists)
		{
			Configuration cfg = new();
			cfg.ConfigUpgrade = (int)ConfigUpgradeHint.FirstRun;
			MissingKeyDefaults defaults = SettingsLoadDefaults.For(settingsFileExists);
			//P.4 (PRD Part B §6): with no settings.json (fresh unzip, or the
			//Setup Wizard's first Config.Save before the main window runs) the
			//default rule says Player mode. An existing file - readable but
			//without the UiMode key, or unreadable - is the upgrade path (Advanced).
			cfg.Preferences.UiMode = defaults.UiMode;
			//ADR-0243 Q3: a new install records only on Remaster's Record
			cfg.EnhancementPacks.BootstrapEnhancementFolder = defaults.BootstrapEnhancementFolder;
			return cfg;
		}

		~Configuration()
		{
			//Try to save before destruction if we were unable to save at a previous point in time
			Save();
		}

		public void Save()
		{
			if(ConfigManager.DisableSaveSettings) {
				//Don't save to disk if command line option to disable setting updates was set
				return;
			}

			Serialize(ConfigManager.ConfigFile);
		}

		public void ApplyConfig()
		{
			Video.ApplyConfig();
			Audio.ApplyConfig();
			Input.ApplyConfig();
			Emulation.ApplyConfig();
			Gameboy.ApplyConfig();
			Gba.ApplyConfig();
			Nes.ApplyConfig();
			Sms.ApplyConfig();
			EnhancementPacks.ApplyConfig();
			Preferences.ApplyConfig();
			AudioPlayer.ApplyConfig();
			Debug.ApplyConfig();
		}

		public void InitializeFontDefaults()
		{
			if(ConfigUpgrade == (int)ConfigUpgradeHint.FirstRun) {
				Preferences.InitializeFontDefaults();

				Debug.Fonts.DisassemblyFont = GetDefaultMonospaceFont();
				Debug.Fonts.MemoryViewerFont = GetDefaultMonospaceFont();
				Debug.Fonts.AssemblerFont = GetDefaultMonospaceFont();
				Debug.Fonts.ScriptWindowFont = GetDefaultMonospaceFont();
				Debug.Fonts.OtherMonoFont = GetDefaultMonospaceFont(true);
				Debug.Fonts.ApplyConfig();
			}
		}

		public void UpgradeConfig()
		{
			RestoreKeyboardPresetIfNothingIsBound();

			if(ConfigUpgrade < (int)ConfigUpgradeHint.SmsInput) {
				Sms.InitializeDefaults(DefaultKeyMappings);
			}

			if(ConfigUpgrade < (int)ConfigUpgradeHint.GbaInput) {
				Gba.InitializeDefaults(DefaultKeyMappings);
			}

			if(ConfigUpgrade < (int)ConfigUpgradeHint.WindowsAudioLatency) {
				//Set audio back down to 30ms when upgrading from DirectSound to WASAPI
				if(OperatingSystem.IsWindows()) {
					Audio.AudioLatency = 30;
				}
			}

			//ADR-0243 Q3: an upgrade that kept "record while I play" on says so once
			if(BootstrapRecordingDefault.UpgradeNoticeDue(ConfigUpgrade < (int)ConfigUpgradeHint.RecordingOnDemand, EnhancementPacks.BootstrapEnhancementFolder)) {
				EmuApi.WriteLogEntry("[MEP] ADR-0243: BootstrapEnhancementFolder stays on for this install; new installs record only on Remaster's Record");
				EmuApi.DisplayMessage("MEP", "MepBootstrapNowOnDemand");
			}

			//ADR-0251: an upgrade gets ToggleOverlay's controller binding too
			if(ConfigUpgrade < (int)ConfigUpgradeHint.OverlayControllerBinding) {
				Preferences.SeedOverlayControllerBinding();
			}

			ConfigUpgrade = (int)ConfigUpgradeHint.NextValue - 1;
			Version = EmuApi.GetMesenVersion().ToString(3);
		}

		//ADR-0255 (the keyboard case): with no pad connected the keyboard has to
		//play, and DefaultKeyMappingType.None is the one value that leaves it with
		//nothing bound at all - no pad preset and no keyboard preset, so the
		//player cannot play and cannot reach the menus to fix it. ResetSettings
		//already refuses to *write* None, but only for callers that reset: a
		//settings.json already carrying 0 (hand-edited, or written by a build
		//whose wizard offered it) loads straight through, and InitializeDefaults
		//only ever runs on first run. The guard belongs where the presets are
		//resolved.
		//
		//...and only when nothing is bound anywhere. A config whose keys the
		//player bound by hand is theirs, and re-applying a preset over it would
		//be this same bug in reverse. Returns whether it wrote, which is what the
		//callers - and the test - need to tell "restored" from "left alone".
		public bool RestoreKeyboardPresetIfNothingIsBound()
		{
			if(!CanRestoreKeyboardPreset()) {
				return false;
			}
			DefaultKeyMappings = DefaultKeyMappingType.Xbox | DefaultKeyMappingType.ArrowKeys;
			Nes.InitializeDefaults(DefaultKeyMappings);
			Gameboy.InitializeDefaults(DefaultKeyMappings);
			Gba.InitializeDefaults(DefaultKeyMappings);
			Sms.InitializeDefaults(DefaultKeyMappings);
			return true;
		}

		//The one question the guard above asks, on its own: may the keyboard preset
		//be written back? DefaultKeyMappings.None is the state that leaves the player
		//unable to play, and "nothing is bound anywhere" (every console's player
		//ports - see NothingIsBound) is what keeps a preset from overwriting the
		//player's own keys. The Play Controller sheet's "Use the keyboard preset"
		//button is offered on THIS answer and no other, so the button can never be
		//shown where RestoreKeyboardPresetIfNothingIsBound would silently do nothing
		//(ADR-0255: "the guard belongs where the presets are resolved, not in one
		//caller"). This is the authority; callers ask it rather than restating it.
		public bool CanRestoreKeyboardPreset()
		{
			return DefaultKeyMappings == DefaultKeyMappingType.None && NothingIsBound();
		}

		//Whether every console's four mapping slots are empty - the state in
		//which a preset can be applied without overwriting anything. Read off the
		//slots' own fields rather than through ToInterop(): a console with default
		//custom keys reports them for an empty slot, which would answer "bound"
		//for a config where the player never bound anything.
		private bool NothingIsBound()
		{
			//The ports a player can play from, per console: the two NES and SMS
			//ports, GB's and GBA's single one. A config whose only keys sat in
			//Nes.ExpPort or Nes.MapperInput would call this "bound", which is
			//absurd enough to be worth not paying for.
			return !SendsAnyKey(Nes.Port1) && !SendsAnyKey(Nes.Port2)
				&& !SendsAnyKey(Gameboy.Controller)
				&& !SendsAnyKey(Gba.Controller)
				&& !SendsAnyKey(Sms.Port1) && !SendsAnyKey(Sms.Port2);
		}

		private static bool SendsAnyKey(ControllerConfig port)
		{
			foreach(KeyMapping m in new[] { port.Mapping1, port.Mapping2, port.Mapping3, port.Mapping4 }) {
				if(m.A != 0 || m.B != 0 || m.X != 0 || m.Y != 0 || m.L != 0 || m.R != 0
					|| m.Up != 0 || m.Down != 0 || m.Left != 0 || m.Right != 0
					|| m.Start != 0 || m.Select != 0 || m.U != 0 || m.D != 0
					|| m.TurboA != 0 || m.TurboB != 0 || m.TurboX != 0 || m.TurboY != 0
					|| m.TurboL != 0 || m.TurboR != 0 || m.TurboSelect != 0 || m.TurboStart != 0
					|| m.GenericKey1 != 0) {
					return true;
				}
			}
			return false;
		}

		public void InitializeDefaults()
		{
			if(ConfigUpgrade == (int)ConfigUpgradeHint.FirstRun) {
				Nes.InitializeDefaults(DefaultKeyMappings);
				Gameboy.InitializeDefaults(DefaultKeyMappings);
				Gba.InitializeDefaults(DefaultKeyMappings);
				Sms.InitializeDefaults(DefaultKeyMappings);
				ConfigUpgrade = (int)ConfigUpgradeHint.NextValue - 1;
			}
			Preferences.InitializeDefaultShortcuts();
		}

		private static HashSet<string>? _installedFonts = null;
		private static List<string>? _sortedFonts = null;

		[MemberNotNull(nameof(_installedFonts), nameof(_sortedFonts))]
		private static void InitInstalledFonts()
		{
			_installedFonts = new();
			_sortedFonts = new();
			try {
				int count = FontManager.Current.SystemFonts.Count;
				for(int i = 0; i < count; i++) {
					try {
						string? fontName = FontManager.Current.SystemFonts[i]?.Name;
						if(!string.IsNullOrWhiteSpace(fontName)) {
							_installedFonts.Add(fontName);
						}
					} catch { }
				}

				_sortedFonts.AddRange(_installedFonts);
				_sortedFonts.Sort();
			} catch {
			}
		}

		public static List<string> GetSortedFontList()
		{
			if(_sortedFonts == null) {
				InitInstalledFonts();
			}
			return new List<string>(_sortedFonts);
		}

		private static string FindMatchingFont(string defaultFont, params string[] fontNames)
		{
			if(_installedFonts == null) {
				InitInstalledFonts();
			}

			foreach(string name in fontNames) {
				if(_installedFonts.Contains(name)) {
					return name;
				}
			}

			return defaultFont;
		}

		public static string GetValidFontFamily(string requestedFont, bool preferMonoFont)
		{
			if(_installedFonts == null) {
				InitInstalledFonts();
			}

			if(_installedFonts.Contains(requestedFont)) {
				return requestedFont;
			}

			foreach(string name in _installedFonts) {
				if(preferMonoFont && name.Contains("Mono", StringComparison.InvariantCultureIgnoreCase)) {
					return name;
				} else if(!preferMonoFont && name.Contains("Sans", StringComparison.InvariantCultureIgnoreCase)) {
					return name;
				}
			}

			return _installedFonts.First();
		}

		public static FontConfig GetDefaultFont()
		{
			if(OperatingSystem.IsWindows()) {
				return new FontConfig() { FontFamily = "Microsoft Sans Serif", FontSize = 11 };
			} else if(OperatingSystem.IsMacOS()) {
				return new FontConfig() { FontFamily = FindMatchingFont("Microsoft Sans Serif"), FontSize = 11 };
			} else {
				return new FontConfig() { FontFamily = FindMatchingFont("FreeSans", "DejaVu Sans", "Noto Sans"), FontSize = 11 };
			}
		}

		public static FontConfig GetDefaultMenuFont()
		{
			if(OperatingSystem.IsWindows()) {
				return new FontConfig() { FontFamily = "Segoe UI", FontSize = 12 };
			} else if(OperatingSystem.IsMacOS()) {
				return new FontConfig() { FontFamily = FindMatchingFont("Microsoft Sans Serif"), FontSize = 12 };
			} else {
				return new FontConfig() { FontFamily = FindMatchingFont("FreeSans", "DejaVu Sans", "Noto Sans"), FontSize = 12 };
			}
		}

		public static FontConfig GetDefaultMonospaceFont(bool useSmallFont = false)
		{
			if(OperatingSystem.IsWindows()) {
				return new FontConfig() { FontFamily = "Consolas", FontSize = useSmallFont ? 12 : 14 };
			} else if(OperatingSystem.IsMacOS()) {
				return new FontConfig() { FontFamily = FindMatchingFont("PT Mono"), FontSize = useSmallFont ? 11 : 12 };
			} else {
				return new FontConfig() { FontFamily = FindMatchingFont("FreeMono", "DejaVu Sans Mono", "Noto Sans Mono"), FontSize = 12 };
			}
		}

		public static Configuration Deserialize(string configFile)
		{
			Configuration config;

			try {
				string fileData = File.ReadAllText(configFile);
				Configuration? loaded = (Configuration?)JsonSerializer.Deserialize(fileData, typeof(Configuration), MesenSerializerContext.Default);
				if(loaded == null) {
					//#678: a `null` document is as unreadable as a truncated one
					throw new JsonException("settings.json holds no configuration");
				}
				config = loaded;
				config._fileData = fileData;
			} catch {
				try {
					//File exists but couldn't be loaded, make a backup of the old settings before we overwrite them
					BackupSettings(configFile);
				} catch { }

				//#678: the file existed, so this is an existing install - upgrade-path defaults
				config = Configuration.CreateConfig(settingsFileExists: true);
			}

			return config;
		}

		public static void BackupSettings(string configFile)
		{
			//File exists but couldn't be loaded, make a backup of the old settings before we overwrite them
			string? folder = Path.GetDirectoryName(configFile);
			if(folder != null) {
				File.Copy(configFile, Path.Combine(folder, "settings." + DateTime.Now.ToString("yyyy-M-dd_HH-mm-ss") + ".bak"), true);
			}
		}

		public void Serialize(string configFile)
		{
			try {
				string cfgData = JsonSerializer.Serialize(this, typeof(Configuration), MesenSerializerContext.Default);
				if(_fileData != cfgData && !Design.IsDesignMode) {
					FileHelper.WriteAllText(configFile, cfgData);
					_fileData = cfgData;
				}
			} catch {
				//This can sometime fail due to the file being used by another Mesen instance, etc.
			}
		}

		public void RemoveObsoleteConfig()
		{
			//Clean up configuration to remove any obsolete values that existed in older versions
			for(int i = Preferences.ShortcutKeys.Count - 1; i >= 0; i--) {
				if(Preferences.ShortcutKeys[i].Shortcut >= EmulatorShortcut.LastValidValue) {
					Preferences.ShortcutKeys.RemoveAt(i);
				}
			}
		}
	}

	[Flags]
	public enum DefaultKeyMappingType
	{
		None = 0,
		Xbox = 1,
		Ps4 = 2,
		WasdKeys = 4,
		ArrowKeys = 8
	}

	public enum ConfigUpgradeHint
	{
		Uninitialized = 0,
		FirstRun,
		SmsInput,
		GbaInput,
		CvInput,
		WsInput,
		WindowsAudioLatency,
		RecordingOnDemand,
		OverlayControllerBinding,
		NextValue,
	}
}
