using Avalonia;
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
			//The timer goes with the window. Nothing here outlives it, and a tick
			//that landed after its window closed would run this bridge against a
			//closed window on whatever dispatcher happened to be current - the
			//shape #840 reports, where a leaked window's 50 ms tick broke the next
			//test's Avalonia session setup. Stopping it here rather than in a
			//caller keeps the two together wherever Attach is used, and stops the
			//window the player closes from leaving a timer behind too.
			window.Closed += (_, _) => timer.Stop();
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

		//#994 review 3: where the keyboard panel is drawn; a headless case swaps
		//it to stand in for a field with no overlay layer. Null puts it back.
		private static Func<Visual, OverlayLayer?> _overlayOf = OverlayLayer.GetOverlayLayer;

		public static void SetOverlayLookupForTest(Func<Visual, OverlayLayer?>? lookup)
		{
			_overlayOf = lookup ?? OverlayLayer.GetOverlayLayer;
		}

		//ADR-0262: the keyboard the pad has open, or null - so a headless case can
		//walk its keys the way a player does instead of guessing the layout.
		public static PadKeyboard? KeyboardForTest(MainWindow window)
		{
			return Installed.TryGetValue(window, out Bridge? bridge) ? bridge.Keyboard : null;
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
			//ADR-0256 Decision 8: the Settings sheet's System tab (the first
			//run's storage and keyboard-preset choices) is a surface of its own,
			//so it is claimed before the sheet that holds it: with the tab
			//showing, the pad lands on the storage choice; on any other tab this
			//claim is closed and the one below puts it on the strip, as before.
			//#932: and it names the sheet as its root, as the Settings claim does
			//(#910). Inferred from the storage choice, the root was the tab's page,
			//which holds neither the strip nor the footer (Done), so the D-pad
			//could not leave the four choices.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerSystemTabVisible)],
				() => model.IsPlayerSystemTabVisible, () => Named(window, "SystemStorageUserFolder"),
				() => Named(window, "PlayerSettingsSheet"));
			//#910: the sheet names its own root. Inferred from the strip's tab,
			//the root was the TabControl, which holds neither the page's rows
			//nor the footer (Exit full screen, Done), so the D-pad could not
			//leave the strip.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerSettingsVisible)],
				() => model.IsPlayerSettingsVisible, () => Named(window, "tabPlayerWindow"),
				() => Named(window, "PlayerSettingsSheet"));
			//ADR-0255's Controller sheet, which CurrentPlaySheet() reads right
			//after Settings (one of the two is current at a time; the sheet
			//replaces the Settings sheet's Controls landing), so the arbiter's
			//order keeps mirroring the chain PlayEsc.Next walks. Done is the
			//sheet's own control, and the one to open it on: it is always on
			//screen and focusable whatever the sheet is showing - the pad picker,
			//the PLAYERS rows and the keyboard block's restore button each come
			//and go with the connected pad and the preset, and Done is declared
			//outside every one of those conditions - and a
			//Confirm on it closes the sheet back to W-P4 - where the other one,
			//More in Options…, leaves for the classic Input window, which
			//ADR-0256's non-goals say a pad cannot drive.
			focus.When(model.ControllerSheet, [nameof(ControllerSheetViewModel.IsVisible)],
				() => model.ControllerSheet.IsVisible, () => Named(window, "ControllerSheetDone"));
			focus.When(model.PackDepSheet, [nameof(PlayPackDepSheetViewModel.IsVisible)],
				() => model.PackDepSheet.IsVisible, () => Named(window, "PackDepSheetChooseFile"));
			//#848: and it names its own search root for the same reason #845's
			//picker does - its first control is a row of its own list, so the
			//inference in SearchRoot would answer with that row's item container
			//and the D-pad could not leave the first choice.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerPackPickerVisible)],
				() => model.IsPlayerPackPickerVisible, () => PackPickerChoice(window),
				() => Named(window, "PlayerPackPicker"));
			focus.When(model, [nameof(MainWindowViewModel.IsEnhancementsPanelVisible)],
				() => model.IsEnhancementsPanelVisible, () => Named(window, "EnhancementsModernCheckBox"));
			focus.When(model, [nameof(MainWindowViewModel.IsPackDetailVisible)],
				() => model.IsPackDetailVisible,
				() => Named(window, PackDetailPendingFile.FirstControl(model.PackDepSheet.HasPending, model.PackDetailCanChange)));
			focus.When(model.CheatsSheet, [nameof(PlayerCheatsSheetViewModel.IsVisible)],
				() => model.CheatsSheet.IsVisible,
				() => Named(window, model.CheatsSheet.IsSearchEnabled ? "CheatsSearchBox" : "CheatsDoneButton"));
			focus.When(model.ReplaysSheet, [nameof(PlayerReplaysSheetViewModel.IsVisible)],
				() => model.ReplaysSheet.IsVisible,
				() => EnabledNamed(window, "ReplaysWatchButton") ?? Named(window, "ReplaysDoneButton"));
			//#909: the Save states sheet is a grid of rows (#848's reason applies
			//here too: its first control is a row of its own list), so it names its
			//own search root and its target is the row's own *Save here* - the slot
			//the sheet opens on, which the rule answers (newest state, else the
			//first slot).
			focus.When(model, [nameof(MainWindowViewModel.IsSaveStatesSheetVisible)],
				() => model.IsSaveStatesSheetVisible, () => SaveStatesFocusTarget(window, model),
				() => Named(window, "PlayerSaveStatesSheet"));
			//W-P4 itself, under every sheet opened from it and over the game.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerOverlayVisible)],
				() => model.IsPlayerOverlayVisible, () => Named(window, "OverlayResumeButton"));
			//#845 (ADR-0256 Decision 9): the ROM picker, over the home. It is the
			//one Play surface over the content area rather than over W-P4, so it is
			//claimed last of the surfaces, before the content area it covers.
			//
			//PathText is watched beside IsVisible on purpose: a step inside the
			//picker rebuilds its list (the rows of the folder just chosen), and the
			//row that held the focus is gone with it - so the step is also what
			//re-arbitrates, and the ring lands on the new first row. Without it the
			//sheet would answer the first Step and no other. Both reads are the
			//view-model's own state, never a second copy of it.
			//
			//It is the one claim that also names its own search root, and it has to:
			//its first control is a *row*, alone inside its ContentPresenter, and the
			//arbiter's inference (the nearest ancestor the target and the focused
			//control share) then answers with that one row - so the ring could never
			//leave it, and only the first root or the alphabetically first entry of a
			//folder was reachable with a pad (found by the second review of #845,
			//2026-10-05; the case below presses Down).
			//
			//SuggestionRevision is watched beside those two because the scan's
			//deep pass REPLACES the rows the shallow one published (ADR-0256
			//Decision 9 amendment: a shallow answer now, the measured one when it
			//lands). A replaced row takes its container - and the ring on it - with
			//it, and without this the pad would be left with nothing focused to
			//press Confirm on.
			//
			//#1032 (ADR-0264): the sheet has TWO surfaces now - the library and,
			//inside it, the folder browser *Browse a file…* opens - so the claim
			//watches the mode and the grid's own revision beside the browser's,
			//and the target is whichever surface's first control the mode names.
			//Without the mode in the list, the press that steps into the browser
			//would leave the ring on a tile the player can no longer see.
			focus.When(model.RomPicker,
				[nameof(PlayerRomPickerViewModel.IsVisible), nameof(PlayerRomPickerViewModel.PathText),
				 nameof(PlayerRomPickerViewModel.SuggestionRevision), nameof(PlayerRomPickerViewModel.Mode),
				 nameof(PlayerRomPickerViewModel.TilesRevision)],
				() => model.RomPicker.IsVisible, () => RomPickerFocusTarget(window, model),
				() => Named(window, "PlayerRomPickerSheet"));

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

		//#845: the picker's first row, whatever it is now. Not FirstFocusable over
		//the sheet: the sheet's own Back button is declared before the list (it is
		//docked to the bottom, which does not move it in the tree), so "the first
		//focusable control" is the way out rather than the way in. Fall back to
		//that button only when the list has no rows at all.
		//
		//#845 amendment: the action row now LEADS a folder's list, and the ring
		//must never land on it on a descend - otherwise a stray Confirm would
		//silently repoint the games folder. So the first non-Action row wins; a
		//folder with no content rows at all answers null, which the caller turns
		//into the Back button rather than the action row.
		//#1032 (ADR-0264): which surface's first control the ring lands on. The
		//library's way in is its first TILE (Decision 3: A plays the focused
		//game, so the sheet must open with a game focused); the browser's is its
		//first row, as it always was. Both fall back to Back, so a state with
		//nothing to pick still has something to press - the ring is never left
		//with nothing at all.
		//
		//#1060: a library with no tile at all - no library folder yet, or folders
		//the scan answered nothing for - lands on *Browse a file…* instead, because
		//that is the control the empty sentence names as the next step, and Back
		//leaves the sheet instead of taking it. Back stays the last resort: it is
		//the one control the sheet always has.
		private static Control? RomPickerFocusTarget(MainWindow window, MainWindowViewModel model)
		{
			if(model.RomPicker.Mode == RomPickerMode.Library) {
				return RomPickerFirstTile(window) ?? Named(window, "RomPickerBrowseFile") ?? Named(window, "RomPickerBack");
			}
			return RomPickerFirstRow(window) ?? Named(window, "RomPickerBack");
		}

		//The grid's first tile. The items are found by their own data context -
		//the same way the rows are - so a rebuild that reorders the grid moves
		//the ring to whatever leads it now.
		private static Control? RomPickerFirstTile(MainWindow window)
		{
			IEnumerable<Button> tiles = (Named(window, "RomPickerGrid") as ItemsControl)?.GetVisualDescendants().OfType<Button>()
				?? Enumerable.Empty<Button>();
			return tiles.FirstOrDefault(b => b.DataContext is PlayerLibraryTile);
		}

		private static Control? RomPickerFirstRow(MainWindow window)
		{
			IEnumerable<Button> rows = (Named(window, "RomPickerList") as ItemsControl)?.GetVisualDescendants().OfType<Button>()
				?? Enumerable.Empty<Button>();
			return rows.FirstOrDefault(b => b.DataContext is not PlayerRomPickerRow row || row.Kind != RomPickerRowKind.Action);
		}

		//#1032 (ADR-0264 Decision 3, as AMENDED on #1040): the library grid is not
		//a trap. Up from the grid's TOP row steps into the sheet's header
		//controls, and Down steps back to the tile it left. Those header controls
		//are otherwise unreachable from a pad - the grid's own XY navigation holds
		//the ring inside itself - and ADR-0256's rule that the whole Play GUI
		//works from a controller alone is not a clause ADR-0264 supersedes: a
		//pad-only player still has to reach *Browse a file…*, and therefore *Make
		//this my games folder* inside it.
		//
		//The search field and *Library folders…* the amendment also names are
		//later slices (#1034, #1035); this answers for the header the sheet has
		//today and keeps answering as they arrive, because it asks the sheet for
		//its controls by name rather than counting them.
		//
		//Nothing outside this sheet is touched in either direction: every other
		//surface keeps the engine's own traversal, and this closes only the one
		//case the engine cannot - a grid whose XY scope has nothing above it.
		//Review finding 4 on #1032: the library grid stays in the tree while the
		//browser is up - the sheet hides it with IsLibraryMode, it does not remove
		//it - so a step that only asked whether the grid EXISTS answered with a
		//tile the player cannot see, and Enter cannot focus what is hidden: the
		//press was spent, the ring stayed on Back and the pad's Down did nothing
		//on that surface. The step reads the surface that is UP, and answers for
		//that one: the grid's tile on the library, the list's first row in the
		//browser. Never a control the player cannot see.
		private static Control? RomPickerHeaderStep(MainWindow window, MainWindowViewModel model, Control focused, Control? lastTile, PadNavAction action)
		{
			if(Named(window, "RomPickerGrid") is not ItemsControl grid) {
				return null;
			}
			bool libraryIsUp = model.RomPicker.IsVisible && model.RomPicker.Mode == RomPickerMode.Library && grid.IsEffectivelyVisible;
			if(action == PadNavAction.Up && focused.DataContext is PlayerLibraryTile && IsInFirstGridRow(grid, focused)) {
				return Named(window, "RomPickerBrowseFile") ?? Named(window, "RomPickerBack");
			}
			//#1033: the search box is a header control too, so Down out of it comes
			//back to the grid exactly as Down out of the buttons does - one step
			//out, one step back, whichever control the ring was on.
			if(action == PadNavAction.Down && focused.Name is "RomPickerBrowseFile" or "RomPickerBack" or "RomPickerSearch") {
				//#1050 review finding 4: the grid is only a place to come back to
				//while it is the surface that is UP. In the browser the sheet hides
				//the grid rather than removing it, and Enter cannot focus what is
				//hidden - so Down on Back there lands on the browser's own first row.
				if(!libraryIsUp) {
					return RomPickerFirstRow(window) ?? Named(window, "RomPickerBack");
				}
				//The tile the player left, not the first one: Down undoes Up - and
				//only while that tile is one the grid still draws (a rebuild
				//replaced its container, and the old one is attached no longer).
				return (lastTile is { IsEffectivelyVisible: true } ? lastTile : null)
					?? RomPickerFirstTile(window) ?? Named(window, "RomPickerBack");
			}
			return null;
		}

		//The grid's first visual row. The WrapPanel owns the layout, so the row is
		//read off the positions rather than counted: the tiles that share the
		//smallest Y are the ones with nothing above them, whatever the tile width
		//or the sheet's width happens to be.
		private static bool IsInFirstGridRow(ItemsControl grid, Control focused)
		{
			double top = double.MaxValue;
			double? mine = null;
			foreach(Button tile in grid.GetVisualDescendants().OfType<Button>()) {
				if(tile.DataContext is not PlayerLibraryTile || tile.TranslatePoint(new Point(0, 0), grid) is not Point point) {
					continue;
				}
				top = Math.Min(top, point.Y);
				if(ReferenceEquals(tile, focused)) {
					mine = point.Y;
				}
			}
			return mine is not null && mine.Value <= top + 1;
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
		//#909: the row W-P4's Save states grid opens on (SaveStateSheet.FocusSlot
		//answers which), and its own *Save here* - the first control of the row, so
		//the Load beside it is one Right away. A row whose *Save here* does not
		//exist - the auto-save, which offers Load alone - hands over that button.
		//The list answers in its own order, so the row is found by its index.
		private static Control? SaveStatesFocusTarget(MainWindow window, MainWindowViewModel model)
		{
			SaveStateSlotViewModel? focus = model.FocusSaveStateSlot();
			if(focus != null && Named(window, "SaveStatesGrid") is ItemsControl grid
				&& grid.ContainerFromIndex(model.SaveStateSlots.IndexOf(focus)) is Control container) {
				return container.GetVisualDescendants().OfType<Button>()
					.FirstOrDefault(b => b.Name == "SlotSaveButton" && b.IsEffectivelyVisible)
					?? container.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "SlotLoadButton");
			}
			//No rows (the sheet is not over a game, which the app never does): the
			//sheet's own first control, the way every other surface answers.
			return FirstFocusable(window, "PlayerSaveStatesSheet");
		}

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
			//#964: the drop-down the pad opened and the row it is on (committed
			//only by Confirm), and the hold button Confirm is holding down.
			private ComboBox? _openPopup;
			//#1032 (ADR-0264 Decision 3, as amended): the library tile the ring
			//last sat on, so Down out of the header comes back to it.
			private Control? _libraryTile;
			private int _walk = -1;
			private Button? _holding;
			//ADR-0262: the one on-screen keyboard, the field it fills and the
			//panel it is drawn in (the window's overlay layer, below the field).
			private PadKeyboard? _keyboard;
			private TextBox? _keyboardField;
			private Border? _keyboardPanel;

			public PadKeyboard? Keyboard => _keyboard;

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

				bool authority = HasAuthority();
				PadNavAction action = _repeat.Next(pressed, _previous, mapping, authority, step);

				//Defect 2: Back is the slot grid's only way out from a pad, and it
				//must not be gated by authority. A grid opened by the Load/Save-state
				//shortcuts sits over a game CurrentPlaySheet() does not name, so
				//authority is false and _repeat.Next answers None - which left the
				//player stuck on the grid. The grid asks for Back directly when a
				//closable grid (not the Play home's tiles, which have no close)
				//holds the focus; Apply then closes it through the grid's own path.
				//
				//Scoped to the Play door, like the authority it sidesteps: the
				//classic StateGrid is also Advanced's game-selection and Save/Load
				//screen, and the bridge ticks in every window, so without this gate
				//a pad Back would close an Advanced screen the ADR never gave it
				//(ADR-0256 is the Play GUI's; Advanced keeps its own behavior).
				if(action == PadNavAction.None && InPlayDoor && CloseableGridHasFocus()
					&& PlayPadNavigation.IsBackEdge(pressed, _previous, mapping)) {
					action = PadNavAction.Back;
				}

				//#1033 (ADR-0264 Decision 3): the library sheet's own control, Y.
				//It opens search, which means it puts the ring on the sheet's search
				//box - the one control the sheet has that a pad would otherwise
				//reach only by walking the header - and Confirm on that box then
				//opens the shared on-screen keyboard ADR-0262 owns, so the query is
				//typed with the pad. It is read off the pressed sets rather than off
				//Next's answer because Y is not one of the six the pad navigates
				//with (PadNavControls.SheetControls, and the reason it is a second
				//table is there). Gated on this sheet so every other Play surface
				//keeps the button the player may have bound to a console's own.
				if(authority && InPlayDoor && LibrarySheetIsUp
					&& PlayPadNavigation.IsSheetEdge(PadNavControls.SheetCode(pad?.Family, pad?.Device ?? -1, PadSheetControl.Search, keyCode), pressed, _previous)) {
					FocusLibrarySearch();
				}

				//#964: a hold ends on Confirm's release, which is not an action the
				//edge rule produces - so it is read off the pressed set here, every
				//tick and authority or not, or compare would outlive the press.
				if(PlayPadValueRules.EndsHold(_holding is not null, pressed, mapping)) {
					SetHold(_holding!, false);
					_holding = null;
				}

				//Recorded on EVERY tick, authority or not: a button held across
				//the moment the overlay opens would otherwise look like a new
				//press and step the menu the instant it appeared.
				_previous = new HashSet<ushort>(pressed);

				//ADR-0262 Decision 4: a field that went away under its keyboard
				//(the sheet closed by something else) or the focus leaving it (a
				//mouse click) closes the keyboard as a cancel; the pad losing
				//authority closes it keeping the draft. Either way the pad never
				//comes back editing a field it no longer holds, and the focus is
				//left where it went.
				if(_keyboardField is TextBox keyboardField && _keyboard is not null) {
					PadKeyboardLeave? leave = !keyboardField.IsEffectivelyVisible ? PadKeyboardLeave.FieldGone
						: !authority ? PadKeyboardLeave.AuthorityLost
						: !ReferenceEquals(_window.FocusManager?.GetFocusedElement(), keyboardField) ? PadKeyboardLeave.FocusMoved
						: null;
					if(leave is PadKeyboardLeave why) {
						keyboardField.Text = _keyboard.TextOnLeave(why);
						CloseKeyboard(cancel: false, refocus: false);
					}
				}

				if(action != PadNavAction.None) {
					Apply(action);
				}
			}

			//ADR-0256 Decisions 1 and 2, through the rule. `IsPlaySurfaceOverGame`
			//answers "is something drawn over the game", which is a weaker question
			//than "did that something take the console away from the pad": it counts
			//the barcode tool sheet, Settings reached from a task door, the archive's
			//ROM list and the load card - none of which pause. So the rule is handed
			//the pause state beside it (a surface qualifies only when the game is
			//paused under it), plus the two non-pausing surfaces that ARE the pad's
			//and are named rather than folded in: the load card, which is refused
			//because it has no focusable control of its own, and the on-load pack
			//picker, which is granted because it has to be answered before play.
			//
			//The Player-mode/Play-workspace gate is ShortcutHandler's own
			//(ToggleOverlay's): the pad drives the *Play* GUI, which is the door an
			//arcade cabinet boots into, not the classic menus. Named InPlayDoor so
			//the authority path and the grid's Back edge ask the same door.
			private bool HasAuthority()
			{
				//ADR-0255 slice 3 adds one clause, and it is the same predicate:
				//while the Controller sheet is capturing "press a control", the pad
				//is the capture's, so authority is refused and the capture consumes
				//the press. Not a second rule - the capture is a state of "the pad
				//is not the GUI's", which is exactly what this predicate answers,
				//and the capture reads the same pressed set this bridge does.
				return InPlayDoor && !_model.IsControllerCapturing
					&& PlayPadNavigation.HasAuthority(_model.IsPlaySurfaceOverGame, EmuApi.IsRunning(), EmuApi.IsPaused(), _model.IsLoadCardVisible, _model.IsOnLoadPackPickerVisible);
			}

			//The door the bridge is for: Player UI mode in a game-screen workspace
			//(the switcher's Play door, or Classic under the same UI mode). The rule
			//is PlayPadNavigation's, not a private one here, because the slot grid's
			//own pad branch asks the same door (StateGrid.TimerInput_Tick) and the
			//two must never answer differently.
			private bool InPlayDoor => PlayPadNavigation.InPlayDoor(_model.IsPlayerMode, _model.IsPlayWorkspace);

		//#1033 (ADR-0264 Decision 3): the sheet that owns the pad's Y. Asked of the
		//view-model's own surface state - the same expressions the sheet renders
		//from - so the button means search exactly while the library is what the
		//player is looking at, and the folder browser inside it keeps the button
		//the player may have bound to a console's own.
		private bool LibrarySheetIsUp => _model.RomPicker.IsVisible && _model.RomPicker.Mode == RomPickerMode.Library;

		//Y's whole effect: the ring goes to the search box, and the shared
		//on-screen keyboard ADR-0262 owns opens with it - one press, and the player
		//is typing, which is what "Y opens search" has to mean on a cabinet with no
		//keyboard behind the pad. PlayFocusOnOpen.Enter is the one focus entry point
		//(ADR-0256 Decision 3), so the ring is drawn; a keyboard already open is
		//left alone rather than drawn twice, and Confirm over the box still opens
		//it for a player who reached the field by walking the header.
		private void FocusLibrarySearch()
		{
			if(Named(_window, "RomPickerSearch") is TextBox field && PlayFocusOnOpen.Enter(field) && _keyboard is null) {
				OpenKeyboard(field);
			}
		}

			//A slot grid the pad can leave: the classic grid the Save/Load screens
			//and Advanced use, which draws a close box. The Play home's row of tiles
			//is a StateGrid too (ShowClose false) and has nothing to leave, so Back
			//on it stays what it was with Esc - a no-op on the home.
			private bool CloseableGridHasFocus()
			{
				return _window.FocusManager?.GetFocusedElement() is Control focused
					&& GridOf(focused) is StateGrid grid && grid.CanCloseFromPad;
			}

			//What a press does. Directions move the focus through the engine's own
			//traversal, so every surface's layout works without teaching it about
			//the pad; Confirm activates what the focus is on; Back is Esc.
			private void Apply(PadNavAction action)
			{
				if(ApplyKeyboard(action)) {
					return;
				}
				if(ApplyValue(action)) {
					return;
				}
				if(action == PadNavAction.Back) {
					//The grid's Back closes the grid through the grid's own path,
					//never the Esc router: for a grid opened from W-P4 the two agree
					//(both come back to the overlay), but for the Load/Save-state
					//shortcuts the router would open W-P4 over a grid that is not its
					//own, while the grid's close hides the grid and resumes the game.
					//A grid with no close (the Play home's tiles) has nothing to
					//leave: Back does nothing there, as Esc does.
					if(_window.FocusManager?.GetFocusedElement() is Control gridFocus && GridOf(gridFocus) is StateGrid grid) {
						if(grid.CanCloseFromPad) {
							grid.CloseFromPad();
						}
						return;
					}
					//Not a grid: Back is the same shortcut ADR-0251 gave the pad's
					//chord, so a pad walks ADR-0249's Esc order (sheet → W-P4 →
					//resume) through the one router that already implements it. On the
					//home Esc does nothing, and the pad's Back does nothing with it.
					EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = EmulatorShortcut.ToggleOverlay });
					return;
				}
				if(_window.FocusManager?.GetFocusedElement() is not Control focused) {
					return;
				}
				if(focused.DataContext is PlayerLibraryTile) {
					_libraryTile = focused;
				}
				if(RomPickerHeaderStep(_window, _model, focused, _libraryTile, action) is Control header) {
					PlayFocusOnOpen.Enter(header);
					return;
				}

				//StateGrid is scoped OUT of the bridge, deliberately - the choice
				//ADR-0256 Decision 3 left open ("either own the grid or exclude it").
				//It already moves its own SelectedIndex from the pad, in its own
				//50 ms timer, and that same loop serves Advanced, where no Play
				//surface and so no mapping exists. Its slots are not individually
				//focusable, so "owning" it here would mean inventing the roving-focus
				//container Decision 3 rules out. Since defect 3 its pad codes come
				//from the pad's own preset (PlayPadNavigation.GridAction, resolved
				//for the device the code came from), not the rebindable console port
				//mapping - so the pad's own directions move it and its own Confirm
				//loads, and a second pad drives it too.
				//
				//So while the grid holds the focus the D-pad and Confirm are the
				//grid's, and Back is still ours: Back is the grid's only way out from
				//a pad (the grid's own loop has no exit, and Esc - what Back already
				//is - is a route a cabinet has no keyboard for), and a player stuck
				//in the slot grid is the exact failure this ADR exists to prevent.
				//The grid asks for Back in Tick even without authority, and Apply
				//closes the grid through the grid's own path, never the Esc router.
				//
				//The B ambiguity, written down and resolved rather than hidden: on
				//the pad's own preset Back is the pad's B, and the console mapping
				//puts the console's A on that same button (KeyPresets maps the Xbox
				//B to the console's A). While the grid read its load off the console
				//mapping, the pad's B both loaded the slot (as console A) and left the
				//grid (as Back) - two readings of one press. Reading the grid's pad
				//codes off the preset instead puts the load on the pad's A (the
				//preset's Confirm) and leaves the pad's B to Back, so the two no
				//longer collide. The keyboard's console mapping is untouched:
				//Decision 4 is about the pad, and the keyboard player's own choice is
				//theirs.
				if(action != PadNavAction.Back && IsGrid(focused) && !GridYieldsUp(focused, action)) {
					return;
				}

				if(action == PadNavAction.Confirm && focused is TextBox field && field.IsEffectivelyEnabled && !field.IsReadOnly && OpenKeyboard(field)) {
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
				//
				//Decision 3 also means the search stays inside the surface that
				//holds the focus: the engine searches the whole window, and what
				//is under a sheet is on screen on purpose (W-P4's card behind its
				//sheets, the home behind a sheet opened from a task door), so
				//without a root a D-pad press walks off the sheet onto a surface
				//the player can see but is not using. The arbiter answers which
				//surface that is - the same one whose claim took the focus.
				if(TopLevel.GetTopLevel(focused)?.FocusManager is IFocusManager manager) {
					FindNextElementOptions options = new() { FocusedElement = focused, SearchRoot = PlayFocusOnOpen.Of(focused)?.SearchRoot() };
					if(manager.FindNextElement(Direction(action), options) is Control next) {
						PlayFocusOnOpen.Enter(next);
					}
				}
			}

			//#964: the focused control's own value semantics first (a slider's
			//step, a drop-down's open/walk/commit/cancel, Hold to Compare's hold),
			//as PlayPadValueRules answers them; false hands the press on to focus
			//movement and Activate, as before. An open drop-down is asked even when
			//the focus sits on one of its rows, because Avalonia focuses the rows
			//when the popup opens.
			private bool ApplyValue(PadNavAction action)
			{
				if(_openPopup is not null && !_openPopup.IsDropDownOpen) {
					//Closed by something else (a pointer, the sheet going away:
					//#983, a ComboBox closes itself once hidden or detached).
					_openPopup = null;
				}
				Control? target = _openPopup ?? _window.FocusManager?.GetFocusedElement() as Control;
				if(target is null) {
					return false;
				}
				PadValueAnswer answer = PlayPadValueRules.Next(KindOf(target), _openPopup is not null, action);
				switch(answer.Verb) {
					case PadValueVerb.None:
						return false;
					case PadValueVerb.Step when target is Slider slider:
						slider.Value = PlayPadValueRules.Step(slider.Value, slider.SmallChange, slider.Minimum, slider.Maximum, answer.Delta);
						break;
					case PadValueVerb.Open when target is ComboBox combo:
						_walk = combo.SelectedIndex;
						_openPopup = combo;
						combo.IsDropDownOpen = true;
						break;
					case PadValueVerb.Walk when target is ComboBox combo:
						//The row is only highlighted (focused, so the ring shows it);
						//the value is written by the commit alone, so Back can leave it.
						//#983: a virtualized list realizes only the rows in view, so
						//the next row is scrolled in first and the walk lands on it
						//only if it is then shown.
						int next = PlayPadValueRules.Walk(_walk, combo.ItemCount, answer.Delta);
						if(next >= 0) {
							combo.ScrollIntoView(next);
						}
						Control? row = next >= 0 ? combo.ContainerFromIndex(next) as Control : null;
						_walk = PlayPadValueRules.Land(_walk, next, row is not null && row.IsEffectivelyVisible);
						if(_walk == next) {
							row?.Focus(NavigationMethod.Directional);
						}
						break;
					case PadValueVerb.Commit when target is ComboBox combo:
						if(_walk >= 0) {
							combo.SelectedIndex = _walk;
						}
						ClosePopup(combo);
						break;
					case PadValueVerb.Cancel when target is ComboBox combo:
						ClosePopup(combo);
						break;
					case PadValueVerb.HoldStart when target is Button button:
						_holding = button;
						SetHold(button, true);
						break;
				}
				return true;
			}

			//What the focused control is to the value rule. Hold to Compare is the
			//one hold button in Play, and it is named here because nothing else
			//marks a hold: its view listens only to the pointer and Space.
			private static PadValueKind KindOf(Control focused)
			{
				return focused switch {
					Slider => PadValueKind.Slider,
					ComboBox => PadValueKind.Popup,
					Button { Name: "btnLookHoldToCompare", DataContext: LookConfigViewModel } => PadValueKind.Hold,
					_ => PadValueKind.None
				};
			}

			private void ClosePopup(ComboBox combo)
			{
				//The focus comes back BEFORE the popup closes: Avalonia's ComboBox
				//refocuses itself on close without a navigation method, and a focus
				//it already holds is not re-entered - so the ring would be lost.
				//Directly, not PlayFocusOnOpen.Enter: the focus is on a row of the
				//popup, which Enter's other-top-level guard would refuse.
				_openPopup = null;
				_walk = -1;
				combo.Focus(NavigationMethod.Directional);
				combo.IsDropDownOpen = false;
			}

			//ADR-0262: while the keyboard is open every press is the keyboard's -
			//the D-pad walks its keys, A presses one, B cancels - so the focus
			//cannot walk off the field it is filling and Back cannot close the
			//sheet under it. What a press means is PadKeyboard's; this writes the
			//draft into the field as it changes, so a search filters while typed.
			private bool ApplyKeyboard(PadNavAction action)
			{
				if(_keyboard is null || _keyboardField is null) {
					return false;
				}
				switch(_keyboard.Press(action)) {
					case PadKeyboardOutcome.Edited:
						_keyboardField.Text = _keyboard.Draft;
						_keyboardField.CaretIndex = _keyboard.Draft.Length;
						PaintKeyboard();
						break;
					case PadKeyboardOutcome.Moved:
						PaintKeyboard();
						break;
					case PadKeyboardOutcome.Committed:
						CloseKeyboard(cancel: false);
						break;
					case PadKeyboardOutcome.Cancelled:
						CloseKeyboard(cancel: true);
						break;
				}
				return true;
			}

			//The field declares its own shape (ADR-0262 Decision 2): its mask, or
			//the padCode style class a code-shaped box carries in its view.
			//#994 review 3: no overlay layer means nowhere to draw the keyboard,
			//and an invisible keyboard would swallow every press, Back included -
			//so it does not open, and the press falls through as before.
			private bool OpenKeyboard(TextBox field)
			{
				if(_overlayOf(field) is not OverlayLayer layer) {
					return false;
				}
				PadKeyboardShape shape = PadKeyboard.ShapeOf(field.PasswordChar != default(char), field.Classes.Contains(PadCodeClass));
				_keyboard = new PadKeyboard(shape, field.Text ?? "", field.MaxLength);
				_keyboardField = field;
				_keyboardPanel = PadKeyboardPanel.Build(_keyboard);
				layer.Children.Add(_keyboardPanel);
				PaintKeyboard();
				PadKeyboardPanel.Place(field, layer, _keyboardPanel);
				return true;
			}

			//Cancel gives the field back its original value. Closed by the pad
			//(OK, B), the focus comes back to the field, ring drawn; closed because
			//the focus or the pad went elsewhere, the focus is left where it is.
			private void CloseKeyboard(bool cancel, bool refocus = true)
			{
				TextBox? field = _keyboardField;
				if(cancel && field is not null && _keyboard is not null) {
					field.Text = _keyboard.Original;
				}
				if(_keyboardPanel?.Parent is OverlayLayer layer) {
					layer.Children.Remove(_keyboardPanel);
				}
				_keyboard = null;
				_keyboardField = null;
				_keyboardPanel = null;
				if(refocus && field is not null && field.IsEffectivelyVisible) {
					field.Focus(NavigationMethod.Directional);
				}
			}

			private const string PadCodeClass = "padCode";

			private void PaintKeyboard()
			{
				if(_keyboard is not null && _keyboardPanel is not null) {
					PadKeyboardPanel.Paint(_keyboardPanel, _keyboard);
				}
			}

			//The view model's own SetCompare, the call the view's pointer and Space
			//handlers make - not a synthetic Space, which MainWindow's tunnel key
			//handler would hand to the console as a key press.
			private static void SetHold(Button button, bool on)
			{
				(button.DataContext as LookConfigViewModel)?.SetCompare(on);
			}

			//The grid itself, or a control inside one (nothing puts one there today,
			//but the walk is what makes the exclusion hold if a sheet ever does).
			private static bool IsGrid(Control focused)
			{
				return GridOf(focused) is not null;
			}

			//The grid a focused control belongs to, or null - the same walk IsGrid
			//used, returned rather than only answered so Back can close it.
			private static StateGrid? GridOf(Control focused)
			{
				return focused as StateGrid ?? focused.GetVisualAncestors().OfType<StateGrid>().FirstOrDefault();
			}

			//#896: the one press a grid gives back to the bridge. A grid with a
			//single row has nothing above it - its own Up moves nothing, which is
			//StateGrid.MovesWithUpFromPad - so the bridge keeps Up there and walks
			//the focus out of the grid, instead of the grid holding it for good.
			//
			//The Play home's row of tiles (ADR-0249's W-P2) is where that mattered:
			//its directions are the grid's by the rule above, its Back has no close
			//box to leave by (CanCloseFromPad is false for it), and a cabinet has no
			//Tab - so one D-pad Down off the Continue card left the player unable to
			//reach the card, or anything on it, again. Every direction the grid does
			//move stays the grid's; this is one press, not a second focus model.
			private static bool GridYieldsUp(Control focused, PadNavAction action)
			{
				return action == PadNavAction.Up && GridOf(focused) is StateGrid grid && !grid.MovesWithUpFromPad;
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
			//A TextBox reaches here only when the on-screen keyboard could not
			//open (no overlay layer, #994 review 3): Apply opens it instead
			//(ADR-0262), because a pad cannot type and an arcade cabinet has no
			//keyboard.
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
