using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.IO;

namespace Mesen.ViewModels
{
	//ADR-0256 Decision 8: Play's Settings › System. The two things the retired
	//SetupWizardWindow asked, on a surface the pad drives like every other Play
	//sheet (PlayPadNavigationWiring.RegisterSurfaces claims this tab):
	//
	//- storage: the same two folders, written through the same ConfigManager
	//  path the wizard wrote (CreateConfig), with the relaunch the wizard's flow
	//  ended with offered as a button instead of pretending the move happened
	//  under a running process;
	//- keyboard: the same two presets (KeyPresets' arrow and WASD layouts, with
	//  both gamepad presets always on), writing the same setting the wizard
	//  wrote (Configuration.DefaultKeyMappings).
	//
	//Everything the wizard did NOT ask - the update check and the desktop
	//shortcut checkboxes - stays gone (PlayFirstRun.Defaults carries their
	//values); the Advanced door's Preferences tab keeps them.
	public partial class PlayerSystemSettingsViewModel : DisposableViewModel
	{
		//The folder the settings file would live in on each side, shown under
		//the radio that picks it.
		public string UserFolder { get; }
		public string PortableFolder { get; }

		[ObservableProperty] public partial bool StoreInUserProfile { get; set; }
		//The two radios the pad activates (PlayPadNavigationWiring.Activate's
		//RadioButton case) - an enum is what the row means, two bools are what a
		//radio binds to.
		[ObservableProperty, NotifyPropertyChangedFor(nameof(UsesArrowKeys), nameof(UsesWasd))] public partial FirstRunKeyboard Keyboard { get; set; }

		public bool UsesArrowKeys
		{
			get => Keyboard == FirstRunKeyboard.ArrowKeys;
			//A radio is only ever checked, never unchecked by pressing it, so the
			//false half of the pair has nothing to do.
			set { if(value) { Keyboard = FirstRunKeyboard.ArrowKeys; } }
		}

		public bool UsesWasd
		{
			get => Keyboard == FirstRunKeyboard.Wasd;
			set { if(value) { Keyboard = FirstRunKeyboard.Wasd; } }
		}

		//The relaunch is owed: the settings file is written in the chosen folder
		//and the one being left cannot win the next launch, but the core reads
		//its folders once, so nothing moves until the process starts again.
		[ObservableProperty] public partial bool RestartPending { get; set; }
		//The line under the choices: what was written, what it takes to apply
		//it, or why it could not be written.
		[ObservableProperty] public partial string NoticeText { get; set; } = "";

		private readonly Func<string> _homeFolder;
		private readonly Func<string> _documentsFolder;
		private readonly Func<string> _portableFolder;
		//Seeding the radios is not a choice: the two setters below write files.
		private bool _loading = true;

		public PlayerSystemSettingsViewModel(Func<string> homeFolder, Func<string> documentsFolder, Func<string> portableFolder)
		{
			_homeFolder = homeFolder;
			_documentsFolder = documentsFolder;
			_portableFolder = portableFolder;
			UserFolder = documentsFolder();
			PortableFolder = portableFolder();

			DefaultKeyMappingType mappings = ConfigManager.Config.DefaultKeyMappings;
			Keyboard = PlayFirstRun.KeyboardOf(
				mappings.HasFlag(DefaultKeyMappingType.WasdKeys),
				mappings.HasFlag(DefaultKeyMappingType.ArrowKeys)
			);
			StoreInUserProfile = PlayFirstRun.InUserFolder(homeFolder(), portableFolder());
			_loading = false;
		}

		partial void OnStoreInUserProfileChanged(bool value)
		{
			if(!_loading) {
				ChooseStorage(value);
			}
		}

		partial void OnKeyboardChanged(FirstRunKeyboard value)
		{
			if(!_loading) {
				ChooseKeyboard(value);
			}
		}

		//Pick the folder the settings file lives in. The process cannot absorb
		//it (ConfigManager.HomeFolder is resolved once, before the main window
		//exists), so a real change is written now and applied by the restart the
		//row then offers; picking the folder the process is already on changes
		//nothing and clears the offer.
		private void ChooseStorage(bool storeInUserProfile)
		{
			string target = PlayFirstRun.Folder(storeInUserProfile, _documentsFolder(), _portableFolder());
			string leaving = _homeFolder();
			RestartPending = PlayFirstRun.NeedsRestart(leaving, target);
			if(!RestartPending) {
				NoticeText = "";
				return;
			}

			try {
				WriteStorage(storeInUserProfile, leaving);
				NoticeText = ResourceHelper.GetMessage("SystemRestartPending");
			} catch(Exception) {
				//W-X2's sentence: a folder that cannot be written is said, not
				//swallowed, and the restart is not offered for a move that did
				//not happen.
				RestartPending = false;
				NoticeText = ResourceHelper.GetMessage("FirstRunCannotWrite", target);
			}
		}

