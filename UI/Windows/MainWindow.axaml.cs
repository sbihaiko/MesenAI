using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Controls;
using Mesen.Debugger.Utilities;
using Mesen.Debugger.Windows;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public partial class MainWindow : MesenWindow
	{
		private DispatcherTimer _timerBackgroundFlag = new DispatcherTimer();
		private MainWindowViewModel _model = null!;

		private NotificationListener? _listener = null;
		private ShortcutHandler _shortcutHandler;

		private MouseManager? _mouseManager = null;
		private ContentControl _audioPlayer;
		private MainMenuView _mainMenu;
		private WorkspaceShellBar _shellBar;
		private CommandLineHelper? _cmdLine;

		private bool _testModeEnabled;
		private bool _needCloseValidation = true;
		private bool _isClosing = false;

		private bool _preventFullscreenToggle = false;

		//The headless tests close a window and keep the process-global core for
		//the next test: EmuApi.Release cannot be undone in one process. Null is
		//EmuApi.Release (not an initializer, so the constructor never names it).
		public Action? ReleaseCore { get; set; }

		//Set by the same caller, for the same reason (#840): a window a test
		//showed is closed when that test ends, and the quit confirmation -
		//ConfirmQuit for a recording or a Remaster job, then ValidateExit's
		//ConfirmExitResetPower or Player mode's stop banner - would *cancel* that
		//close instead. The window then outlives its test with its 50 ms pad
		//timer still armed, and the next test's Avalonia session setup fails in
		//EnsureIsolatedApplication, which is exactly the failure #840 reports.
		//A window the player closes still asks; only the harness's own close
		//skips the question.
		public bool SkipCloseConfirmation { get; set; }

		//#658: the Core's load thread waits here for the UI's answer (W-P13's
		//BIOS sheet, the classic firmware dialog) while it holds its load locks.
		private readonly CoreRequestWaits _coreRequests = new();

		private Panel _rendererPanel;
		private NativeRenderer _renderer;
		private SoftwareRendererView _softwareRenderer;
		private Size _rendererSize;
		private bool _usesSoftwareRenderer;

		//#619: the startup work OnOpened runs on a thread-pool thread (the core
		//init, then Dispatcher.UIThread.Post). Headless tests wait on it, so it
		//never outlives the test that opened the window.
		public Task Startup { get; private set; } = Task.CompletedTask;

		//#681: completes once Startup has finished (the core is initialized and
		//the command-line files' post is queued). On a cold launch the OS's
		//open-documents event can arrive before that; RunWhenStarted holds it.
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void RunWhenStarted(Action action)
		{
			_started.Task.ContinueWith(_ => Dispatcher.UIThread.Post(action), TaskScheduler.Default);
		}

		private FrameInfo _prevScreenSize;

		private Size _originalSize;
		private PixelPoint _originalPos;
		private WindowState _prevWindowState;

		//Used to suppress key-repeat keyup events on Linux
		private Dictionary<UInt16, IDisposable> _pendingKeyUpEvents = new();
		private bool _isLinux = false;

		private Stopwatch _stopWatch = Stopwatch.StartNew();
		private Dictionary<UInt16, long> _keyPressedStamp = new();
		private bool _focusInMenu;
		private bool _needRendererReset;

		//ADR-0254: the focus pause opened W-P4, so the automatic resume waits for
		//the player's Esc instead of firing the moment the window comes forward.
		private bool _focusPausedWithOverlay;

		//#967: where the focus poll reads "is the app active"; a headless test
		//swaps it to lose and regain focus.
		public IAppFocus AppFocus { get; set; } = Mesen.Windows.AppFocus.Desktop;

		public Control Renderer => _usesSoftwareRenderer ? _softwareRenderer : _renderer;

		static MainWindow()
		{
			WindowStateProperty.Changed.AddClassHandler<MainWindow>((x, e) => x.OnWindowStateChanged());
			IsActiveProperty.Changed.AddClassHandler<MainWindow>((x, e) => x.OnActiveChanged());
		}

		public MainWindow()
		{
			_testModeEnabled = ConfigManager.Config.EnableTestMode || System.Diagnostics.Debugger.IsAttached;
			_isLinux = OperatingSystem.IsLinux();
			_usesSoftwareRenderer = RendererPolicy.UsesSoftwareRenderer(ConfigManager.Config.Video.UseSoftwareRenderer, OperatingSystem.IsMacOS());

			_model = new MainWindowViewModel();
			DataContext = _model;
			InitGlobalShortcuts();

			EmuApi.InitDll();

			Directory.CreateDirectory(ConfigManager.HomeFolder);
			Directory.SetCurrentDirectory(ConfigManager.HomeFolder);

			InitializeComponent();

			//G.5: the Play edge-flow sheets' reload and controller poll.
			PlayEdgeFlowsWiring.Attach(this, _model);

			//ADR-0256 Decisions 2 and 3 (accepted 2026-10-04): the pad drives the
			//Play GUI, and one focusable control at a time holds it. Every surface's
			//focus-on-open claim - what this constructor, PlayEdgeFlowsWiring and
			//the sheets' own code-behind each posted for themselves - is registered
			//inside Attach, in one place, so the surfaces arbitrate instead of
			//racing each other's posts. The held repeat rides on the same tick
			//(PadNavRepeat); it is the slice's, and ADR-0256's decision list stops
			//at six rules without naming it.
			PlayPadNavigationWiring.Attach(this, _model);

			_shortcutHandler = new ShortcutHandler(this);

			AddHandler(DragDrop.DropEvent, OnDrop);

			//Allows us to catch LeftAlt/RightAlt key presses
			AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, true);
			AddHandler(InputElement.KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel, true);

			_rendererPanel = this.GetControl<Panel>("RendererPanel");
			_rendererPanel.LayoutUpdated += RendererPanel_LayoutUpdated;

			ResetRenderer();

			_softwareRenderer = this.GetControl<SoftwareRendererView>("SoftwareRenderer");
			_audioPlayer = this.GetControl<ContentControl>("AudioPlayer");
			_mainMenu = this.GetControl<MainMenuView>("MainMenu");
			_mainMenu.MainMenu.Opened += MainMenu_Opened;
			_shellBar = this.GetControl<WorkspaceShellBar>("ShellBar");
			_shellBar.ToolsMenu.Opened += MainMenu_Opened;
			InitShellTitleBar();
			InitPlaySheets();
			ConfigManager.Config.MainWindow.LoadWindowSettings(this);

			Console.CancelKeyPress += Console_CancelKeyPress;

		}

		[MemberNotNull(nameof(_renderer))]
		private void ResetRenderer()
		{
			if(_renderer != null && !_needRendererReset) {
				//Renderer needs to be reset when VRR mode is enabled, because DX11 does not allow switching
				//back to the non-flip swapchain model on a window that had the flip model enabled once.
				return;
			}

			ContentControl container = this.GetControl<ContentControl>("RendererContainer");
			_renderer = new NativeRenderer();
			_renderer.IsVisible = _model.IsNativeRendererVisible;
			_model.Renderer = _renderer;
			container.Content = _renderer;
			_needRendererReset = false;
		}

		private static void InitGlobalShortcuts()
		{
			if(Application.Current?.PlatformSettings == null) {
				return;
			}

			PlatformHotkeyConfiguration hotkeyConfig = Application.Current.PlatformSettings.HotkeyConfiguration;
			List<KeyGesture> gestures = hotkeyConfig.OpenContextMenu;
			for(int i = gestures.Count - 1; i >= 0; i--) {
				if(gestures[i].Key == Key.F10 && gestures[i].KeyModifiers == KeyModifiers.Shift) {
					//Disable Shift-F10 shortcut to open context menu - interferes with default shortcut for step back
					gestures.RemoveAt(i);
				}
			}
			hotkeyConfig.Copy.Add(new KeyGesture(Key.Insert, KeyModifiers.Control));
			hotkeyConfig.Paste.Add(new KeyGesture(Key.Insert, KeyModifiers.Shift));
			hotkeyConfig.Cut.Add(new KeyGesture(Key.Delete, KeyModifiers.Shift));
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			base.OnClosing(e);
			if(SkipCloseConfirmation) {
				//The harness is closing a window it showed (#840): there is no
				//player to ask, and a cancelled close would leak the window - and
				//its pad timer - into the next test. The exit path itself is
				//unchanged: CloseEmu still stops the emulator and runs ReleaseCore.
				_needCloseValidation = false;
			}
			//G.6 (W-X3): a recording or a Remaster job asks first, inline. Its
			//answer is the one confirmation (rule 7), so ConfirmExit is skipped.
			if(_needCloseValidation && _model != null && !_model.ConfirmQuit(() => { _needCloseValidation = false; Close(); })) {
				e.Cancel = true;
				return;
			}
			if(_needCloseValidation) {
				e.Cancel = true;
				ValidateExit();
			} else {
				if(!CloseEmu(false)) {
					e.Cancel = true;
				}
			}
		}

		private bool CloseEmu(bool force)
		{
			//Close all other windows first
			DebugWindowManager.CloseAllWindows();
			foreach(Window wnd in ApplicationHelper.GetOpenedWindows()) {
				if(wnd != this) {
					wnd.Close();
				}
			}

			if(!force && ApplicationHelper.GetOpenedWindows().Count > 1) {
				return false;
			}

			_timerBackgroundFlag.Stop();
			//#658: a load waiting on the BIOS sheet (or the classic firmware
			//dialog) holds the Core's load locks that Stop takes on this thread,
			//where the answer would run: answer every request first.
			_model?.BiosSheet.Dismiss();
			_coreRequests.Close();
			EmuApi.Stop();
			_listener?.Dispose();
			if(ReleaseCore != null) {
				ReleaseCore();
			} else {
				EmuApi.Release();
			}
			ConfigManager.Config.MainWindow.SaveWindowSettings(this);
			ConfigManager.Config.Save();
			_isClosing = true;

			return true;
		}

		private async void ValidateExit()
		{
			//ADR-0249 (W-X1): Player mode asks in place, the stop banner above the
			//profile (the shared InterruptionBar), never in a message box.
			if(_model.IsPlayerMode) {
				if(_model.ConfirmQuitApp(ConfigManager.Config.Preferences.ConfirmExitResetPower, QuitAfterConfirm)) {
					QuitAfterConfirm();
				}
				return;
			}
			if(!ConfigManager.Config.Preferences.ConfirmExitResetPower || await MesenMsgBox.Show(null, "ConfirmExit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
				_needCloseValidation = false;
				Close();
			}
		}

		private void QuitAfterConfirm()
		{
			_needCloseValidation = false;
			Close();
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			_mouseManager?.Dispose();
		}

		private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
		{
			_needCloseValidation = false;
			Dispatcher.UIThread.Post(() => {
				CloseEmu(true);
			});
		}

		private void OnDrop(object? sender, DragEventArgs e)
		{
			//#953: what the drop opens is DropRoute's (UI/Logic, pinned in UI.Tests).
			//No pack branch: a pack archive goes to the ROM loader, a folder is
			//reported missing.
			string? filename = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
			DropAction action = LoadRomHelper.Route(filename);
			if(action == DropAction.Ignore) {
				return;
			}
			LoadRomHelper.Run(action, filename!);
			if(action != DropAction.FileNotFound) {
				Activate();
			}
		}

		//P.4/G.2 (PRD Part B §6, §13.5.2 W-P4): the pause overlay's rows. Each
		//one routes at a surface that already exists - the slot grids, the pack
		//picker/window, the Enhancements panel, the Cheats sheet, the reduced
		//Settings page - and a sheet opened here closes back to the overlay.

		private void OnOverlayResume(object? sender, RoutedEventArgs e)
		{
			_model.IsPlayerOverlayVisible = false;
			EmuApi.Resume();
		}

		private void OnOverlaySaveStates(object? sender, RoutedEventArgs e) => _model.OpenSaveStatesSheet();
		private void OnSaveStatesBack(object? sender, RoutedEventArgs e) => _model.CloseSaveStatesSheet();
		private void OnSaveStatesReplays(object? sender, RoutedEventArgs e) => _model.OpenReplaysSheet();

		//#909 (W-P4): the grid's own per-slot actions. The row is the button's own
		//DataContext - a DataTemplate's named controls live in its own name scope,
		//so there is nothing to look up by name.
		private void OnSlotSaveHere(object? sender, RoutedEventArgs e)
		{
			if((sender as Control)?.DataContext is SaveStateSlotViewModel row) {
				_model.SaveSaveStateSlot(row);
			}
		}

		private void OnSlotLoad(object? sender, RoutedEventArgs e)
		{
			if((sender as Control)?.DataContext is SaveStateSlotViewModel row) {
				_model.LoadSaveStateSlot(row);
			}
		}

		//The grid scrolls (eleven rows do not fit the sheet), and the pad's
		//directional search only answers a row that is on screen: with just the
		//focused row scrolled into view, Down off the last visible row found no row
		//below it and left the grid for Shared replays, so slots 7 … 10 and the
		//auto-save were out of the pad's reach. Keeping one row either side of the
		//focused one in view is what lets every Down / Up land on the next row.
		private void OnSlotGotFocus(object? sender, FocusChangedEventArgs e)
		{
			if((sender as Control)?.Parent is Control row && row.Bounds.Height > 0) {
				double h = row.Bounds.Height;
				row.BringIntoView(new Rect(0, -h, row.Bounds.Width, h * 3));
			}
		}

		private void OnOverlayEnhancements(object? sender, RoutedEventArgs e)
		{
			//P.7 (§6.1): replaces the overlay with the quick-toggle panel,
			//same shape as OnOverlayPack replacing it with the picker.
			//ADR-0253 §4 (W.5): the core's per-game measurement is read as the
			//sheet opens, so the Widescreen switch already reflects it and the
			//per-ROM record is written for the next load.
			_model.SyncWidescreenSupport(EmuApi.GetMepRomSha1(), (WidescreenSupport)EmuApi.GetWidescreenSupportVerdict());
			_model.OpenEnhancementsPanel();
		}

		//P.10 (W-P11): replaces the overlay with the Cheats sheet.
		private void OnOverlayCheats(object? sender, RoutedEventArgs e) => _model.OpenCheatsSheet();

		//W-P4's Quit game powers the game off and lands on the Play home
		//(§13.6); the emulator and the window stay open. The existing
		//ConfirmExitResetPower preference still asks first, in place over the
		//overlay, which stays up when the answer is no.
		private void OnOverlayQuitGame(object? sender, RoutedEventArgs e)
		{
			//ADR-0249 (W-X1): the question is the stop banner on the card itself.
			//ADR-0256 Decision 3: the banner's Keep button is the pad's/arrow key's
			//way to answer the question, so it takes the focus the moment the
			//question appears. The goal is a surface's claim in
			//PlayPadNavigationWiring, which focuses the same button through the
			//same one path - no post of its own here.
			if(!_model.ConfirmQuitGame(ConfigManager.Config.Preferences.ConfirmExitResetPower, QuitGameFromOverlay)) {
				return;
			}
			QuitGameFromOverlay();
		}

		private void QuitGameFromOverlay()
		{
			_model.IsPlayerOverlayVisible = false;
			LoadRomHelper.PowerOff();
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);

			if(Design.IsDesignMode) {
				return;
			}

			_mouseManager = new MouseManager(this, _mainMenu, _usesSoftwareRenderer);

			ConfigManager.Config.InitializeFontDefaults();
			ConfigManager.Config.Preferences.ApplyFontOptions();
			ConfigManager.Config.Debug.Fonts.ApplyConfig();

			_timerBackgroundFlag.Interval = TimeSpan.FromMilliseconds(100);
			_timerBackgroundFlag.Tick += TimerUpdateBackgroundFlag;
			_timerBackgroundFlag.Start();

			//Give focus to panel to avoid menu being given focus by default
			this.GetControl<Panel>("RendererPanel").Focus();

			//ADR-0256 Decision 3: then the one arbiter decides who really holds
			//it, in the app's own terms - a Play surface if one is already up,
			//else Play's home / the recent-games or slot grid, else the renderer
			//just focused above. This replaces the raw FindDescendantOfType
			//<StateGrid>()?.Focus() that used to fight the home for the keyboard.
			PlayFocusOnOpen.Of(this)?.Refresh();

			Startup = Task.Run(() => {
				CommandLineHelper cmdLine = new CommandLineHelper(Program.CommandLineArgs, true);
				_cmdLine = cmdLine;

				EmuApi.InitializeEmu(
					ConfigManager.HomeFolder,
					TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
					_renderer.Handle,
					_usesSoftwareRenderer,
					cmdLine.NoAudio,
					cmdLine.NoVideo,
					cmdLine.NoInput
				);

				ConfigManager.Config.RemoveObsoleteConfig();

				//InitializeDefaults must be after InitializeEmu, otherwise keybindings will be empty
				ConfigManager.Config.InitializeDefaults();
				ConfigManager.Config.UpgradeConfig();

				_listener = new NotificationListener();
				_listener.OnNotification += OnNotification;

				_model.Init(this);

				ConfigManager.Config.ApplyConfig();

				//#887: the same rule as Open ROM's start folder - a games folder that
				//answers nothing is not registered with the core as a known folder.
				//What that list feeds is RomFinder alone (Core/Shared/RomFinder.h),
				//which resolves a ROM by name and CRC; registering a folder that
				//holds nothing buys nothing there and is the other half of the same
				//write MakeGamesFolder makes in the picker.
				string? gamesFolder = GamesFolderChoice.Usable(
					ConfigManager.Config.Preferences.OverrideGameFolder ? ConfigManager.Config.Preferences.GameFolder : null);
				if(gamesFolder != null) {
					EmuApi.AddKnownGameFolder(gamesFolder);
				}
				foreach(RecentItem recentItem in ConfigManager.Config.RecentFiles.Items) {
					EmuApi.AddKnownGameFolder(recentItem.RomFile.Folder);
				}

				ConfigManager.Config.Preferences.UpdateFileAssociations();
				SingleInstance.Instance.ArgumentsReceived += Instance_ArgumentsReceived;

				Dispatcher.UIThread.Post(() => {
					if(cmdLine.FilesToLoad.Count > 0) {
						//G.1 (PRD Part B §13.6, rule 11): a ROM opened from the OS
						//lands in Play.
						_model.LandInPlayForOsOpen();
					}
					cmdLine.LoadFiles();
					cmdLine.OnAfterInit(this);

					if(ConfigManager.Config.Preferences.AutomaticallyCheckForUpdates) {
						_model.MainMenu.CheckForUpdate(this, true);
					}
				});

				ConfigApi.CheckShaderSupport();
			});
			Startup.ContinueWith(_ => _started.TrySetResult(), TaskScheduler.Default);
		}

		private void Instance_ArgumentsReceived(object? sender, ArgumentsReceivedEventArgs e)
		{
			Dispatcher.UIThread.Post(() => {
				CommandLineHelper cmdLine = new(e.Args, false);

				//Set _cmdLine to allow Lua scripts to be loaded once/if a game is loaded
				_cmdLine = cmdLine;

				ConfigManager.Config.ApplyConfig();
				if(cmdLine.FilesToLoad.Count > 0) {
					_model.LandInPlayForOsOpen();
				}
				cmdLine.LoadFiles();
			});
		}

		private void OnNotification(NotificationEventArgs e)
		{
			DebugWindowManager.ProcessNotification(e);
			//#734: the Play load card ends with the first picture, a pause or a stop.
			OnLoadWaitNotification(e.NotificationType);

			switch(e.NotificationType) {
				case ConsoleNotificationType.GameLoaded:
					//A recording the user started never survives a load, so none holds
					//codes back here. The legacy "Record while I play" setting
					//(ADR-0243 Q3) starts a passive one with the load: it yields to a
					//code that changes the game, which ApplyCheats stops it for
					//(ADR-0245 amendment 2026-10-03).
					CheatCodes.SetRecordingArt(false);
					RomInfo romInfo = EmuApi.GetRomInfo();

					Dispatcher.UIThread.Post(() => {
						bool wasAudioFile = _model.AudioPlayer != null;
						bool updateConfig = _model.RomInfo.Format != romInfo.Format || _model.RomInfo.ConsoleType != romInfo.ConsoleType;
						_model.RomInfo = romInfo;

						if(updateConfig) {
							//Make sure any config overrides (video filter/aspect ratio) are applied when loading a different file
							ConfigManager.Config.Video.ApplyConfig();
						}

						bool isAudioFile = _model.AudioPlayer != null;
						if(wasAudioFile != isAudioFile) {
							//Force window size update when switching between an audio file and a regular rom
							Dispatcher.UIThread.Post(() => {
								ProcessResolutionChange();
							});
						}
					});

					GameConfig.LoadGameConfig(romInfo).ApplyConfig();

					//ADR-0169 section 4: a live session outlives the game that
					//started it, so the recorder is told which ROM its frames
					//now belong to - it re-targets its slot and republishes the
					//new game's palette/CHR instead of leaving the previous
					//game's files for the viewer to read as this one's.
					LiveRecordingSession.OnGameLoaded(romInfo);

					GameLoadedEventParams evtParams = Marshal.PtrToStructure<GameLoadedEventParams>(e.Parameter);
					bool loadedPaused = evtParams.IsPaused;
					Dispatcher.UIThread.Post(() => _model.IsGamePaused = loadedPaused);
					//#734: in Play the home and its load card stay until the first picture.
					bool holdsHome = HoldsHomeForPicture(loadedPaused);
					CommunityPackInstallService.OnGameLoaded(evtParams.IsPowerCycle);
					//W-P2: the hash the home's pack badge looks up later.
					RecentPackLookup.RememberLoadedGame(romInfo);
					//ADR-0253 §4 (W.5): a load starts the per-game measurement
					//from scratch; the window's Play poll writes the answer.
					Dispatcher.UIThread.Post(() => _model.BeginWidescreenMeasurement());

					//#732: a pack patch forced onto another revision of the game
					//(ApplyPatchOnHashMismatch) can freeze it; Player mode says so
					//in place, with a reload without it.
					string forcedPatch = EmuApi.GetForcedPackPatch();
					Dispatcher.UIThread.Post(() => _model.OnForcedPackPatch(forcedPatch));

					//P.5 (PRD Part B §5/§6): Player pack UX - the picker opens
					//once over the un-enhanced game when 2+ competing pack_ids exist
					//and no stored per-ROM choice applies; a pick stores the choice
					//(P.3) and power-cycles, and the reload applies silently (no
					//picker, just the "Applied ..." toast). No toast while the picker
					//is open - the game is un-enhanced until a pick.
					//ADR-0251: during the first three starts the toast also says
					//how to open W-P4 (a game without a pack gets that hint alone).
					bool isGameStart = !evtParams.IsPowerCycle;
					Dispatcher.UIThread.Post(() => {
						bool pickerOpen = _model.EvaluatePlayerPackPicker(EmuApi.GetMepPackList(), EmuApi.GetMepRomSha1());
						if(!pickerOpen) {
							_model.ShowPlayEntryToast(isGameStart);
							//ADR-0253 §4 (W.5): a game the core recorded as having
							//nothing beside the picture says so, once, as it loads.
							_model.AnnounceWidescreenUnavailable(EmuApi.GetMepRomSha1());
						}
					});

					//P.1-local (ADR-0206 §3): a hand-dropped container has no
					//content_id until the cache has seen it once, so it reads as a
					//separate `local:<name>` pack on its first load. The refresh is
					//off this path by design (it walks and hashes local packs), so it
					//runs on a background thread and only when some container is still
					//unidentified - and when it actually recomputed something, the
					//pack list is re-evaluated once so the §5 merge and the adopted
					//catalog pack_id apply in this session, not the next one.
					if(MepPackListParser.Parse(EmuApi.GetMepPackList()).Packs.Any(p => string.IsNullOrEmpty(p.ContentId))) {
						string loadedRomSha1 = EmuApi.GetMepRomSha1();
						Task.Run(() => {
							Int32 recomputed = EmuApi.RefreshMepLocalIdentities();
							if(recomputed <= 0) {
								return;
							}
							Dispatcher.UIThread.Post(() => {
								if(EmuApi.GetMepRomSha1() == loadedRomSha1) {
									_model.EvaluatePlayerPackPicker(EmuApi.GetMepPackList(), loadedRomSha1);
								}
							});
						});
					}
					if(!evtParams.IsPowerCycle) {
						Dispatcher.UIThread.Post(() => {
							if(!holdsHome) {
								ShowGamePicture();
							}

							DispatcherTimer.RunOnce(() => {
								if(_cmdLine != null) {
									_cmdLine?.ProcessPostLoadCommandSwitches(this);
									_cmdLine = null;
								}

								if(WindowState == WindowState.FullScreen || WindowState == WindowState.Maximized) {
									//Force resize of renderer when loading a game while in fullscreen
									//Prevents some issues when fullscreen was turned on before loading a game, etc.
									_rendererSize = new Size();
									ResizeRenderer();
								}
							}, TimeSpan.FromMilliseconds(50));
						});
					}

					Dispatcher.UIThread.Post(() => {
						ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.Close();
					});

					LoadRomHelper.ResetReloadCounter();
					break;

				case ConsoleNotificationType.GameLoadFailed:
					LoadRomHelper.ResetReloadCounter();
					break;

				//G.1 (W-S1): the shell bar shows while the game is paused.
				case ConsoleNotificationType.GamePaused:
				case ConsoleNotificationType.CodeBreak:
					Dispatcher.UIThread.Post(() => _model.IsGamePaused = true);
					break;

				case ConsoleNotificationType.DebuggerResumed:
				case ConsoleNotificationType.GameResumed:
					Dispatcher.UIThread.Post(() => {
						_model.IsGamePaused = false;
						_model.RecentGames.Visible = false;
						if(IsKeyboardFocusWithin) {
							this.GetControl<Panel>("RendererPanel").Focus();
						}
					});
					break;

				case ConsoleNotificationType.RequestConfigChange:
					Dispatcher.UIThread.Post(() => {
						UpdateInputConfiguration();
					});
					break;

				case ConsoleNotificationType.EmulationStopped:
					LiveRecordingSession.OnEmulationStopped();
					Dispatcher.UIThread.Post(() => {
						_model.IsGamePaused = false;
						_model.RomInfo = new RomInfo();
						_model.RecentGames.Init(GameScreenMode.RecentGames);
					});
					break;

				case ConsoleNotificationType.ResolutionChanged:
					Dispatcher.UIThread.Post(() => {
						ProcessResolutionChange();
					});
					break;

				case ConsoleNotificationType.ExecuteShortcut:
					ExecuteShortcutParams p = Marshal.PtrToStructure<ExecuteShortcutParams>(e.Parameter);
					Dispatcher.UIThread.Post(() => {
						_shortcutHandler.ExecuteShortcut(p.Shortcut);
					});
					break;

				case ConsoleNotificationType.MissingFirmware: {
					MissingFirmwareMessage msg = Marshal.PtrToStructure<MissingFirmwareMessage>(e.Parameter);
					//#658: answered by the UI below, or by CloseEmu before it stops
					//the Core; already answered when the app is quitting.
					TaskCompletionSource wait = _coreRequests.Begin();
					if(wait.Task.IsCompleted) {
						break;
					}
					Dispatcher.UIThread.Post(async () => {
						try {
							if(_coreRequests.IsClosed) {
								return;
							}
							//G.5 W-P13: Player mode gets the in-place sheet in every
							//workspace (ADR-0249); Advanced keeps the classic dialog loop.
							if(_model.IsPlayerMode) {
								string fileName = Marshal.PtrToStringUTF8(msg.Filename) ?? "";
								await _model.RequestBios(msg.Firmware, fileName, msg.Size, msg.AltSize, LoadRomHelper.RequestedGameName);
							} else {
								await FirmwareHelper.RequestFirmwareFile(msg);
							}
						} finally {
							_coreRequests.End(wait);
						}
					});
					wait.Task.Wait();
					break;
				}

				case ConsoleNotificationType.SufamiTurboFilePrompt: {
					SufamiTurboFilePromptMessage msg = Marshal.PtrToStructure<SufamiTurboFilePromptMessage>(e.Parameter);
					//#658: the same wait as MissingFirmware's.
					TaskCompletionSource wait = _coreRequests.Begin();
					if(wait.Task.IsCompleted) {
						break;
					}
					Dispatcher.UIThread.Post(async () => {
						try {
							if(!_coreRequests.IsClosed && await MesenMsgBox.Show(this, "PromptLoadSufamiTurbo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
								string? selectedFile = await FileDialogHelper.OpenFile(null, this, FileDialogHelper.SufamiTurboExt);
								//Once answered by closing, the Core no longer reads the message.
								if(selectedFile != null && !wait.Task.IsCompleted) {
									byte[] file = Encoding.UTF8.GetBytes(selectedFile);
									Array.Copy(file, msg.Filename, file.Length);
									Marshal.StructureToPtr<SufamiTurboFilePromptMessage>(msg, e.Parameter, false);
								}
							}
						} finally {
							_coreRequests.End(wait);
						}
					});
					wait.Task.Wait();
					break;
				}

				case ConsoleNotificationType.BeforeGameLoad:
					Dispatcher.UIThread.Post(() => {
						ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.Close();
					});
					break;

				case ConsoleNotificationType.RefreshSoftwareRenderer:
					SoftwareRendererFrame frame = Marshal.PtrToStructure<SoftwareRendererFrame>(e.Parameter);
					_softwareRenderer.UpdateSoftwareRenderer(frame);
					break;

				case ConsoleNotificationType.NetplayStopped:
					Dispatcher.UIThread.Post(() => {
						//Re-apply user config (netplay might temporarily modify options to match host server's options)
						ConfigManager.Config.ApplyConfig();
					});
					break;
			}
		}

		private static void UpdateInputConfiguration()
		{
			//Used to update input devices when the core requests changes (NES-only for now)
			ConfigManager.Config.Nes.UpdateInputFromCoreConfig();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void ProcessResolutionChange()
		{
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			FrameInfo baseScreenSize = EmuApi.GetBaseScreenSize();
			if(WindowState == WindowState.Normal) {
				double menuHeight = (ConfigManager.Config.Preferences.AutoHideMenu ? 0 : _mainMenu.Bounds.Height) + ShellChromeHeight;
				double height = ClientSize.Height - menuHeight - _audioPlayer.Bounds.Height;
				if(baseScreenSize.Width == _prevScreenSize.Height && baseScreenSize.Height == _prevScreenSize.Width) {
					//Rotation, swap sizes without changing scale
					double xScale = ClientSize.Width * dpiScale / _prevScreenSize.Width;
					double yScale = height * dpiScale / _prevScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale), Math.Round(yScale)));
				} else {
					double xScale = ClientSize.Width * dpiScale / baseScreenSize.Width;
					double yScale = height * dpiScale / baseScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale), Math.Round(yScale)));
				}
			} else if(WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) {
				if(_rendererSize == default) {
					ResizeRenderer();
				} else {
					double xScale = _rendererSize.Width * dpiScale / baseScreenSize.Width;
					double yScale = _rendererSize.Height * dpiScale / baseScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale, 2), Math.Round(yScale, 2)));
				}
			}
			_prevScreenSize = baseScreenSize;
		}

		public void SetScale(double scale)
		{
			if(scale < 1) {
				scale = 1;
			}

			//TODOv2 - Calling this twice seems to fix what might be an issue in Avalonia?
			//On the first call, when DPI > 100%, sometimes _rendererPanel's bounds are incorrect
			InternalSetScale(scale);
			InternalSetScale(scale);
		}

		private void InternalSetScale(double scale)
		{
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			double aspectRatio = EmuApi.GetAspectRatio();

			FrameInfo screenSize = EmuApi.GetBaseScreenSize();
			if(WindowState == WindowState.Normal) {
				_rendererSize = new Size();

				//When menu is set to auto-hide, don't count its height when calculating the window's final size
				double menuHeight = (ConfigManager.Config.Preferences.AutoHideMenu ? 0 : _mainMenu.Bounds.Height) + ShellChromeHeight;

				double width = Math.Max(MinWidth, Math.Round(screenSize.Height * aspectRatio * scale) / dpiScale);
				double height = Math.Max(MinHeight, screenSize.Height * scale / dpiScale);
				Width = width;
				Height = height + menuHeight + _audioPlayer.Bounds.Height;
				ResizeRenderer();
			} else if(WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) {
				_rendererSize = new Size(Math.Round(screenSize.Width * scale * aspectRatio) / dpiScale, Math.Round(screenSize.Height * scale) / dpiScale);
				ResizeRenderer();
			}
		}

		//G.1 (W-S1): the shell bar and status line, while on screen, take room
		//from the game like the classic menu bar does.
		private double ShellChromeHeight => _shellBar.IsVisible ? _shellBar.Bounds.Height + this.GetControl<Border>("ShellStatusLine").Bounds.Height : this.GetControl<Border>("ShellDragStrip").Bounds.Height;

		private void ResizeRenderer()
		{
			_rendererPanel.InvalidateMeasure();
			_rendererPanel.InvalidateArrange();
		}

		private void RendererPanel_LayoutUpdated(object? sender, EventArgs e)
		{
			double aspectRatio = EmuApi.GetAspectRatio();
			double dpiScale = LayoutHelper.GetLayoutScale(this);

			Size finalSize = _rendererSize == default ? _rendererPanel.Bounds.Size : _rendererSize;

			//#792: a pass the panel cannot answer - it has no bounds yet (window
			//setup, a Play transition). Sizing the native host to 0x0 does not just
			//draw nothing: Avalonia refuses to show a 0x0 native view and reports
			//nothing, so the game area stayed black, and EmuApi.SetRendererSize(0, 0)
			//stopped the core presenting, until some other event forced a resize -
			//which is why changing the scale or entering fullscreen "brought the
			//picture back". Keep what the last usable pass computed; the next pass
			//that has a panel updates it.
			if(!RendererViewportFit.HasSpace(finalSize.Width, finalSize.Height)) {
				return;
			}

			//P.7: the letterbox/pillarbox fit and the fullscreen integer-scale
			//rule are pure geometry, so they live in UI/Logic and are asserted
			//host-free by UI.Tests (RendererViewportFitTests). This method keeps
			//only what needs a window: the panel bounds, the DPI scale, the
			//window state and the assignments below.
			bool forceIntegerScale = ConfigManager.Config.Video.FullscreenForceIntegerScale && (WindowState == WindowState.FullScreen || WindowState == WindowState.Maximized);
			RendererViewport viewport = RendererViewportFit.Fit(finalSize.Width, finalSize.Height, aspectRatio, dpiScale, forceIntegerScale, EmuApi.GetBaseScreenSize().Height);
			double width = viewport.Width;
			double height = viewport.Height;

			uint realWidth = viewport.RealWidth;
			uint realHeight = viewport.RealHeight;
			EmuApi.SetRendererSize(realWidth, realHeight);
			_model.RendererSize = new Size(realWidth, realHeight);

			//Upstream 3924215 rounds realWidth/realHeight up to even numbers (a
			//shader seam) and snaps width/height to whole device pixels here.
			//Both rules live in RendererViewportFit instead, rounding down so
			//the picture never overflows the panel (P.7) - see
			//docs/validation/process/upstream-sync-3924215-2026-09-24.md.

			if(WindowState == WindowState.FullScreen && !ConfigManager.Config.Video.UseExclusiveFullscreen && ConfigManager.Config.Video.EnableVariableRefreshRate) {
				//When VRR is enabled, set the renderer to the same size as the monitor when in fullscreen mode
				PixelRect bounds = ApplicationHelper.GetMainWindow()?.Screens.Primary?.Bounds ?? default;
				if(bounds != default) {
					_rendererSize = bounds.Size.ToSize(LayoutHelper.GetLayoutScale(this));
					if(_model.IsMenuVisible) {
						_rendererSize = _rendererSize.WithHeight(_rendererSize.Height - _mainMenu.Bounds.Height);
					}
					_renderer.Width = _rendererSize.Width;
					_renderer.Height = _rendererSize.Height;
				}
			} else {
				_renderer.Width = width;
				_renderer.Height = height;
			}

			//Position the renderer in the center
			ContentControl container = this.GetControl<ContentControl>("RendererContainer");
			Canvas.SetTop(container, _rendererPanel.Bounds.Height > _renderer.Height ? (_rendererPanel.Bounds.Height - _renderer.Height) / 2 : 0);
			Canvas.SetLeft(container, _rendererPanel.Bounds.Width > _renderer.Width ? (_rendererPanel.Bounds.Width - _renderer.Width) / 2 : 0);

			_model.SoftwareRenderer.Width = width;
			_model.SoftwareRenderer.Height = height;
		}

		private void OnWindowStateChanged()
		{
			_rendererSize = new Size();
			ResizeRenderer();
			UpdateShellTitleBarInset();
			UpdateShellDragStrip();
		}

		//G.1 (W-S1; user's choice 2026-10-02, "Integrar agora"): on macOS the
		//shell bar is drawn in the title bar. It becomes the topmost row, so the
		//optional classic bar sits right under it instead of above it (above
		//would put the classic menus under the traffic lights). When the bar
		//hides (a Play game running unpaused) the game fills the whole window
		//and the traffic lights stay over its top-left corner; Esc/pause brings
		//the bar back. Windows/Linux keep the in-window strip (ShellTitleBar).
		//ADR-0250: Classic has no shell bar and keeps the plain title bar, so
		//the extension follows the door.
		private void InitShellTitleBar()
		{
			if(!ShellTitleBar.ExtendsIntoTitleBar(OperatingSystem.IsMacOS())) {
				return;
			}
			if(_shellBar.Parent is DockPanel dock) {
				dock.Children.Remove(_shellBar);
				dock.Children.Insert(0, _shellBar);
				Border strip = this.GetControl<Border>("ShellDragStrip");
				dock.Children.Remove(strip);
				dock.Children.Insert(1, strip);
			}
			Border dragStrip = this.GetControl<Border>("ShellDragStrip");
			dragStrip.PointerPressed += (_, e) => {
				if(e.GetCurrentPoint(dragStrip).Properties.IsLeftButtonPressed) {
					BeginMoveDrag(e);
				}
			};
			_model.Shell.WorkspaceChanged += _ => ApplyShellTitleBar();
			_model.Shell.PropertyChanged += (_, e) => {
				if(e.PropertyName == nameof(WorkspaceShellViewModel.IsBarVisible)) {
					UpdateShellDragStrip();
				}
			};
			ApplyShellTitleBar();
		}

		private void ApplyShellTitleBar()
		{
			bool extend = ShellTitleBar.ExtendsIntoTitleBar(OperatingSystem.IsMacOS(), _model.Shell.Active);
			ExtendClientAreaToDecorationsHint = extend;
			ExtendClientAreaTitleBarHeightHint = extend ? ShellTitleBar.Height : -1;
			UpdateShellTitleBarInset();
			UpdateShellDragStrip();
		}

		//The strip shows only while the bar is hidden, so the window can still
		//be dragged by its top edge; the game's own mouse input is untouched
		//(the strip is outside the renderer panel).
		private void UpdateShellDragStrip()
		{
			Border strip = this.GetControl<Border>("ShellDragStrip");
			double height = ShellTitleBar.DragStripHeight(ExtendClientAreaToDecorationsHint, WindowState == WindowState.FullScreen, _model.Shell.IsBarVisible);
			strip.Height = height;
			strip.IsVisible = height > 0;
		}

		private void UpdateShellTitleBarInset()
		{
			_shellBar?.SetLeadingInset(ShellTitleBar.LeadingInset(ExtendClientAreaToDecorationsHint, WindowState == WindowState.FullScreen));
		}

		private void SetFullscreenMode(FullscreenMode mode, IntPtr windowHandle)
		{
			EmuApi.SetFullscreenMode(new FullscreenSettings() {
				Mode = mode,
				WindowHandle = windowHandle,
				Width = ConfigManager.Config.Video.GetFullscreenWidth(),
				Height = ConfigManager.Config.Video.GetFullscreenHeight()
			});
		}

		public void ToggleFullscreen()
		{
			if(_preventFullscreenToggle) {
				return;
			}

			_preventFullscreenToggle = true;
			if(WindowState == WindowState.FullScreen) {
				ResetRenderer();

				Task.Run(() => {
					SetFullscreenMode(FullscreenMode.Disabled, _renderer.Handle);

					Dispatcher.UIThread.Post(() => {
						WindowState = _prevWindowState;
						if(_prevWindowState == WindowState.Normal) {
							Width = _originalSize.Width;
							Height = _originalSize.Height;
							Position = _originalPos;
						}
						_preventFullscreenToggle = false;
					});
				});
			} else {
				_originalSize = ClientSize;
				_originalPos = Position;
				_prevWindowState = WindowState;

				if(ConfigManager.Config.Video.UseExclusiveFullscreen) {
					if(!EmuApi.IsRunning()) {
						//Prevent entering fullscreen mode until a game is loaded
						_preventFullscreenToggle = false;
						return;
					}

					Task.Run(() => {
						SetFullscreenMode(FullscreenMode.Exclusive, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
						_preventFullscreenToggle = false;

						Dispatcher.UIThread.Post(() => {
							WindowState = WindowState.FullScreen;
						});
					});
				} else {
					Dispatcher.UIThread.Post(() => {
						if(ConfigManager.Config.Video.EnableVariableRefreshRate) {
							_needRendererReset = OperatingSystem.IsWindows();
							SetFullscreenMode(FullscreenMode.Borderless, _renderer.Handle);
						}
						WindowState = WindowState.FullScreen;
						_preventFullscreenToggle = false;
					});
				}
			}
		}

		protected override void OnLostFocus(FocusChangedEventArgs e)
		{
			base.OnLostFocus(e);
			if(WindowState == WindowState.FullScreen && ConfigManager.Config.Video.UseExclusiveFullscreen) {
				ToggleFullscreen();
			}
		}

		private bool ProcessTestModeShortcuts(Key key)
		{
			if(key == Key.F1) {
				if(TestApi.RomTestRecording()) {
					TestApi.RomTestStop();
				} else {
					RomTestHelper.RecordTest();
				}
				return true;
			} else if(key == Key.F2) {
				RomTestHelper.RunTest();
				return true;
			} else if(key == Key.F3) {
				RomTestHelper.RunAllTests();
				return true;
			} else if(key == Key.F7) {
				RomTestHelper.RunGbMicroTests();
				return true;
			} else if(key == Key.F8) {
				RomTestHelper.RunGambatteTests();
				return true;
			} else if(key == Key.F6) {
				//For testing purposes (to test for memory leaks)
				Task.Run(() => {
					for(int i = 0; i < 50; i++) {
						GC.Collect();
						GC.WaitForPendingFinalizers();
						Thread.Sleep(10);
					}
				});
				return true;
			}
			return false;
		}

		//G.1 (W-S3), ADR-0250: ⌘1-⌘4 (Ctrl+1-4 off macOS) switch to
		//Play/Remaster/Share/Classic directly, in the switcher's fixed order. Handled before the key reaches
		//the core, so it never doubles as an emulator input.
		private bool ProcessWorkspaceShortcut(KeyEventArgs e)
		{
			KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
			if(e.KeyModifiers != modifier) {
				return false;
			}
			int digit = e.Key switch {
				Key.D1 or Key.NumPad1 => 1,
				Key.D2 or Key.NumPad2 => 2,
				Key.D3 or Key.NumPad3 => 3,
				Key.D4 or Key.NumPad4 => 4,
				_ => 0
			};
			Workspace? target = WorkspaceShell.FromShortcutDigit(digit);
			if(target == null) {
				return false;
			}
			_model.SelectWorkspace(target.Value);
			e.Handled = true;
			return true;
		}

		private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
		{
			if(_testModeEnabled && e.KeyModifiers == KeyModifiers.Alt && ProcessTestModeShortcuts(e.Key)) {
				return;
			}

			if(ProcessWorkspaceShortcut(e)) {
				return;
			}

			if(OperatingSystem.IsMacOS()) {
				//Keyhandler handles key internally on macOS
				return;
			}

			if(_focusInMenu) {
				return;
			}

			if(e.Key != Key.None) {
				UInt16 keyCode = e.GetKeyCode();
				_keyPressedStamp[keyCode] = _stopWatch.ElapsedTicks;

				if(_isLinux && _pendingKeyUpEvents.TryGetValue(keyCode, out IDisposable? cancelTimer)) {
					//Cancel any pending key up event
					cancelTimer.Dispose();
					_pendingKeyUpEvents.Remove(keyCode);
				}

				InputApi.SetKeyState(keyCode, true);
			}

			if(e.Key == Key.Tab || e.Key == Key.F10) {
				//Prevent menu/window from handling these keys to avoid issue with custom shortcuts
				e.Handled = true;
			}
		}

		private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
		{
			if(OperatingSystem.IsMacOS()) {
				//Keyhandler handles key internally on macOS
				return;
			}

			if(e.Key != Key.None) {
				UInt16 keyCode = e.GetKeyCode();
				if(e.IsSpecialKey() && (!_keyPressedStamp.TryGetValue(keyCode, out long stamp) || ((_stopWatch.ElapsedTicks - stamp) * 1000 / Stopwatch.Frequency) < 10)) {
					//Key up received without key down, or key pressed for less than 10 ms, pretend the key was pressed for 50ms
					//Some special keys can behave this way (e.g printscreen)
					InputApi.SetKeyState(keyCode, true);
					DispatcherTimer.RunOnce(() => InputApi.SetKeyState(keyCode, false), TimeSpan.FromMilliseconds(50), DispatcherPriority.MaxValue);
					_keyPressedStamp.Remove(keyCode);
					return;
				}

				_keyPressedStamp.Remove(keyCode);

				if(_isLinux) {
					//Process keyup events after 1ms on Linux to prevent key repeat from triggering key up/down repeatedly
					IDisposable cancelTimer = DispatcherTimer.RunOnce(() => InputApi.SetKeyState(keyCode, false), TimeSpan.FromMilliseconds(1), DispatcherPriority.MaxValue);
					_pendingKeyUpEvents[keyCode] = cancelTimer;
				} else {
					InputApi.SetKeyState(keyCode, false);
				}
			}
		}

		private void OnActiveChanged()
		{
			if(!_isClosing) {
				//Only the main window's own active state was checked here before, so
				//focusing any other window belonging to the app (e.g. the Settings
				//dialog) counted as "in background" and triggered the volume
				//reduction/mute meant for switching away to a different application.
				//Check across all of the app's windows instead - "in background"
				//should mean none of them has focus.
				ConfigApi.SetEmulationFlag(EmulationFlags.InBackground, ApplicationHelper.GetActiveWindow() == null);
				InputApi.ResetKeyState();
			}
		}

		private void MainMenu_Opened(object? sender, RoutedEventArgs e)
		{
			UpdateAutoPause();
		}

		private void TimerUpdateBackgroundFlag(object? sender, EventArgs e)
		{
			bool focusInMenu = MenuHelper.IsFocusInMenu(_mainMenu.MainMenu) || MenuHelper.IsFocusInMenu(_shellBar.ToolsMenu);
			if(focusInMenu && !_focusInMenu) {
				InputApi.ResetKeyState();
			}
			_focusInMenu = focusInMenu;

			UpdateAutoPause();
		}

		private void UpdateAutoPause()
		{
			Window? activeWindow = AppFocus.GetActiveWindow();
			PreferencesConfig cfg = ConfigManager.Config.Preferences;

			//ADR-0254: the focus half is resolved on its own, because it is the one
			//that gets a voice - the menus/config half keeps its silent pause.
			bool focusLost = activeWindow == null && cfg.PauseWhenInBackground;
			bool needPause = focusLost;
			if(activeWindow != null) {
				bool isConfigWindow = (activeWindow != this) && !DebugWindowManager.IsDebugWindow(activeWindow);
				needPause |= cfg.PauseWhenInMenusAndConfig && !isConfigWindow && (_mainMenu.MainMenu.IsOpen || _shellBar.ToolsMenu.IsOpen); //in main menu or Tools ⋯
				needPause |= cfg.PauseWhenInMenusAndConfig && isConfigWindow; //in a window that's neither the main window nor a debug tool
			}

			if(needPause) {
				if(!EmuApi.IsPaused()) {
					_model.MainMenu.AutoPaused = true;

					DebuggerWindow? wnd = DebugWindowManager.GetDebugWindow<DebuggerWindow>(x => x.CpuType == _model.RomInfo.ConsoleType.GetMainCpuType());
					if(wnd != null) {
						//If the debugger window for the main cpu is opened, suppress the "bring to front on break" behavior
						wnd.SuppressBringToFront();
					}

					EmuApi.Pause();

					//ADR-0254: a focus pause in Play says why, and the player's own
					//Esc is the way back in. A game already paused by the player is
					//left alone: they have their own surface, or no need of one.
					if(FocusPause.ShowsOverlay(focusLost, _model.IsPlayerMode, _model.RomInfo.Format != RomFormat.Unknown)) {
						_focusPausedWithOverlay = true;
						_model.OpenPauseOverlay();
					}
				}
			} else if(_model.MainMenu.AutoPaused) {
				//Don't resume if the load/save state dialog is opened
				if(!_model.RecentGames.Visible && FocusPause.AutoResumes(_focusPausedWithOverlay, _model.IsPlayerOverlayVisible)) {
					EmuApi.Resume();
					_model.MainMenu.AutoPaused = false;
				}
			}
		}
	}
}
