using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config.Shortcuts;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mesen.Windows
{
	//ADR-0256 (accepted 2026-10-04), the host half of Decisions 2 and 3: while
	//W-P4 is up, or with no game loaded, the pad drives the GUI (focus moves,
	//something activates, something goes back) and one surface holds the focus
	//with it drawn. The held repeat rides on the same tick (PadNavRepeat) and is
	//Decision 7, added to ADR-0256 by the same amendment that decided the
	//mechanism below.
	//
	//The mechanism is the one the user decided on 2026-10-04, over the ADR's own
	//"translate pad events into keyboard navigation events": the bridge calls the
	//focus engine directly (IFocusManager.FindNextElement for the traversal,
	//InputElement.Focus with NavigationMethod.Directional to take it). Faking
	//arrow keys would have meant a second traversal implementation to keep in step
	//with Avalonia's, and the engine's is the one that already knows every
	//surface's layout. KeyboardNavigationHandler, which owns that traversal
	//internally, is not reachable from here (Avalonia 12.1.1, internal), and
	//FindNextElement is the same search behind the same public interface.
	//
	//Nothing about *what* a press means is decided here. The rules are host-free
	//and tested without a pad or a window: PadInHand (which pad is in the
	//player's hand), PadNaming (the backend's answer to which family a code
	//belongs to), PadNavControls.Resolve (the codes that pad's preset binds),
	//PlayPadNavigation.HasAuthority/Next (when the pad is the GUI's, and what a
	//press means), PadNavRepeat (the held repeat's two numbers). This file feeds
	//them and applies what they answer, so the pad is not a second source of
	//truth for any of it.
	public static class PlayPadNavigationWiring
	{
		//The app's existing poll cadence (PlayEdgeFlowsWiring's controller poll,
		//the key-binding grid): the pad's own state is sampled, never subscribed
		//to, so the bridge cannot depend on a platform's event model - which is
		//also why it does not hook OnPreviewKeyDown, whose macOS early return
		//would have made it platform-dependent.
		private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

		//The one installed bridge per window, so a test can reach the one its
		//window is running.
		private static readonly ConditionalWeakTable<MainWindow, Bridge> Installed = new();

		internal static DispatcherTimer Attach(MainWindow window, MainWindowViewModel model)
		{
			PlayFocusOnOpen focus = new(window);
			RegisterSurfaces(focus, window, model);

			Bridge bridge = new(window, model);
			Installed.AddOrUpdate(window, bridge);
			DispatcherTimer timer = new(PollInterval, DispatcherPriority.Background, (s, e) => bridge.Tick());
			timer.Start();
			return timer;
		}

		//The headless suite's door, and only that. The host's three inputs are
		//what a test cannot supply: the pressed set (MacOSKeyManager::GetPressedKeys
		//reads a pad's buttons off a live controller and ignores SetKeyState for any
		//code above the keyboard's range), the clock, and the backend's two
		//lookups - a headless build has no key manager at all, because
		//InitializeEmu registers one only when the window and the viewer both hand
		//it a platform handle, so GetKeyName answers "" for every code and
		//GetKeyCode 0 for every name.
		//
		//Everything above those three is the real path and the reason this is worth
		//having: PadInHand, PadNaming, PadNavControls.Resolve, PlayPadNavigation's
		//authority rule, PadNavRepeat's timing, and the focus application in
		//Apply/Activate. Public for the same reason ShortcutHandler.InputBarcode is;
		//the production caller is the no-argument Tick above.
		public static void TickForTest(MainWindow window, IReadOnlyCollection<ushort> pressed, TimeSpan delta, Func<ushort, string>? keyName = null, Func<string, ushort>? keyCode = null)
		{
			if(Installed.TryGetValue(window, out Bridge? bridge)) {
				bridge.Tick(pressed, delta, keyName, keyCode);
			}
		}

		//ADR-0256 Decision 3: ONE path decides who holds the focus when a Play
		//surface opens or closes. The surfaces are registered in the order the Esc
		//router itself walks them - TogglePlayerOverlay's QuitGameConfirm first,
		//then HandleEdgeFlowEsc (whose HandleInWindowSheetEsc puts Shader before
		//Tool), then PlayEsc.Next over CurrentPlaySheet()'s own if-chain - so a
		//pad's Back and the focus the pad opens each surface on agree on where
		//"back" is. Two surfaces up at once is what that order is for, and it is
		//reachable: Look's Adjust… opens the shader sheet over the Settings sheet,
		//which stays open beneath and comes back on close (InWindowSheets.cs).
		//Each entry keeps the trigger it always had (the same property, the same
		//first control); what changed is that they answer to one arbiter instead
		//of each posting its own Focus() and racing the others.
		private static void RegisterSurfaces(PlayFocusOnOpen focus, MainWindow window, MainWindowViewModel model)
		{
			//W-X1: Quit game's question, over everything.
			focus.When(model.QuitGameConfirm, [nameof(InterruptionViewModel.IsVisible)],
				() => model.QuitGameConfirm.IsVisible, () => Named(window, "QuitGameKeepButton"));
			//ADR-0250's task doors' sheets (the archive's list, Look's Adjust…, a
			//door's tool): HandleInWindowSheetEsc's order, SelectRom → Shader → Tool.
			focus.When(model.SelectRomSheet, [nameof(PlaySelectRomSheetViewModel.IsVisible)],
				() => model.SelectRomSheet.IsVisible, () => Named(window, "SelectRomSheetSearch"));
			focus.When(model, [nameof(MainWindowViewModel.IsShaderSheetVisible)],
				() => model.IsShaderSheetVisible, () => Named(window, "ShaderSheetOk"));
			//The tool sheet is a surface for every kind it can show, not only the
			//barcode: IsPlaySurfaceOverGame counts ToolSheet.IsVisible, and About,
			//Command Line, Check for Updates and the video recorder's settings all
			//come up as it. A claim that opened on IsBarcode alone left those kinds
			//with no claim at all, so the arbiter put the focus back on the content
			//under the sheet. The target follows the kind the same way: the barcode
			//box for the barcode kind, and the sheet's own first focusable control
			//otherwise - a single named control could not be both, and one that is
			//only on screen in the barcode kind is the same bug in a new place.
			focus.When(model.ToolSheet, [nameof(PlayerToolSheetViewModel.IsVisible)],
				() => model.ToolSheet.IsVisible,
				() => model.ToolSheet.IsBarcode ? Named(window, "ToolSheetBarcode") : FirstFocusable(window, "ToolSheet"));
			//The edge-flow sheets, which HandleEdgeFlowEsc answers only after the
			//in-window ones: Bios, then ControllerSetup.
			focus.When(model.BiosSheet, [nameof(PlayBiosSheetViewModel.IsVisible)],
				() => model.BiosSheet.IsVisible, () => Named(window, "BiosSheetChooseFile"));
			focus.When(model.ControllerSetup, [nameof(PlayControllerSetupViewModel.IsVisible)],
				() => model.ControllerSetup.IsVisible, () => Named(window, "ControllerSetupSkip"));
			//W-P4's sheets, in CurrentPlaySheet()'s order - the chain PlayEsc.Next
			//reads, so the surface Esc would close first is the one that holds the
			//focus: Settings, Controller (ADR-0255's sheet, read right after
			//Settings), PackDep, PackPicker, Enhancements, PackDetail, Cheats,
			//Replays, SaveStates. (The chain's SaveStateGrid is the grid itself,
			//which is content, not a sheet.)
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerSettingsVisible)],
				() => model.IsPlayerSettingsVisible, () => Named(window, "tabPlayerWindow"));
			//ADR-0255's Controller sheet, which CurrentPlaySheet() reads right
			//after Settings (one of the two is current at a time; the sheet
			//replaces the Settings sheet's Controls landing), so the arbiter's
			//order keeps mirroring the chain PlayEsc.Next walks. Done is the
			//sheet's own control, and the one to open it on: it is always on
			//screen and focusable whatever the sheet is showing (the pad picker
			//and the PLAYERS rows exist only with two or more pads, so with one or
			//with none the sheet's focusables are its two footer buttons), and a
			//Confirm on it closes the sheet back to W-P4 - where the other one,
			//More in Options…, leaves for the classic Input window, which
			//ADR-0256's non-goals say a pad cannot drive.
			focus.When(model.ControllerSheet, [nameof(ControllerSheetViewModel.IsVisible)],
				() => model.ControllerSheet.IsVisible, () => Named(window, "ControllerSheetDone"));
			focus.When(model.PackDepSheet, [nameof(PlayPackDepSheetViewModel.IsVisible)],
				() => model.PackDepSheet.IsVisible, () => Named(window, "PackDepSheetChooseFile"));
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerPackPickerVisible)],
				() => model.IsPlayerPackPickerVisible, () => PackPickerChoice(window));
			focus.When(model, [nameof(MainWindowViewModel.IsEnhancementsPanelVisible)],
				() => model.IsEnhancementsPanelVisible, () => Named(window, "EnhancementsModernCheckBox"));
			focus.When(model, [nameof(MainWindowViewModel.IsPackDetailVisible)],
				() => model.IsPackDetailVisible,
				() => Named(window, model.PackDetailCanChange ? "PackDetailChangeButton" : "PackDetailDoneButton"));
			focus.When(model.CheatsSheet, [nameof(PlayerCheatsSheetViewModel.IsVisible)],
				() => model.CheatsSheet.IsVisible,
				() => Named(window, model.CheatsSheet.IsSearchEnabled ? "CheatsSearchBox" : "CheatsDoneButton"));
			focus.When(model.ReplaysSheet, [nameof(PlayerReplaysSheetViewModel.IsVisible)],
				() => model.ReplaysSheet.IsVisible,
				() => EnabledNamed(window, "ReplaysWatchButton") ?? Named(window, "ReplaysDoneButton"));
			focus.When(model, [nameof(MainWindowViewModel.IsSaveStatesSheetVisible)],
				() => model.IsSaveStatesSheetVisible, () => Named(window, "SaveStatesSaveButton"));
			//W-P4 itself, under every sheet opened from it and over the game.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerOverlayVisible)],
				() => model.IsPlayerOverlayVisible, () => Named(window, "OverlayResumeButton"));

			//The content area under all of them: the home's primary action, the
			//Continue button, the slot grid over a game. It is not a claim (it is
			//not in the Esc stack, and it is also what Advanced shows), so it is
			//what the arbiter finds when no surface is up.
			focus.Content(model.RecentGames,
				[nameof(RecentGamesViewModel.Visible), nameof(RecentGamesViewModel.Mode),
				 nameof(RecentGamesViewModel.ShowFirstRunHome), nameof(RecentGamesViewModel.ShowRecentsHome),
				 nameof(RecentGamesViewModel.ShowPlainGrid), nameof(RecentGamesViewModel.ShowHomeGrid)],
				() => ContentFocus(window, model));
		}

		//W-P1/W-P2: the home's primary action, or W-P3's Continue, or the slot
		//grid over a game - whichever of them this window is showing. Null when
		//the content area is showing nothing (a game running with nothing up),
		//which leaves the renderer to the arbiter's last resort.
		private static Control? ContentFocus(MainWindow window, MainWindowViewModel model)
		{
			if(!model.IsPlayWorkspace || !model.RecentGames.Visible) {
				return null;
			}
			if(model.RecentGames.ShowFirstRunHome && Named(window, "PlayHomeOpenRomPrimary") is Control firstRun) {
				return firstRun;
			}
			if(model.RecentGames.ShowRecentsHome && Named(window, "PlayHomeContinueButton") is Control recents) {
				return recents;
			}
			//Two StateGrids live in the home view - W-P2's row of tiles and, under
			//it, the classic grid the Save/Load screens and Advanced use - and only
			//one of them is on screen at a time: they are toggled by the two host
			//panels, not by the grid's own IsVisible, so the grid cannot ask for the
			//focus itself when the slot grid opens over a game. The one that is on
			//screen is the one to focus; a hidden control cannot take the focus at
			//all, which is how the slot grid used to open with nothing focused.
			return window.GetVisualDescendants().OfType<StateGrid>().FirstOrDefault(grid => grid.IsEffectivelyVisible);
		}

		//W-P5: the stored choice, else the first row.
		private static Control? PackPickerChoice(MainWindow window)
		{
			RadioButton[] choices = (Named(window, "PackPickerList") as ItemsControl)?.GetVisualDescendants().OfType<RadioButton>().ToArray() ?? Array.Empty<RadioButton>();
			return choices.FirstOrDefault(c => c.IsChecked == true) ?? choices.FirstOrDefault();
		}

		//The window's own name scope only sees MainWindow.axaml; the sheets are
		//UserControls with their own, so a surface's first control is found by
		//walking the visual tree (MainWindow.FindNamedDescendant's rule).
		private static Control? Named(MainWindow window, string name)
		{
			return window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
		}

		private static Control? EnabledNamed(MainWindow window, string name)
		{
			return window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyEnabled);
		}

		//The first control inside a named surface's own tree that can take the
		//focus now, in the surface's own order - the sheet's order of focus, the
		//same one a keyboard's Tab walks. Used where the surface's first control
		//is not one fixed name: the tool sheet's first control depends on the kind
		//it is showing, and picking a name that only exists in one kind is what
		//left the other kinds with no claim at all. A surface that is not up yet,
		//or is still laying out, answers nothing, which the arbiter reads as "wait
		//for it" rather than "focus what is underneath".
		private static Control? FirstFocusable(MainWindow window, string surface)
		{
			return Named(window, surface)?.GetVisualDescendants().OfType<Control>()
				.FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
		}

		//The pad, once per tick. It reads the host's pressed set, asks the rules
		//what the press means, and applies it to the focus - the same three steps
		//in the same order every tick, so there is no path where a press is
		//remembered but not acted on, or acted on twice.
		private sealed class Bridge
		{
			private readonly MainWindow _window;
			private readonly MainWindowViewModel _model;
			private readonly PadInHand _padInHand = new();
			private readonly PadNavRepeat _repeat = new();
			private readonly Stopwatch _clock = Stopwatch.StartNew();
			private HashSet<ushort> _previous = new();
			private TimeSpan _lastTick;

			public Bridge(MainWindow window, MainWindowViewModel model)
			{
				_window = window;
				_model = model;
			}

			public void Tick() => Tick(InputApi.GetPressedKeys(), null);

			//The tick itself, with the host's inputs passed in: what is pressed
			//now, how long since the last tick, and the backend's two lookups. All
			//of them are the platform's - the core's key state, the dispatcher's
			//clock, the key manager's name table - and the no-argument Tick above
			//passes the real ones. TickForTest is the only other caller.
			public void Tick(IReadOnlyCollection<ushort> pressed, TimeSpan? delta, Func<ushort, string>? keyName = null, Func<string, ushort>? keyCode = null)
			{
				keyName ??= InputApi.GetKeyName;
				keyCode ??= InputApi.GetKeyCode;

				//Which pad is in the player's hand, off the last new press. Asked
				//every tick, authority or not: the pad in hand is also what names
				//the control in ADR-0256 Decision 6's on-screen text, and a pad
				//pressed while a game runs is still the pad in hand.
				_padInHand.OnPressed(pressed, key => PadNaming.Of(key, keyName));

				//Null is a real answer (a family this backend cannot name, no pad
				//in hand yet): Next and PadNavRepeat both take it as "the pad asks
				//nothing", which is exactly what an unresolved mapping means.
				PadId? pad = _padInHand.Current;
				PadNavMapping? mapping = PadNavControls.Resolve(pad?.Family, pad?.Device ?? -1, keyCode);

				TimeSpan step;
				if(delta is TimeSpan injected) {
					//A test's clock. _lastTick advances anyway, so the real timer
					//resumes with an honest delta if the test ever hands the
					//window back.
					step = injected;
					_lastTick = _clock.Elapsed;
				} else {
					TimeSpan now = _clock.Elapsed;
					step = now - _lastTick;
					_lastTick = now;
				}

				PadNavAction action = _repeat.Next(pressed, _previous, mapping, HasAuthority(), step);
				//Recorded on EVERY tick, authority or not: a button held across
				//the moment the overlay opens would otherwise look like a new
				//press and step the menu the instant it appeared.
				_previous = new HashSet<ushort>(pressed);

				if(action != PadNavAction.None) {
					Apply(action);
				}
			}

			//ADR-0256 Decision 2, through the rule: the pad drives the GUI while a
			//Play surface is up, or with no game loaded. `IsPlaySurfaceOverGame`
			//is that question already answered once in this app (it is what makes
			//the native picture step aside for a surface), read here rather than
			//re-derived - and the pause state is never re-tested, because the rule
			//owns it and a second guard could only disagree with the first.
			//
			//The Player-mode/Play-workspace gate is ShortcutHandler's own
			//(ToggleOverlay's): the pad drives the *Play* GUI, which is the door an
			//arcade cabinet boots into, not the classic menus.
			private bool HasAuthority()
			{
				return _model.IsPlayerMode && _model.IsPlayWorkspace
					&& PlayPadNavigation.HasAuthority(_model.IsPlaySurfaceOverGame, EmuApi.IsRunning());
			}

			//What a press does. Directions move the focus through the engine's own
			//traversal, so every surface's layout works without teaching it about
			//the pad; Confirm activates what the focus is on; Back is Esc.
			private void Apply(PadNavAction action)
			{
				if(action == PadNavAction.Back) {
					//Back is the same shortcut ADR-0251 gave the pad's chord, so a
					//pad walks ADR-0249's Esc order (sheet → W-P4 → resume) through
					//the one router that already implements it. On the home Esc does
					//nothing, and the pad's Back does nothing with it.
					EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = EmulatorShortcut.ToggleOverlay });
					return;
				}
				if(_window.FocusManager?.GetFocusedElement() is not Control focused) {
					return;
				}

				//StateGrid is scoped OUT of the bridge, deliberately - the choice
				//ADR-0256 Decision 3 left open ("either own the grid or exclude it").
				//It already moves its own SelectedIndex from the pad, in its own
				//50 ms timer, off player 1's port mappings (StateGrid.TimerInput_Tick),
				//and that same loop serves Advanced, where no Play surface and so no
				//mapping exists. Its slots are not individually focusable, so "owning"
				//it here would mean inventing the roving-focus container Decision 3
				//rules out.
				//
				//So while the grid holds the focus the D-pad and Confirm are the
				//grid's, and Back is still ours: Back is the grid's only way out from
				//a pad (the grid's own loop has no exit, and Esc - what Back already
				//is - is a route a cabinet has no keyboard for), and a player stuck
				//in the slot grid is the exact failure this ADR exists to prevent.
				//
				//The wart that leaves, stated rather than hidden: the pad's B is both
				//the console's B and Back, so on the grid it reads as "load this slot"
				//to the grid and "leave" to us. That ambiguity is a rebinding question
				//(ADR-0255's), not this slice's, and resolving it silently here would
				//be the second source of truth the ADR warns against.
				if(action != PadNavAction.Back && IsGrid(focused)) {
					return;
				}

				if(action == PadNavAction.Confirm) {
					Activate(focused);
					return;
				}

				//The engine's own directional search, then this app's one focus
				//entry point - rather than FocusManager.TryMoveFocus, whose focus
				//is NavigationMethod.Unspecified and so does NOT set
				//:focus-visible, which is what paints the ring (Decision 3: an
				//arcade cabinet has no cursor to fall back on). The traversal
				//itself is still entirely the engine's.
				if(TopLevel.GetTopLevel(focused)?.FocusManager is IFocusManager manager
					&& manager.FindNextElement(Direction(action), new FindNextElementOptions { FocusedElement = focused }) is Control next) {
					PlayFocusOnOpen.Enter(next);
				}
			}

			//The grid itself, or a control inside one (nothing puts one there today,
			//but the walk is what makes the exclusion hold if a sheet ever does).
			private static bool IsGrid(Control focused)
			{
				return focused is StateGrid || focused.GetVisualAncestors().OfType<StateGrid>().Any();
			}

			private static NavigationDirection Direction(PadNavAction action)
			{
				return action switch {
					PadNavAction.Up => NavigationDirection.Up,
					PadNavAction.Down => NavigationDirection.Down,
					PadNavAction.Left => NavigationDirection.Left,
					_ => NavigationDirection.Right
				};
			}

			//Confirm is a press on the focused control, and the focus ring is the
			//cursor: whatever it is on is what A acts on. Each case is that
			//control's own activation, in the order Avalonia's own OnClick uses
			//(set the state, then raise) - the raise is what surface handlers
			//listen to, and the state is what a surface binds to, so a surface
			//that uses either one works.
			//
			//A TextBox falls through to the raise, which its surface may ignore:
			//a pad cannot type, and the Play sheets that lead with a search box
			//are the ones an arcade cabinet has no keyboard for. That is a real
			//limit, not a TODO silently swallowed here.
			private static void Activate(Control focused)
			{
				switch(focused) {
					case RadioButton radio:
						//RadioButton.OnClick: choose it (idempotent - a radio cannot
						//be unchecked by pressing it), then raise.
						radio.IsChecked = true;
						radio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
						break;
					case ToggleButton toggle:
						//ToggleButton.OnClick (CheckBox included): flip, then raise.
						toggle.IsChecked = !(toggle.IsChecked ?? false);
						toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
						break;
					case TabItem tab:
						tab.IsSelected = true;
						break;
					default:
						focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
						break;
				}
			}
		}
	}
}
