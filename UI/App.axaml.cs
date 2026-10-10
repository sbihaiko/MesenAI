using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mesen
{
	public class App : Application
	{
		public override void Initialize()
		{
			//ADR-0256 Decision 8: the SetupWizardWindow (and the ShowConfigWindow
			//flag that made it the app's first window) left the startup path. Its
			//own new PreferencesConfig().InitializeFontDefaults() went with it:
			//MainWindow.OnOpened initializes the font defaults before anything
			//the player sees, and it is the only window the app now opens.
			if(Design.IsDesignMode) {
				RequestedThemeVariant = ThemeVariant.Light;
			} else {
				RequestedThemeVariant = ConfigManager.Config.Preferences.Theme == MesenTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
			}

			Dispatcher.UIThread.UnhandledException += (s, e) => {
				MesenMsgBox.ShowException(e.Exception);
				e.Handled = true;
			};

#if DEBUG
			this.AttachDeveloperTools();
#endif
			AvaloniaXamlLoader.Load(this);
			ResourceHelper.LoadResources();
		}

		public override void OnFrameworkInitializationCompleted()
		{
			PlayerSettingsEssentials.MenuSoundsAvailable = MenuSoundOutput.HostAvailable;
			PlayerSettingsEssentials.MenuTickAimable = HapticTickOutput.PadInHandAimable;
			if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				//Test if the core can be loaded, and display an error message popup if not
				try {
					EmuApi.TestDll();
				} catch(Exception ex) {
					bool sdlMissing = ex.Message.Contains("SDL2", StringComparison.InvariantCultureIgnoreCase);

					string errorMessage;
					if(sdlMissing) {
						errorMessage = ResourceHelper.GetMessage("UnableToStartMissingSdl", ex.Message);
					} else {
						errorMessage = ResourceHelper.GetMessage("UnableToStartMissingDependencies", ex.Message + Environment.NewLine + ex.StackTrace);
					}
					MessageBox.Show(null, errorMessage, "MesenAI", MessageBoxButtons.OK, MessageBoxIcon.Error, out MessageBox msgbox);
					desktop.MainWindow = msgbox;
					base.OnFrameworkInitializationCompleted();
					return;
				}

				try {
					desktop.MainWindow = new MainWindow();
				} catch {
					//Something broke when trying to load the main window, the settings file might be invalid/broken, try to reset them
					Configuration.BackupSettings(ConfigManager.ConfigFile);
					ConfigManager.ResetSettings(false);
					desktop.MainWindow = new MainWindow();
				}

				//The GUI test hook: only a process started with --test-hook gets one.
				//A path it cannot create is a startup failure, never a silent run.
				try {
					IDisposable? testHook = TestHookWiring.Start(Program.CommandLineArgs, (MainWindow)desktop.MainWindow);
					if(testHook is not null) {
						//Quit closes the window; the socket must not outlive it.
						desktop.MainWindow.Closed += (_, _) => testHook.Dispose();
					}
				} catch(Exception ex) {
					Console.Error.WriteLine("test hook unavailable: " + ex.Message);
					Environment.Exit(2);
				}

				//Issue #149: on macOS the OS delivers files to an already-running
				//app via an Apple 'open documents' event (ActivationKind.File). In
				//Avalonia 12 the event is surfaced by the IActivatableLifetime FEATURE
				//(Application.Current.TryGetFeature), NOT by the application-lifetime
				//object - `ApplicationLifetime is IActivatableLifetime` is always false
				//(ClassicDesktopStyleApplicationLifetime does not implement it), so the
				//original fix was a silent no-op. Route each file through the same
				//LoadRomHelper path used by drag-and-drop and the pipe handler.
				if(OperatingSystem.IsMacOS() && this.TryGetFeature<IActivatableLifetime>() is { } activatable) {
					activatable.Activated += OnActivated;
				}
				if(OperatingSystem.IsMacOS()) {
					InitAppMenu();
				}
			}
			base.OnFrameworkInitializationCompleted();
		}

		//ADR-0250 (Decisions 3 and 4): on macOS the shared tail's About MesenAI
		//and Settings… ⌘, live in the system app menu, in every door (Classic's
		//About, Preferences and Exit moved there too). Quit MesenAI ⌘Q is
		//Avalonia's default app-menu item, which closes the main window through
		//the same path as File › Exit.
		private void InitAppMenu()
		{
			NativeMenu menu = NativeMenu.GetMenu(this) ?? new NativeMenu();
			NativeMenuItem about = new(ResourceHelper.GetMessage("DoorMenuAbout"));
			about.Click += (s, e) => WithMainWindow((wnd, model) => model.MainMenu.OpenAbout(wnd));
			NativeMenuItem settings = new(ResourceHelper.GetMessage("DoorMenuSettings")) {
				Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma, Avalonia.Input.KeyModifiers.Meta)
			};
			settings.Click += (s, e) => WithMainWindow((wnd, model) => model.MainMenu.OpenSettings(wnd));
			//Issue #1008: Avalonia may already have appended its standard items
			//(Services… Quit); MacAppMenu puts About and Settings… above them.
			List<NativeMenuItemBase> items = MacAppMenu.Arrange<NativeMenuItemBase>(menu.Items, about, settings, () => new NativeMenuItemSeparator(), item => item is NativeMenuItemSeparator).ToList();
			foreach(NativeMenuItemBase item in menu.Items.ToList()) {
				menu.Items.Remove(item);
			}
			foreach(NativeMenuItemBase item in items) {
				menu.Items.Add(item);
			}
			NativeMenu.SetMenu(this, menu);
		}

		private static void WithMainWindow(Action<MainWindow, MainWindowViewModel> action)
		{
			if(ApplicationHelper.GetMainWindow() is MainWindow wnd && wnd.DataContext is MainWindowViewModel model) {
				action(wnd, model);
			}
		}

		//Issue #149: handles Apple 'open documents' events (Finder / `open -a`)
		//delivered to an already-running instance on macOS. Avalonia surfaces
		//these as FileActivatedEventArgs via IActivatableLifetime.Activated.
		//Each file is routed through LoadRomHelper.LoadFile - the same
		//path used by drag-and-drop and the SingleInstance named-pipe handler.
		private void OnActivated(object? sender, ActivatedEventArgs e)
		{
			if(e is not FileActivatedEventArgs fileArgs) {
				return;
			}
			MainWindow? window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
			foreach(IStorageItem file in fileArgs.Files) {
				if(file.TryGetLocalPath() is string localPath) {
					OpenFromOs(window, localPath);
				}
			}
		}

		//#681: on a cold launch the event can arrive before MainWindow.Startup
		//has initialized the core, so the open waits for it (the window's own
		//command-line files open first). Public for
		//UI.HeadlessTests/PlayEdgeFlowsLoadPathsTests; the production caller is OnActivated.
		public static void OpenFromOs(MainWindow? window, string localPath)
		{
			Action open = () => {
				//G.1 (PRD Part B §13.6, rule 11): a ROM opened from the OS lands in Play.
				MainWindowViewModel.Instance?.LandInPlayForOsOpen();
				LoadRomHelper.LoadFile(localPath);
			};
			if(window != null) {
				window.RunWhenStarted(open);
			} else {
				Dispatcher.UIThread.Post(open);
			}
		}
	}
}
