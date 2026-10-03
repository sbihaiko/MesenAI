using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Diagnostics;
using System.IO;

namespace Mesen.ViewModels
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P12): the first-run sheet. Same
	//choices as the setup wizard it replaces, fewer words: where saves and
	//settings live, and one keyboard popup. Both gamepad presets are always
	//applied (UI/Logic/PlayFirstRun). Esc and the close button keep what is
	//selected and continue - there is no Cancel.
	public partial class SetupWizardViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool StoreInUserProfile { get; set; } = PlayFirstRun.Defaults.StoreInUserProfile;
		//0 = Arrow keys + S / A, 1 = WASD + K / J (FirstRunKeyboard order).
		[ObservableProperty] public partial int KeyboardIndex { get; set; } = (int)PlayFirstRun.Defaults.Keyboard;

		[ObservableProperty] public partial string UserFolder { get; set; }
		[ObservableProperty] public partial bool CreateShortcut { get; set; } = PlayFirstRun.Defaults.CreateShortcut;
		[ObservableProperty] public partial bool CheckForUpdates { get; set; } = PlayFirstRun.Defaults.CheckForUpdates;
		[ObservableProperty] public partial bool ShowsDesktopOptions { get; set; } = PlayFirstRun.ShowsDesktopOptions(OperatingSystem.IsMacOS());
		//W-X2: a folder that cannot be written is a sentence in the sheet, not a dialog.
		[ObservableProperty] public partial string ErrorText { get; protected set; } = "";

		public SetupWizardViewModel()
		{
			UserFolder = ConfigManager.DefaultDocumentsFolder;
		}

		public FirstRunChoice Choice => new(StoreInUserProfile, (FirstRunKeyboard)KeyboardIndex, CheckForUpdates, CreateShortcut);

		public virtual bool Confirm()
		{
			FirstRunChoice choice = Choice;
			string targetFolder = choice.StoreInUserProfile ? ConfigManager.DefaultDocumentsFolder : ConfigManager.DefaultPortableFolder;
			try {
				WriteSettings(targetFolder, choice);
			} catch(Exception) {
				ErrorText = ResourceHelper.GetMessage("FirstRunCannotWrite", targetFolder);
				return false;
			}
			ErrorText = "";
			//Best effort: the settings are written, so a Desktop that is missing
			//or redirected must not keep the sheet open as an unwritable folder.
			if(choice.CreateShortcut && ShowsDesktopOptions) {
				try {
					CreateShortcutFile();
				} catch(Exception ex) {
					Debug.WriteLine("First run: desktop shortcut not created: " + ex.Message);
				}
			}
			return true;
		}

		//The folder probe and the settings file: a throw here is an unwritable folder.
		protected virtual void WriteSettings(string targetFolder, FirstRunChoice choice)
		{
			string testFile = Path.Combine(targetFolder, "test.txt");
			if(!Directory.Exists(targetFolder)) {
				Directory.CreateDirectory(targetFolder);
			}
			File.WriteAllText(testFile, "test");
			File.Delete(testFile);
			InitializeConfig(choice);
		}

		private static void InitializeConfig(FirstRunChoice choice)
		{
			ConfigManager.CreateConfig(!choice.StoreInUserProfile);
			FirstRunMappings m = PlayFirstRun.Mappings(choice.Keyboard);
			DefaultKeyMappingType mappingType = DefaultKeyMappingType.None;
			if(m.Xbox) {
				mappingType |= DefaultKeyMappingType.Xbox;
			}
			if(m.PlayStation) {
				mappingType |= DefaultKeyMappingType.Ps4;
			}
			if(m.Wasd) {
				mappingType |= DefaultKeyMappingType.WasdKeys;
			}
			if(m.Arrows) {
				mappingType |= DefaultKeyMappingType.ArrowKeys;
			}

			ConfigManager.Config.DefaultKeyMappings = mappingType;
			ConfigManager.Config.Preferences.AutomaticallyCheckForUpdates = choice.CheckForUpdates;
			ConfigManager.Config.Save();
		}

		protected virtual void CreateShortcutFile()
		{
			if(OperatingSystem.IsMacOS()) {
				return;
			}

			if(OperatingSystem.IsWindows()) {
				string linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mesen.url");
				FileHelper.WriteAllText(linkPath,
					"[InternetShortcut]" + Environment.NewLine +
					"URL=file:///" + Program.ExePath + Environment.NewLine +
					"IconIndex=0" + Environment.NewLine +
					"IconFile=" + Program.ExePath.Replace('\\', '/') + Environment.NewLine
				);
			} else {
				string shortcutFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "mesen.desktop");
				FileAssociationHelper.CreateLinuxShortcutFile(shortcutFile);
				Process.Start("chmod", "744 " + shortcutFile);
			}
		}
	}
}
