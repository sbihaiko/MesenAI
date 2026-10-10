using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
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
			window.Closed += (_, _) => {
				timer.Stop();
				bridge.RomPickerParked = null;
			};
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
		private static Func<ushort, string>? _keyNameForTest;
		private static Func<string, ushort>? _keyCodeForTest;

		//The same two backend lookups for the production timer's own tick, so a case
		//that drives the real timer end to end (the GUI test hook's e2e) reads the
		//pad names a headless build cannot answer. Null puts the backend's back.
		public static void SetKeyLookupsForTest(Func<ushort, string>? keyName, Func<string, ushort>? keyCode)
		{
			_keyNameForTest = keyName;
			_keyCodeForTest = keyCode;
		}

		public static void TickForTest(MainWindow window, IReadOnlyCollection<ushort> pressed, TimeSpan delta, Func<ushort, string>? keyName = null, Func<string, ushort>? keyCode = null)
		{
			if(Installed.TryGetValue(window, out Bridge? bridge)) {
				bridge.Tick(pressed, delta, keyName, keyCode);
			}
		}

		//#1232, and the same kind of seam as TickForTest: the arbiter's content area,
		//registered by a headless case that needs the launch window's own state -
		//the content area ON SCREEN with its first control not resolvable yet -
		//exactly rather than raced against. That state is real: the window's startup
		//task classifies the home on a background thread, and until it lands
		//`RecentGames.Visible` is the constructor's own Player-mode `true` while no
		//home screen is classified, so the content area resolves to no first control
		//at all. The wiring registers the same three things through its own call; a
		//case asks for one of the three to be unresolvable.
		public static void ContentForTest(MainWindow window, System.ComponentModel.INotifyPropertyChanged source, string[] properties, Func<Control?> target, Func<Control?>? root)
		{
			PlayFocusOnOpen.Of(window)?.ContentForTest(source, properties, target, root);
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

		//#1281 (text.type): the GUI test hook's door onto the same keyboard. It is
		//production's path, not a test seam - the hook ships in every build and is
		//inert without --test-hook - and it types through the keyboard the window
		//already shows rather than opening one: what a step types into is the field
		//the run put the ring on, exactly as a person would find it.
		public static string? TypeOnKeyboard(MainWindow window, string text)
		{
			return Installed.TryGetValue(window, out Bridge? bridge)
				? bridge.TypeOnKeyboard(text)
				: "the pad bridge is not attached to this window";
		}

		//The field the open keyboard types into, or null - #1062: the claim that
		//keeps the ring on the search box asks for a keyboard bound to THAT box.
		//#1064: this is the shipping reader, and it is private on purpose. The
		//claim in RomPickerFocusTarget is not test code, so it may not reach a
		//*ForTest door - a seam production leans on is load-bearing and can no
		//longer be moved by the refactor it exists to allow. The public
		//KeyboardFieldForTest below delegates here, which is what keeps the
		//headless suite's door open onto the same answer.
		private static TextBox? KeyboardField(MainWindow window)
		{
			return Installed.TryGetValue(window, out Bridge? bridge) ? bridge.KeyboardField : null;
		}

		//The headless suite's door onto the same answer. It adds nothing of its
		//own: whatever a case reads here is what the ring's claim read (#1064).
		public static TextBox? KeyboardFieldForTest(MainWindow window)
		{
			return KeyboardField(window);
		}

		//The header control the sheet parked this window's ring on while a restore
		//waits, or null - so a headless case can tell one window's parking from
		//another's.
		public static Control? RomPickerParkedForTest(MainWindow window)
		{
			return Installed.TryGetValue(window, out Bridge? bridge) ? bridge.RomPickerParked : null;
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
				() => model.QuitGameConfirm.IsVisible, () => Named(window, "QuitGameKeepButton"), actions: () => PlayBarDeclarations.Sheet);
			//ADR-0250's task doors' sheets (the archive's list, Look's Adjust…, a
			//door's tool): HandleInWindowSheetEsc's order, SelectRom → Shader → Tool.
			focus.When(model.SelectRomSheet, [nameof(PlaySelectRomSheetViewModel.IsVisible)],
				() => model.SelectRomSheet.IsVisible, () => Named(window, "SelectRomSheetSearch"), actions: () => PlayBarDeclarations.Sheet);
			focus.When(model, [nameof(MainWindowViewModel.IsShaderSheetVisible)],
				() => model.IsShaderSheetVisible, () => Named(window, "ShaderSheetOk"), actions: () => PlayBarDeclarations.Sheet);
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
				() => model.ToolSheet.IsBarcode ? Named(window, "ToolSheetBarcode") : FirstFocusable(window, "ToolSheet"),
				actions: () => PlayBarDeclarations.Sheet);
			//The edge-flow sheets, which HandleEdgeFlowEsc answers only after the
			//in-window ones: Bios, then ControllerSetup.
			focus.When(model.BiosSheet, [nameof(PlayBiosSheetViewModel.IsVisible)],
				() => model.BiosSheet.IsVisible, () => Named(window, "BiosSheetChooseFile"), actions: () => PlayBarDeclarations.Sheet);
			focus.When(model.ControllerSetup, [nameof(PlayControllerSetupViewModel.IsVisible)],
				() => model.ControllerSetup.IsVisible, () => Named(window, "ControllerSetupSkip"), actions: () => PlayBarDeclarations.Sheet);
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
				() => Named(window, "PlayerSettingsSheet"), () => PlayBarDeclarations.Sheet);
			//#910: the sheet names its own root. Inferred from the strip's tab,
			//the root was the TabControl, which holds neither the page's rows
			//nor the footer (Exit full screen, Done), so the D-pad could not
			//leave the strip.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerSettingsVisible)],
				() => model.IsPlayerSettingsVisible, () => Named(window, "tabPlayerWindow"),
				() => Named(window, "PlayerSettingsSheet"), () => PlayBarDeclarations.Sheet);
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
				() => model.ControllerSheet.IsVisible, () => Named(window, "ControllerSheetDone"), actions: () => PlayBarDeclarations.Sheet);
			focus.When(model.PackDepSheet, [nameof(PlayPackDepSheetViewModel.IsVisible)],
				() => model.PackDepSheet.IsVisible, () => Named(window, "PackDepSheetChooseFile"), actions: () => PlayBarDeclarations.Sheet);
			//#848: and it names its own search root for the same reason #845's
			//picker does - its first control is a row of its own list, so the
			//inference in SearchRoot would answer with that row's item container
			//and the D-pad could not leave the first choice.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerPackPickerVisible)],
				() => model.IsPlayerPackPickerVisible, () => PackPickerChoice(window),
				() => Named(window, "PlayerPackPicker"), () => PlayBarDeclarations.Sheet);
			focus.When(model, [nameof(MainWindowViewModel.IsEnhancementsPanelVisible)],
				() => model.IsEnhancementsPanelVisible, () => Named(window, "EnhancementsModernCheckBox"), actions: () => PlayBarDeclarations.Sheet);
			focus.When(model, [nameof(MainWindowViewModel.IsPackDetailVisible)],
				() => model.IsPackDetailVisible,
				() => Named(window, PackDetailPendingFile.FirstControl(model.PackDepSheet.HasPending, model.PackDetailCanChange)),
					actions: () => PlayBarDeclarations.Sheet);
			focus.When(model.CheatsSheet, [nameof(PlayerCheatsSheetViewModel.IsVisible)],
				() => model.CheatsSheet.IsVisible,
				() => Named(window, model.CheatsSheet.IsSearchEnabled ? "CheatsSearchBox" : "CheatsDoneButton"),
				actions: () => PlayBarDeclarations.Sheet);
			focus.When(model.ReplaysSheet, [nameof(PlayerReplaysSheetViewModel.IsVisible)],
				() => model.ReplaysSheet.IsVisible,
				() => EnabledNamed(window, "ReplaysWatchButton") ?? Named(window, "ReplaysDoneButton"),
				actions: () => PlayBarDeclarations.Sheet);
			//#909: the Save states sheet is a grid of rows (#848's reason applies
			//here too: its first control is a row of its own list), so it names its
			//own search root and its target is the row's own *Save here* - the slot
			//the sheet opens on, which the rule answers (newest state, else the
			//first slot).
			focus.When(model, [nameof(MainWindowViewModel.IsSaveStatesSheetVisible)],
				() => model.IsSaveStatesSheetVisible, () => SaveStatesFocusTarget(window, model),
				() => Named(window, "PlayerSaveStatesSheet"), () => PlayBarDeclarations.Sheet);
			//W-P4 itself, under every sheet opened from it and over the game.
			focus.When(model, [nameof(MainWindowViewModel.IsPlayerOverlayVisible)],
				() => model.IsPlayerOverlayVisible, () => Named(window, "OverlayResumeButton"),
				actions: () => PlayBarDeclarations.PauseOverlay);
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
			//#1036 (ADR-0264 Decision 8): *Library folders…* is a third surface of
			//the same sheet, and FoldersRevision is watched beside the other two
			//revisions for exactly their reason - its rows are rebuilt on an open,
			//an add and a remove, and the container the ring was on went with the old
			//ones. Without it a pad that removed a row would be left holding nothing.
			focus.When(model.RomPicker,
				[nameof(PlayerRomPickerViewModel.IsVisible), nameof(PlayerRomPickerViewModel.PathText),
				 nameof(PlayerRomPickerViewModel.SuggestionRevision), nameof(PlayerRomPickerViewModel.Mode),
				 nameof(PlayerRomPickerViewModel.TilesRevision), nameof(PlayerRomPickerViewModel.FoldersRevision)],
				() => model.RomPicker.IsVisible, () => RomPickerFocusTarget(window, model),
				() => Named(window, "PlayerRomPickerSheet"),
				() => model.RomPicker.IsLibrarySurfaceVisible ? LibraryDeclaration(window, model)
					: model.RomPicker.IsFoldersSheetVisible ? PlayBarDeclarations.LibraryFoldersSheet((TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control)?.Name)
					: PlayBarDeclarations.Browser);

			//The content area under all of them: the home's primary action, the
			//Continue button, the slot grid over a game. It is not a claim (it is
			//not in the Esc stack, and it is also what Advanced shows), so it is
			//what the arbiter finds when no surface is up.
			focus.Content(model.RecentGames,
				[nameof(RecentGamesViewModel.Visible), nameof(RecentGamesViewModel.Mode),
				 nameof(RecentGamesViewModel.ShowFirstRunHome), nameof(RecentGamesViewModel.ShowRecentsHome),
				 nameof(RecentGamesViewModel.ShowPlainGrid), nameof(RecentGamesViewModel.ShowHomeGrid)],
				() => ContentFocus(window, model),
				() => model.IsPlayWorkspace && model.RecentGames.Visible
					? model.RecentGames.ShowFirstRunHome ? PlayBarDeclarations.HomeFirstRun
					: model.RecentGames.ShowRecentsHome ? HomeDeclaration(window, model)
					: PlayBarDeclarations.None
					: PlayBarDeclarations.None,
				() => ContentRoot(window));
		}

		//#1137: what a D-pad press stays inside while the content area holds the
		//focus - the home host, so a press from the home can never land on the
		//header's Profile / Tools buttons, which are drawn over it and outside it.
		//Containing the walk is ADR-0256 Decision 3 read for the content area: the
		//surface holding the focus is the one the pad walks, and with no sheet up
		//that surface is the home. The header keeps the door it always had - mouse
		//and keyboard - so nothing becomes unreachable, only unpadded.
		//
		//The host is the whole content area (both home screens and the slot grid
		//over a game), which is why the root is read here rather than derived from
		//the focused control: one root answers for every screen the content area
		//shows. Null when the host is not on screen - a game running with nothing
		//up - and the arbiter's last resort then has the window, as before. It is
		//also what tells the arbiter the content area IS the screen when its first
		//control is not resolvable yet: a press there waits for the home instead of
		//putting the ring on the renderer panel under it (#1235, #1232).
		private static Control? ContentRoot(MainWindow window)
		{
			return Named(window, "PlayHomeHost") is Control host && host.IsEffectivelyVisible ? host : null;
		}

		//The library's A is the focused control's, as the home's is: a tile plays,
		//but the header actions open what they name.
		private static IReadOnlyList<PlayBarEntry> LibraryDeclaration(MainWindow window, MainWindowViewModel model)
		{
			Control? focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;
			//#1108 AC2: the console filter row is a place the ring can be, and it has
			//no Play to promise - the segment is the filter itself (see
			//PlayBarDeclarations.FilterRow). Picked by the focused control's data
			//context rather than by name, because a segment carries no name.
			if(focused?.DataContext is PlayerConsoleFilterOption) {
				return PlayBarDeclarations.FilterRow;
			}
			return PlayFavoriteCover.Declare(focused?.Name switch {
				"RomPickerLibraryFolders" => PlayBarDeclarations.LibraryFolders,
				"RomPickerBrowseFile" => PlayBarDeclarations.BrowseFile,
				"RomPickerSearch" => PlayBarDeclarations.SearchField,
				"RomPickerBack" => PlayBarDeclarations.BackButton,
				"RomPickerSearchClear" => PlayBarDeclarations.SearchClear,
				_ => PlayBarDeclarations.Library
			}, model, focused);
		}

		//The recents home's A is the focused control's, not the surface's: Continue
		//plays, but Open a game… (the secondary button) opens the sheet.
		//#1110: X names Favorite/Unfavorite only while a cover (Continue or a tile) has the focus.
		private static IReadOnlyList<PlayBarEntry> HomeDeclaration(MainWindow window, MainWindowViewModel model)
		{
			Control? focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;
			return focused is not null && focused == Named(window, "PlayHomeOpenRomSecondary")
				? PlayBarDeclarations.HomeFirstRun
				: PlayFavoriteCover.Declare(PlayBarDeclarations.Home, model, focused);
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
			//#1036 (ADR-0264 Decision 8): the folders sheet's own way in is its
			//*Add a folder…* - the sheet's primary action, and the one press that is
			//not the removal of a folder the player already has. The rows below it
			//are reached by moving up, which is what the engine's traversal is for.
			if(model.RomPicker.IsFoldersSheetVisible) {
				return Named(window, "RomPickerAddFolder") ?? Named(window, "RomPickerBack");
			}
			if(model.RomPicker.Mode == RomPickerMode.Library) {
				//#1033: a scan landing bumps TilesRevision, which is a claim for the
				//first tile - and the player who pressed Y (or is typing) before a
				//slow scan answered is in the box, with the pad keyboard possibly open
				//over it. The claim keeps the ring where it is rather than taking the
				//query away mid-word.
				Control? search = Named(window, "RomPickerSearch");
				if(search is not null && (search.IsFocused || ReferenceEquals(KeyboardField(window), search))) {
					return search;
				}
				//#1037: the end of a scan whose restore never landed, and the
				//remembered game landing, are the sheet's own claims, not a claim
				//over the ring - a player who walked it to Back or the search box
				//while the scan ran keeps it there.
				//The ring is the player's when it is on a header control other than
				//the one the sheet parked it on, whichever control that is.
				Control? focused = window.FocusManager?.GetFocusedElement() as Control;
				//#1108 AC2: the filter row's segment is the ring the PLAYER put
				//there - the shoulders land the ring on the segment they cycle to
				//(FocusLibraryConsoleSegment) - and that press is itself a
				//TilesRevision bump: the grid under the row is rebuilt by the same
				//press. Without this the bump's claim would take the ring straight
				//back off the row onto the rebuilt grid (the same note as the search
				//box's, above, for the same reason), which is the state AC2 exists to
				//end. A detached segment is NOT answered: a rescan that changes which
				//consoles exist rebuilds the row, and the ring then belongs on the
				//grid like any other ring a rebuild took the container out from under.
				//#1108 review finding 2: a restore in flight outranks the row. While the
				//scan is still bringing the game the player left on - pending, or the
				//moment it lands - the ring is the restore's to place, so this branch is
				//skipped and the park / land logic below answers instead: a player who
				//presses RB while the restore waits gets the cycle, and the remembered
				//game still lands on the ring rather than on the segment the press
				//selected.
				if(focused is Control { DataContext: PlayerConsoleFilterOption } segment
					&& segment.IsAttachedToVisualTree() && segment.IsEffectivelyVisible
					&& !model.RomPicker.IsRestorePending && !model.RomPicker.IsRestoreLanding) {
					return segment;
				}
				//The header control the sheet itself parked THIS window's ring on while
				//a restore waits - per window, so a second window's sheet never reads it.
				Installed.TryGetValue(window, out Bridge? bridge);
				Control? parkedBefore = bridge?.RomPickerParked;
				if((model.RomPicker.IsFinishFallback || model.RomPicker.IsRestoreLanding)
					&& focused is not null && focused.DataContext is not PlayerLibraryTile
					&& focused.Name?.StartsWith("RomPicker") == true && !ReferenceEquals(focused, parkedBefore)) {
					return focused;
				}
				//A restore still waiting on its game parks the ring on Back, never on
				//*Browse a file…*: pressing that one would leave the library the
				//player is waiting on.
				Control? parked = model.RomPicker.IsRestorePending ? Named(window, "RomPickerBack") : null;
				if(bridge is not null) {
					bridge.RomPickerParked = parked;
				}
				if(parked is not null && RomPickerTile(window, model.RomPicker.LastFocusedTilePath, true) is null) {
					return parked;
				}
				//#1037 picks the tile; the CALLER named the game, because the
				//path lives on the view-model and this walks the tree. The
				//fallback chain is #1060's: a grid with no tile at all - no
				//library folder yet, or folders the scan answered nothing for -
				//lands on the control its empty sentence names, and Back stays
				//the last resort, being the one control the sheet always has.
				return RomPickerTile(window, model.RomPicker.LastFocusedTilePath, model.RomPicker.IsRestorePending)
					?? Named(window, "RomPickerLibraryFolders") ?? Named(window, "RomPickerBrowseFile") ?? Named(window, "RomPickerBack");
			}
			return RomPickerFirstRow(window) ?? Named(window, "RomPickerBack");
		}

		//The tile the ring lands on. The items are found by their own data
		//context - the same way the rows are - so a rebuild that reorders the
		//grid moves the ring to whatever leads it now.
		//
		//#1037 (ADR-0264 Decision 1): the game the player was on leads, so the
		//sheet REOPENS on it rather than on whatever the scan happened to list
		//first. A path the grid no longer holds - the file was moved, the folder
		//left the library - falls back to the first tile, which is also where a
		//sheet that has never been opened lands; while the scan is still bringing
		//that path the ring waits on the sheet's Back instead (see below).
		//`restorePending` says the scan is still bringing the game the player left
		//on and the grid does not hold it yet. The first-tile fallback is what the
		//ring lands on in every other case - a sheet that has never been opened, a
		//file that was moved - but mid-restore it is exactly the wrong answer: the
		//tile that takes the ring reports "the player is on it" (Decision 1), and
		//that report would overwrite the path the scan is still looking for. The
		//sheet's Back is where the ring waits instead, which the caller's fallback
		//supplies, so the sheet is never left with nothing to press.
		private static Control? RomPickerTile(MainWindow window, string? path = null, bool restorePending = false)
		{
			IEnumerable<Button> tiles = (Named(window, "RomPickerGrid") as ItemsControl)?.GetVisualDescendants().OfType<Button>()
				?? Enumerable.Empty<Button>();
			Control? remembered = tiles.FirstOrDefault(b => b.DataContext is PlayerLibraryTile tile && tile.Path.Length > 0 && tile.Path == path);
			if(remembered is not null || restorePending) {
				return remembered;
			}
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
			//#1036 (ADR-0264 Decision 8) adds the third one the same way - the
			//folders button that stands beside *Browse a file…* - by name and not by
			//counting them, so every header control keeps answering as they arrive.
			if(action == PadNavAction.Down && focused.Name is "RomPickerBrowseFile" or "RomPickerBack" or "RomPickerSearch" or "RomPickerLibraryFolders") {
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
				//#1037: the fallback is the tile the sheet would reopen on, which
				//is the first tile when the player has focused nothing yet.
				return (lastTile is { IsEffectivelyVisible: true } ? lastTile : null)
					?? RomPickerTile(window) ?? Named(window, "RomPickerBack");
			}
			return null;
		}

		//#1108 AC2: the console filter row is a place the pad can be now - the
		//shoulders land the ring on the segment they cycle to - so the row owes the
		//same one-step-out answer every other control on the sheet has. Up leaves it
		//for the header exactly as Up out of the grid's top row does (so the header
		//stays one step from anywhere on the sheet), Down comes back to the grid the
		//row is filtering (the tile the player left, or the sheet's own reopen
		//target), and Left / Right are the filter's own two directions: the row is
		//ONE control (PRD §13.3 rule 2 counts a segmented row as one element), so
		//its neighbours are the filter's own states and not other controls - which
		//is also what keeps the row and the grid agreeing (ADR-0264 Decision 5)
		//while the ring is on it.
		private static Control? RomPickerFilterRowStep(MainWindow window, MainWindowViewModel model, Control focused, Control? lastTile, PadNavAction action)
		{
			if(focused.DataContext is not PlayerConsoleFilterOption) {
				return null;
			}
			if(action is PadNavAction.Left or PadNavAction.Right) {
				model.RomPicker.CycleConsole(action == PadNavAction.Right ? 1 : -1);
				return ConsoleSegment(window);
			}
			if(action == PadNavAction.Up) {
				return Named(window, "RomPickerBrowseFile") ?? Named(window, "RomPickerBack");
			}
			if(action == PadNavAction.Down) {
				return (lastTile is { IsEffectivelyVisible: true } ? lastTile : null)
					?? RomPickerTile(window) ?? Named(window, "RomPickerBack");
			}
			return null;
		}

		//#1108 AC2: the segment the filter row draws for the option that is up,
		//read off the row's own containers (a container's data context IS its
		//segment). Null while the row has not drawn it, so no caller puts the ring
		//on a control the player cannot see.
		private static Control? ConsoleSegment(MainWindow window)
		{
			if(Named(window, "RomPickerConsoleFilter") is not ListBox row || row.SelectedItem is not { } selected) {
				return null;
			}
			return row.ContainerFromItem(selected) as Control
				?? row.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.DataContext, selected));
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
		//
		//#1160: this is the host half of the authority rule, and it is asked by
		//more than the bridge now. The keyboard navigates the same Play surfaces
		//(ADR-0270 D10), and D10 binds the two paths to the same ANSWER - so the
		//keyboard asks this predicate rather than the door alone, which is what
		//let a press the pad refuses outright still blip: a game paused with no
		//Play surface over it is the console's, and the arrows are game input.
		//One predicate, asked by both, or the two drift apart again.
		public static bool HasAuthority(MainWindowViewModel model)
		{
			//ADR-0255 slice 3 adds one clause, and it is the same predicate:
			//while the Controller sheet is capturing "press a control", the pad
			//is the capture's, so authority is refused and the capture consumes
			//the press. Not a second rule - the capture is a state of "the pad
			//is not the GUI's", which is exactly what this predicate answers,
			//and the capture reads the same pressed set this bridge does.
			return PlayPadNavigation.InPlayDoor(model.IsPlayerMode, model.IsPlayWorkspace)
				&& !model.IsControllerCapturing
				&& PlayPadNavigation.HasAuthority(model.IsPlaySurfaceOverGame, EmuApi.IsRunning(), EmuApi.IsPaused(), model.IsLoadCardVisible, model.IsOnLoadPackPickerVisible);
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

			//The sheet's header control this window's ring was parked on while a
			//restore waits; it dies with the window (see Attach).
			public Control? RomPickerParked { get; set; }

			public PadKeyboard? Keyboard => _keyboard;
			public TextBox? KeyboardField => _keyboardField;

			public Bridge(MainWindow window, MainWindowViewModel model)
			{
				_window = window;
				_model = model;
				_model.InHandDevice = InHand;
				_model.PlayActionBarDeclaration = BarDeclaration;
			}

			//#1104: what ADR-0256 Decision 6 calls the pad in hand: a connected pad
			//is the input being held (PlayMenuHint.ActiveDevice), its family is the
			//last pad pressed - null while it is not told yet, which names no
			//control. Also the seam W-P4's footer used to leave to the keyboard
			//"until the bridge lands".
			private (PlayInputDevice Device, PadFamily? Family) InHand()
			{
				PlayInputDevice device = PlayMenuHint.ActiveDevice(_model.ConnectedGamepadCount());
				return (device, device == PlayInputDevice.Controller ? _padInHand.Current?.Family : null);
			}

			private void RefreshActionBar()
			{
				_model.RefreshPlayActionBar(BarDeclaration(), _keyboard is not null);
			}

			//What the focus owner declares right now, read fresh (not from the last
			//tick) and null - a hidden bar - outside the Play door, where pad
			//navigation is off and the bar's actions would name nothing.
			private IReadOnlyList<PlayBarEntry>? BarDeclaration()
				=> InPlayDoor ? PlayFocusOnOpen.Of(_window)?.Declared() : null;

			public void Tick()
			{
				Tick(InputApi.GetPressedKeys(), null);
				//One UI tick for the GUI test hook's holds, counted after the set was
				//read so a press of N ticks is seen by N ticks. A no-op without
				//--test-hook.
				TestHookWiring.Advance();
			}

			//The tick itself, with the host's inputs passed in: what is pressed
			//now, how long since the last tick, and the backend's two lookups. All
			//of them are the platform's - the core's key state, the dispatcher's
			//clock, the key manager's name table - and the no-argument Tick above
			//passes the real ones. TickForTest is the only other caller.
			public void Tick(IReadOnlyCollection<ushort> pressed, TimeSpan? delta, Func<ushort, string>? keyName = null, Func<string, ushort>? keyCode = null)
			{
				keyName ??= _keyNameForTest ?? InputApi.GetKeyName;
				keyCode ??= _keyCodeForTest ?? InputApi.GetKeyCode;

				//Which pad is in the player's hand, off the last new press. Asked
				//every tick, authority or not: the pad in hand is also what names
				//the control in ADR-0256 Decision 6's on-screen text, and a pad
				//pressed while a game runs is still the pad in hand.
				_padInHand.OnPressed(pressed, key => PadNaming.Of(key, keyName));
				//#1112: kept for the Menu tick row; a pad that unplugs stays here, and the
				//host's aimable query answers false for an index that is gone.
				if(_padInHand.Current is PadId inHand) {
					HapticTickOutput.PadInHand = inHand.Device;
				}

				//#1104: the shared action bar names the control in this hand, so it
				//is recomputed from the same tick that moved the hand - and from the
				//connected count, read here rather than subscribed to for the reason
				//the pad's own state is sampled (PollInterval).
				RefreshActionBar();

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

				//#1177 (ADR-0256, W-P8): Y on the Play home, with no game loaded, opens
				//the Settings sheet - the pad's way to it, since the home draws no
				//Settings control. Read off the same Y code the library's Search uses;
				//the two never fire together because the library sheet being up is
				//"another surface up" here. B from the sheet returns home through the
				//Esc router the pad's B shares.
				if(authority && _keyboard is null
					&& PlayPadNavigation.OpensSettingsFromHome(InPlayDoor, _model.RecentGames.Visible, _model.RomInfo.Format != RomFormat.Unknown,
						_model.RomPicker.IsVisible || _model.IsPlaySurfaceOverGame || _model.IsPlayerSettingsVisible)
					&& PlayPadNavigation.IsSheetEdge(PadNavControls.SheetCode(pad?.Family, pad?.Device ?? -1, PadSheetControl.Search, keyCode), pressed, _previous)) {
					_window.OpenPlayerSettingsSheet();
				}

				//#1110 (ADR-0268 Decision 1): X toggles Favorite on the cover the ring is
				//on - a library tile, a Home tile or Continue. A second sheet control,
				//read off the pressed sets like Y; PlayFavoriteCover answers null (and
				//the press does nothing) wherever no cover has the focus.
				if(authority && InPlayDoor && _keyboard is null
					&& PlayPadNavigation.IsSheetEdge(PadNavControls.SheetCode(pad?.Family, pad?.Device ?? -1, PadSheetControl.Favorite, keyCode), pressed, _previous)) {
					PlayFavoriteCover.Toggle(_model, _window.FocusManager?.GetFocusedElement() as Control);
				}

				//#1034 (ADR-0264 Decision 3): LB/RB cycle the library's console
				//filter. A shoulder is not one of the six the nav mapping resolves
				//- Decision 3 gives the D-pad, A, B and Y their own meanings and
				//the shoulders this one - so it is read off the pad in hand the
				//same way those are, and it is applied only while the library's own
				//surface is up: the folder browser inside the sheet has no console
				//row to cycle, and every other Play surface has no row at all.
				//
				//The edge is taken from the same `_previous` the action above was,
				//before it is recorded below, so a held shoulder cycles once.
				//
				//#1108 AC2: the cycle also LANDS the ring on the segment it moved,
				//which is what makes the row a control the pad can be on and not
				//one it changes from across the sheet (ADR-0256 Decision 3; the
				//last gap the pad-walk carried, KnownChipGaps). Where the ring is
				//when the press arrives decides whether it lands - see below.
				if(authority && InPlayDoor && LibrarySheetIsUp) {
					int shoulder = ShoulderStep(pressed, _previous, pad, keyCode);
					if(shoulder != 0) {
						_model.RomPicker.CycleConsole(shoulder);
						FocusLibraryConsoleSegment();
					}
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
					//#1105, and #1127's shared hook: the optional move / confirm / back
					//sound. After Apply, judged on the state the press left: a Confirm on
					//Resume or a start-game press leaves a game running unpaused, and the
					//blip must not mix into it.
					PlayMenuSound.For(action);
					TickPadInHand(action, pad);
				}
			}

			//#1112: the optional haptic tick on a focus move, to the pad in hand only
			//(its device index), judged like the sound on the state the press left.
			private static void TickPadInHand(PadNavAction action, PadId? pad)
			{
				InputConfig input = ConfigManager.Config.Input;
				if(pad is PadId inHand && inHand.Device >= 0 && MenuSounds.For(action) == MenuSoundKind.Move
					&& HapticTickRule.ShouldTickOnMove(input.MenuTick, input.ForceFeedbackIntensity, EmuApi.IsRunning() && !EmuApi.IsPaused(), (uint)inHand.Device, HapticTickOutput.IsAimable)) {
					HapticTickOutput.Tick((uint)inHand.Device);
				}
			}

			//The authority rule's host half, asked of the one predicate rather than
			//restated here (#1160): the window's keyboard arms ask the same one.
			private bool HasAuthority() => PlayPadNavigationWiring.HasAuthority(_model);

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

		//#1108 AC2: the ring goes onto the console filter segment the cycle just
		//selected, so the row is a place the pad can be and the focus is drawn on
		//the control the press acted on (ADR-0256 Decision 3).
		//
		//The one control that does NOT give the ring up is the search box: a player
		//typing a query keeps the box - and the pad keyboard ADR-0262 opened over it
		//- while the shoulders narrow the grid under them, which is #1034 review
		//finding 2, and the sheet's own answer to "the box holds the ring"
		//(RomPickerFocusTarget asks it the same way). Everywhere else the press
		//lands on the row, including from the sheet's header buttons: an empty
		//library - a first run with no folder yet, or folders the scan answered
		//nothing for - parks the ring on *Library folders…* or Back, and a rule that
		//only landed from the grid would leave the row unreachable exactly where a
		//pad-only player first meets it.
		//#1108 review finding 4: the landing does not hang on something being
		//focused. A rebuild can take the focused container out from under the ring
		//before this runs, and a rule that only landed from a focused control would
		//leave the row unreachable exactly where the sheet is busiest - the cycle
		//would still happen and the ring would never be on what the press acted on.
		//The search box is the one control that keeps the ring, and it is asked the
		//way the sheet asks it.
		//#1108 review finding 3 (ADR-0264 "What outranks the row"): the landing is
		//SKIPPED while the sheet's own claim over the ring is standing - a restore
		//pending, or the moment its remembered game lands - rather than landing and
		//being taken back on the arbiter's next turn. The cycle above still happens
		//(the filter is not what the restore waits on), but the ring never touches
		//the row: without this a player who presses RB while the scan still owes
		//them their last game watches the ring flash on the segment and jump off
		//it. The arbiter already refuses to HAND the segment the ring mid-restore
		//(RomPickerFocusTarget, above); this is the same rule, at the landing.
		private void FocusLibraryConsoleSegment()
		{
			if(_model.RomPicker.IsRestorePending || _model.RomPicker.IsRestoreLanding) {
				return;
			}
			if(SearchBoxHoldsRing()) {
				return;
			}
			if(ConsoleSegment(_window) is Control segment) {
				PlayFocusOnOpen.Enter(segment);
			}
		}

		//The sheet's own definition of "the search box holds the ring" - the same
		//two reads RomPickerFocusTarget answers the claim's search branch with -
		//because a second definition is a second thing to keep in step.
		private bool SearchBoxHoldsRing()
		{
			return Named(_window, "RomPickerSearch") is TextBox box
				&& (box.IsFocused || ReferenceEquals(KeyboardField(_window), box));
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
				//#1235: a press that finds NO control holding the focus. That is the
				//state the real app boots in - measured through the mesen-gui adapter,
				//the home is already drawn while `ui.focused` reads None for the first
				//second - and it is reproducible at any time by taking the ring away.
				//There is nothing here to move, and returning left the screen that way
				//for good: the player pressed and the home stayed without a ring, on
				//the screen a cabinet boots into. ADR-0256 Decision 3 is "one focusable
				//control at a time, WITH the focus drawn" - a cabinet has no cursor to
				//fall back on - so the press asks the ONE place that decides who holds
				//the focus for a decision, instead of deciding here (which would be a
				//second copy of the rule) or giving up. The arbiter answers from the
				//same claims and content area every other decision is read from, so a
				//press cannot land on a surface the screen is not showing.
				if(_window.FocusManager?.GetFocusedElement() is not Control focused) {
					PlayFocusOnOpen.Of(_window)?.Refresh();
					return;
				}
				if(focused.DataContext is PlayerLibraryTile) {
					_libraryTile = focused;
				}
				if(RomPickerHeaderStep(_window, _model, focused, _libraryTile, action) is Control header) {
					PlayFocusOnOpen.Enter(header);
					return;
				}
				//#1108 AC2: the filter row's own steps, asked the same way and from
				//the same place as the header's - a control the sheet owns answers
				//before the engine's geometric search does, so the row's four
				//directions mean what the row says they mean.
				if(RomPickerFilterRowStep(_window, _model, focused, _libraryTile, action) is Control onRow) {
					PlayFocusOnOpen.Enter(onRow);
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
				//#1036 (ADR-0264 Decision 8): *Add a folder…* has two doors and the
				//press decides which one. The pointer's is the view's own handler (the
				//native folder dialog, which is what a player at a desk expects); the
				//pad's is the sheet's own folder browser, answered here BEFORE the
				//activation because a native dialog owns the screen - the focus engine
				//cannot draw a ring in it, so a cabinet with a pad and nothing else
				//could not add a folder at all.
				if(action == PadNavAction.Confirm && focused.Name == "RomPickerAddFolder") {
					_model.RomPicker.AddFolderFromPad();
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
					Control? next = manager.FindNextElement(Direction(action), options) as Control;
					//#1111: a tab strip's tabs sit side by side, and the engine can
					//answer Down with the neighbouring tab (the page's rows are far
					//to the right of the strip's left-most tab). Down from a tab goes
					//into the page, never along the strip: Left / Right do that.
					//Only the player settings strip is rewired; any other TabControl
					//keeps the engine's answer.
					if(action == PadNavAction.Down && focused is TabItem && next is TabItem && FirstInPage(focused) is Control inPage) {
						next = inPage;
					}
					//#1146 review finding 1: the settings sheet's footer line - the
					//"More in Options…" button on Audio and Controls - is drawn above
					//Done and to its left, so its projection never overlaps Done's and
					//the engine never answers Up with it. The button was visible,
					//enabled and focusable with no press reaching it, which is what
					//made it a name in the walk's gap list instead of a control a
					//player can use. Up from Done goes to that line.
					else if(action == PadNavAction.Up && focused.Name == "btnPlayerSettingsDone" && FooterControl(focused, "btnPlayerSettingsMoreInOptions") is Control line) {
						next = line;
					}
					//...and the line hands the ring back to the page, so the footer is
					//a step and not a pocket: the engine answers the line's own Up with
					//nothing (its only neighbour up there is the page, which is drawn
					//across the line's whole width), so the answer is taken from Done,
					//the footer's bottom row, whose Up the engine does answer - and the
					//page's own bottom row is what lies above the line.
					else if(action == PadNavAction.Up && focused.Name == "btnPlayerSettingsMoreInOptions" && FooterControl(focused, "btnPlayerSettingsDone") is Control done) {
						next = manager.FindNextElement(NavigationDirection.Up, new FindNextElementOptions {
							FocusedElement = done,
							SearchRoot = PlayFocusOnOpen.Of(done)?.SearchRoot(),
						}) as Control;
					}
					if(next is Control landed) {
						PlayFocusOnOpen.Enter(landed);
					}
				}
			}

			//A control the settings sheet draws now, by the name it gave it: the
			//footer's two rows (ADR-0249, PRD Part B §13.3 rule 10). Null when the
			//sheet is not drawing it - "More in Options…" is on the Audio and
			//Controls tabs alone, and the hint takes that line on the others.
			private static Control? FooterControl(Control focused, string name)
			{
				Control? sheet = focused.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a.Name == "PlayerSettingsSheet");
				return sheet?.GetVisualDescendants().OfType<Control>()
					.FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
			}

			//The first control of the page under the tab strip that holds this tab.
			private static Control? FirstInPage(Control tab)
			{
				TabControl? tabs = tab.FindAncestorOfType<TabControl>();
				if(tabs?.Name != "PlayerSettingsTabs") {
					return null;
				}
				return tabs.GetVisualDescendants().OfType<Control>().FirstOrDefault(c =>
					c is not TabItem && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.FindAncestorOfType<TabItem>() is null);
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
					case PadValueVerb.Step when target is ComboBox combo:
						//A stepper row (#1111): the value changes in place, at once.
						combo.SelectedIndex = PlayPadValueRules.Walk(combo.SelectedIndex, combo.ItemCount, answer.Delta);
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
					ComboBox { Name: "cboDisplayInterfaceSize" } => PadValueKind.Stepper,
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
			private bool ApplyKeyboard(PadNavAction action) => ApplyKeyboardOutcome(action) is not null;

			//The press with the keyboard's own answer, for the caller that has to tell a
			//key the keyboard took from one it swallowed (#1281 review, finding 2):
			//PadKeyboard answers None when it does nothing, and a full field is exactly
			//that - the draft does not move. Null is "there is no keyboard", which is
			//not an outcome of a press. Every other caller only asks whether the press
			//was the keyboard's, which is what ApplyKeyboard above answers.
			private PadKeyboardOutcome? ApplyKeyboardOutcome(PadNavAction action)
			{
				if(_keyboard is null || _keyboardField is null) {
					return null;
				}
				PadKeyboardOutcome outcome = _keyboard.Press(action);
				switch(outcome) {
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
				return outcome;
			}

			//#1281 (text.type): the string a GUI test run asks for, typed through the
			//one on-screen keyboard this bridge owns. Every character is the two
			//gestures a player makes - the D-pad walks the cursor onto its key, A
			//presses it - fed through PadKeyboard and ApplyKeyboard, so nothing here
			//is a second typing path and the field, the draft and the panel all move
			//exactly as they do for a person. Returns null when the text was typed,
			//otherwise why it was not.
			public string? TypeOnKeyboard(string text)
			{
				if(text.Length == 0) {
					return "text.type takes the text to type";
				}
				if(_keyboard is null || _keyboardField is null) {
					return "no on-screen keyboard is open: focus a text field and press A first";
				}
				foreach(char c in text) {
					if(_keyboard is null) {
						return "the on-screen keyboard closed while typing";
					}
					string? why = TypeChar(c);
					if(why is not null) {
						return why;
					}
				}
				return null;
			}

			private string? TypeChar(char c)
			{
				PadKeyboard keyboard = _keyboard!;
				if(keyboard.IndexOf(c) < 0) {
					return "the on-screen keyboard has no key for \"" + c + "\"";
				}
				//A capital letter is the keyboard's case key and then the letter: the
				//pad keyboard types lower case unless its Shift is on (PadKeyboard).
				bool upper = char.IsUpper(c) && keyboard.Keys.Any(k => k.Kind == PadKeyKind.Shift);
				if(upper != keyboard.Shifted) {
					bool shiftedBefore = keyboard.Shifted;
					string? shifted = PressKeyAt(keyboard.Keys.ToList().FindIndex(k => k.Kind == PadKeyKind.Shift), out _);
					if(shifted is not null) {
						return shifted;
					}
					//The case key is the one press whose proof is not the draft: it
					//toggles Shifted. A press that reports success and leaves the case
					//where it was did not land, and the letter after it would be typed
					//in the wrong case.
					if(keyboard.Shifted == shiftedBefore) {
						return "the on-screen keyboard did not accept the case key for \"" + c + "\"";
					}
				}
				string? typed = PressKeyAt(_keyboard?.IndexOf(c) ?? -1, out PadKeyboardOutcome? outcome);
				if(typed is not null) {
					return typed;
				}
				if(outcome is not PadKeyboardOutcome.Edited) {
					//#1281 review, finding 2: the press is reported done only when the
					//field moved. PadKeyboard.PressKey answers None at the field's own
					//MaxLength, and the draft - and so the field - is unchanged; a step
					//that said "typed" there would be lying about a field the player
					//can see.
					return "the on-screen keyboard did not accept \"" + c + "\": the field holds no more characters";
				}
				return null;
			}

			//The key at `index`, pressed the way a player presses it: the D-pad walks
			//the cursor onto it (Right wraps, so every key is reachable) and the pad's
			//own confirm presses it. `outcome` is the keyboard's own answer to that
			//confirm - null when there is no keyboard left to answer - which is how a
			//caller tells a key it took from a press it swallowed.
			private string? PressKeyAt(int index, out PadKeyboardOutcome? outcome)
			{
				outcome = null;
				if(index < 0 || _keyboard is null) {
					return "the on-screen keyboard is not there to press that key";
				}
				for(int steps = 0; _keyboard.Cursor != index; steps++) {
					if(steps > _keyboard.Keys.Count) {
						return "the on-screen keyboard never moved onto that key";
					}
					ApplyKeyboard(PadNavAction.Right);
					if(_keyboard is null) {
						return "the on-screen keyboard closed while the cursor walked";
					}
				}
				outcome = ApplyKeyboardOutcome(PadNavAction.Confirm);
				return null;
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
				RefreshActionBar();
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
				RefreshActionBar();
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

			//#1034 (ADR-0264 Decision 3): which shoulder went down this tick - LB
			//as -1, RB as +1, neither as 0. Resolved off the pad in the player's
			//hand and the host's own name table, exactly the way PadNavControls
			//resolves the six: the family's own spelling first ("Pad1 L1", the
			//XInput-shaped table) and the other family's second ("Joy1 But5", the
			//DirectInput one), because which spelling a host defines is the
			//backend's business and a name it does not define answers 0.
			//KeyPresets binds the console's own L/R to the same four names, so a
			//code that is not a shoulder on this pad cannot be read as one - and
			//the mapping itself is not extended: Decision 4's six stay the six,
			//and a shoulder is one press on one sheet rather than a seventh
			//navigation control every surface would have to answer for.
			//
			//LB is asked first, so two shoulders in one tick resolve the same way
			//whatever order the host enumerated its pressed set in - the reason
			//PlayPadNavigation.Next breaks its own two presses in a fixed order.
			private static readonly (int Step, string Xbox, string Ps4)[] Shoulders = {
				(-1, "L1", "But5"),
				(1, "R1", "But6")
			};

			private static int ShoulderStep(IReadOnlyCollection<ushort> pressed, IReadOnlyCollection<ushort> previous, PadId? pad, Func<string, ushort> keyCode)
			{
				if(pad is not PadId known) {
					return 0;
				}
				string padPrefix = "Pad" + (known.Device + 1).ToString() + " ";
				string joyPrefix = "Joy" + (known.Device + 1).ToString() + " ";
				foreach((int step, string xbox, string ps4) in Shoulders) {
					string own = known.Family == PadFamily.Xbox ? padPrefix + xbox : joyPrefix + ps4;
					string other = known.Family == PadFamily.Xbox ? joyPrefix + ps4 : padPrefix + xbox;
					foreach(string name in new[] { own, other }) {
						ushort code = keyCode(name);
						if(code != 0 && pressed.Contains(code) && !previous.Contains(code)) {
							return step;
						}
					}
				}
				return 0;
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