		//The wizard's write, and the only way a storage choice reaches disk: the
		//folder is probed with a file first, then ConfigManager.CreateConfig -
		//the same call the wizard made - points HomeFolder at it and saves the
		//settings there. Virtual so the headless tests never touch the real user
		//folder or the one next to the executable.
		protected virtual void WriteStorage(bool storeInUserProfile, string leaving)
		{
			string target = PlayFirstRun.Folder(storeInUserProfile, _documentsFolder(), _portableFolder());
			Directory.CreateDirectory(target);
			string probe = Path.Combine(target, "test.txt");
			File.WriteAllText(probe, "test");
			File.Delete(probe);

			//The folder being left must not win the next launch: HomeFolder
			//prefers a settings.json next to the executable and otherwise takes a
			//documents folder that holds one, so the file is moved aside under
			//the name the Advanced door's Change Folder window already uses
			//(SelectStorageFolderViewModel.MigrateData). Only the settings file
			//moves: saves, packs and save states stay in the folder that holds
			//them, which is what makes Change Folder - the path that copies them,
			//with a progress bar - the door for moving a library.
			//
			//The switch below cannot be undone by retrying, so the three files it
			//touches are snapshotted first and put back if anything after it
			//throws - the in-memory folder with them. Without that a failed move
			//left both folders holding a settings.json, the old one won the next
			//launch and the choice reverted in silence, which is worse than the
			//"could not be written" the surface says.
			SettingsStorageMove move = SettingsStorageMove.Begin(target, leaving);
			string folderBefore = ConfigManager.HomeFolder;
			try {
				SwitchConfigFolder(storeInUserProfile);
				move.MoveOldSettingsAside();
			} catch {
				move.Rollback();
				ConfigManager.RestoreHomeFolder(folderBefore);
				throw;
			}
		}

		//The folder switch itself. `ConfigManager.CreateConfig` is the call the
		//wizard made, and it writes to the process's own default folders - which a
		//test must never do - so it is the second seam here (the folder functions
		//are the first). A test overrides this to write the same settings.json
		//into the temp folder it handed the constructor, which is what lets the
		//rollback above be exercised end to end.
		protected virtual void SwitchConfigFolder(bool storeInUserProfile)
		{
			ConfigManager.CreateConfig(portable: !storeInUserProfile);
		}

		//Pick the keyboard preset. It writes the setting the first run wrote and
		//then the keys themselves, through the seeding path the first run and the
		//Controller sheet's restore already share (Configuration.
		//SeedConsoleKeyDefaults), so the Xbox and PlayStation layouts and the
		//chosen keyboard layout all come from KeyPresets. The choice is explicit,
		//which is what makes writing the keys right here: ADR-0255's guard
		//(CanRestoreKeyboardPreset) protects the automatic restore from
		//overwriting keys nobody asked to replace, and it still owns that - the
		//Controller sheet keeps offering that button on its answer alone.
		private void ChooseKeyboard(FirstRunKeyboard keyboard)
		{
			FirstRunMappings mappings = PlayFirstRun.Mappings(keyboard);
			DefaultKeyMappingType flags = DefaultKeyMappingType.None;
			if(mappings.Xbox) {
				flags |= DefaultKeyMappingType.Xbox;
			}
			if(mappings.PlayStation) {
				flags |= DefaultKeyMappingType.Ps4;
			}
			if(mappings.Wasd) {
				flags |= DefaultKeyMappingType.WasdKeys;
			}
			if(mappings.Arrows) {
				flags |= DefaultKeyMappingType.ArrowKeys;
			}

			WriteKeyboard(flags);
			NoticeText = "";
		}

		//Virtual for the same reason as WriteStorage.
		protected virtual void WriteKeyboard(DefaultKeyMappingType flags)
		{
			ConfigManager.Config.DefaultKeyMappings = flags;
			ConfigManager.Config.SeedConsoleKeyDefaults();
			ConfigManager.Config.Input.ApplyConfig();
			ConfigManager.Config.Save();
		}

		//The relaunch: ConfigManager.RestartMesen is the running app's version of
		//what Program.cs did after the wizard wrote its settings file (start the
		//executable again) - it lets go of the single-instance mutex first, or
		//the new process would hand its arguments to this one and exit. Then this
		//window closes, which is what ends this process.
		public void Restart()
		{
			RestartMesen();
			CloseMainWindow();
		}

		protected virtual void RestartMesen() => ConfigManager.RestartMesen();

		protected virtual void CloseMainWindow() => ApplicationHelper.GetMainWindow()?.Close();
	}
}
