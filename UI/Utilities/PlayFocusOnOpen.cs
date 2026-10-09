using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mesen.Utilities;

//ADR-0256 (accepted 2026-10-04) Decision 3: one focusable control at a time,
//with the focus drawn - an arcade cabinet has no cursor to fall back on, so
//"where am I" has to be on screen. The ADR states the other half of the same
//rule: the decision has to live in ONE place. Before this, every Play surface
//focused its own first control from its own code-behind (MainWindow's ctor,
//PlayEdgeFlowsWiring, PlayHomeView, StateGrid) with nothing arbitrating between
//them, so a sheet opening over the home and the home coming back were two
//posted Focus() calls racing each other, and the loser took the keyboard.
//
//This is that one place. A surface registers a claim in ADR-0249's Esc order
//(topmost first) and the arbiter answers "who holds the focus now" from the
//claims' own visibility - the same expressions the surfaces render from, so a
//claim can never disagree with the screen. It is not a new roving-focus
//container and it does not touch tab stops: it only decides *who* is focused
//when a Play surface opens or closes, and *how* (Enter).
internal sealed class PlayFocusOnOpen
{
	//Keyed by the top level because keyboard focus is one for the app, not one
	//per window: the registry has to be reachable from a view that only knows
	//itself (StateGrid, PlayHomeView) and has to answer for its own window.
	private static readonly ConditionalWeakTable<TopLevel, PlayFocusOnOpen> Installed = new();

	private readonly Window _window;
	private readonly List<Claim> _claims = new();
	private Func<Control?>? _content;
	private Func<IReadOnlyList<PlayBarEntry>>? _contentActions;

	private sealed record Claim(Func<bool> IsOpen, Func<Control?> Target, Func<Control?>? Root = null, Func<IReadOnlyList<PlayBarEntry>>? Actions = null);

	public PlayFocusOnOpen(Window window)
	{
		_window = window;
		Installed.AddOrUpdate(window, this);
	}

	//The surface's own properties, watched so that a close re-arbitrates as well
	//as an open: closing a sheet has to hand the focus back to whatever is under
	//it, and that is the same decision read the other way round. `isOpen` is the
	//expression the surface is rendered from - never a second copy of it.
	//
	//`root` is for the surface whose first control is a row of its own list, and
	//it is the answer to a question the inference in SearchRoot cannot give: the
	//nearest ancestor two controls share, walked from the target's parent, is
	//then the row's own item container - one row, and a D-pad press inside it has
	//nowhere to go. A surface that says what its walk stays inside keeps the
	//presses working past the first row (#845).
	//
	//`actions` is #1104's half of the same decision: the surface that claims the
	//focus also declares what its buttons do, so the shared action bar
	//(PlayActionBar) lists exactly what the surface performs and no surface
	//writes a footer of its own. Null is a surface not on the bar yet (#1108).
	public void When(INotifyPropertyChanged source, string[] properties, Func<bool> isOpen, Func<Control?> target, Func<Control?>? root = null, Func<IReadOnlyList<PlayBarEntry>>? actions = null)
	{
		Claim claim = new(isOpen, target, root, actions);
		_claims.Add(claim);
		Watch(source, properties);
	}

	//The content area below every surface: the home's primary action, the slot
	//grid over a game. Deliberately not a claim - it is not in the Esc stack,
	//and the screens that make it up also run in Advanced, where no surface
	//above it exists. It is what is left when no claim is open.
	public void Content(INotifyPropertyChanged source, string[] properties, Func<Control?> target, Func<IReadOnlyList<PlayBarEntry>>? actions = null)
	{
		_content = target;
		_contentActions = actions;
		Watch(source, properties);
	}

	private void Watch(INotifyPropertyChanged source, string[] properties)
	{
		source.PropertyChanged += (s, e) => {
			if(e.PropertyName != null && properties.Contains(e.PropertyName)) {
				Refresh();
			}
		};
	}

	//How many turns the decision is re-applied for before the arbiter accepts
	//that the surface it chose cannot take the focus. Five is not a tuning
	//knob: the first attempt succeeds in every case but the one below, and this
	//only bounds how long a surface that never becomes focusable is waited for.
	private const int Attempts = 5;

	//Re-arbitrate: the topmost open surface takes the focus, else the content
	//area, else the renderer (the game's own surface, which is what has the
	//focus while a game runs with nothing over it). Posted, because a control
	//that is only now visible cannot take the focus in the same turn. Loaded, and
	//NOT later: measured, a control can be found, focusable, enabled and still not
	//yet *effectively visible* under this priority, which is why the retry below
	//steps down to Background rather than repeating this one - see Apply.
	public void Refresh()
	{
		//Whoever holds the focus now is captured before the decision is posted:
		//a retry that would take the focus from someone who moved it on purpose
		//in the meantime is not a retry, it is a fight (see Apply).
		object? held = _window.FocusManager?.GetFocusedElement();
		Dispatcher.UIThread.Post(() => Apply(0, held), DispatcherPriority.Loaded);
	}

	//One attempt, and the reason it may not be the last one: a surface opening
	//makes its panel visible and gives its first control its place in two
	//separate steps, and a single posted turn can beat the second one under
	//load. Measured: closing the Save states sheet back to W-P4 left the focus
	//on the home under a full headless suite, while the overlay's own button
	//took the focus on demand one turn later. So a failed attempt is retried,
	//bounded - and abandoned the moment the focus has moved to something this
	//decision did not choose, so the pad moving the ring while a surface is
	//settling is never undone by the surface.
	//
	//The retry applies to BOTH halves of the decision. It used to be skipped
	//whenever no surface was up ("nothing is up, so nothing can still become
	//focusable"), and that shortcut was wrong in the one case that matters:
	//#824, where the home's primary action was found, focusable and enabled and
	//was still not *effectively visible*, so Enter failed and the home opened
	//with nothing focused - the exact failure ADR-0256 Decision 3 exists to
	//prevent, and the one Decision 8's keyboard-less first run cannot survive.
	private void Apply(int attempt, object? held)
	{
		if(attempt > 0 && !ReferenceEquals(_window.FocusManager?.GetFocusedElement(), held)) {
			return;
		}
		Claim? claim = Open();
		Control? target = claim?.Target();
		if(claim is not null && target is null) {
			//A surface is up and its first control is not in the tree yet: the
			//answer is to wait for it, never to focus what is underneath - that
			//fallback is how the home took the keyboard from the sheet over it.
			Retry(attempt, held);
			return;
		}
		if(Enter(target ?? _content?.Invoke() ?? _window.GetControl<Panel>("RendererPanel"))) {
			return;
		}
		//#824: the retry is for whichever half of the decision produced the
		//target. Instrumented, the home's case reads exactly:
		//  attempt=0 name=PlayHomeOpenRomPrimary visible=False enter=False
		//  attempt=1 name=PlayHomeOpenRomPrimary visible=True  enter=True
		//so the control exists, is focusable and is enabled, and the layout pass
		//that makes it effectively visible lands after the Loaded turn this
		//decision is posted in - which is why Retry steps down to Background
		//instead of repeating Loaded, where every attempt would see False.
		Retry(attempt, held);
	}

	private void Retry(int attempt, object? held)
	{
		if(attempt + 1 >= Attempts) {
			return;
		}
		Dispatcher.UIThread.Post(() => Apply(attempt + 1, held), DispatcherPriority.Background);
	}

	//True while a Play surface is up over the content area. A screen that asks
	//for the focus on its own (StateGrid, which navigates its own SelectedIndex
	//and also runs in Advanced) asks this first, so it cannot take the focus
	//from the sheet that is over it - which is what used to happen.
	public static bool SurfaceIsUp(Visual from)
	{
		return Of(from)?.Open() is not null;
	}

	//Decision 3, read while the pad moves: the surface that holds the focus is
	//the one the pad walks. The claim above decides who takes the focus when a
	//surface opens; this is the same decision for the step after that, and it
	//needs saying because the engine's own directional search is geometric across
	//the whole window and the surfaces under a sheet are on screen on purpose -
	//W-P4's card stays behind the sheets opened from it, dimmed, and the home
	//stays behind a sheet opened from a task door. Measured on a game loaded with
	//Settings up: one D-pad Down from the storage row landed on the dimmed card's
	//Resume, which is a surface the player can see but is not using, and Confirm
	//would have acted on it. (The card is only *drawn* dim: IsHitTestVisible is
	//false for the pointer, and focus navigation never asks that.)
	//
	//The root is the closest ancestor the open surface's own first control and
	//the focused control share. Both are inside the surface by construction, so
	//the answer is the surface itself and nothing outside it - for W-P4's card,
	//OverlayControls; for Settings' System tab, that tab's own panel. Null when
	//there is no surface (the content area's screens keep the whole window, which
	//is what they had) or when the two controls are not in one tree yet, and the
	//bridge then searches as it did before.
	public Control? SearchRoot()
	{
		if(Open() is not Claim claim || claim.Target() is not Control target) {
			return null;
		}
		//The surface named its own root: it knows what its walk is, and the
		//inference below cannot answer for a target that is one row of a list.
		if(claim.Root is not null) {
			return claim.Root();
		}
		if(_window.FocusManager?.GetFocusedElement() is not Visual focused) {
			return null;
		}

		HashSet<Visual> aboveFocused = new();
		for(Visual? ancestor = focused; ancestor is not null; ancestor = ancestor.GetVisualParent()) {
			aboveFocused.Add(ancestor);
		}
		//From the target's parent: a surface is never the target alone, or the
		//first control could not move at all.
		for(Visual? ancestor = target.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent()) {
			if(aboveFocused.Contains(ancestor)) {
				return ancestor as Control;
			}
		}
		return null;
	}

	//A content-area screen saying it is (re)appearing. It keeps its own triggers
	//- a view knows when it is attached and when its data context arrives - and
	//gives up the decision, which needs to know what may be over it.
	public static void ContentChanged(Visual from)
	{
		Of(from)?.Refresh();
	}

	//The one place a Play surface is focused, and the only place that decides
	//how. Directional, not Tab: the navigation method is what makes this a
	//:focus-visible focus, and PlayerTheme's PlayerFocusRing (the three-layer
	//glow the theme has carried since it landed) is painted from it. A pad move
	//and a sheet opening are the same kind of focus, so they enter the same way.
	public static bool Enter(Control? target)
	{
		if(target is null || !target.Focusable) {
			return false;
		}
		//Keyboard focus is one for the app: a Play surface opening in this window
		//must not take it from another window (a debugger, a tool, a second
		//headless MainWindow). The home used to carry this guard alone (#625);
		//it belongs to the one place that can now take the focus.
		if(TopLevel.GetTopLevel(target) is TopLevel top && top.FocusManager?.GetFocusedElement() is Visual focused
			&& TopLevel.GetTopLevel(focused) is TopLevel other && other != top) {
			return false;
		}
		return target.Focus(NavigationMethod.Directional);
	}

	//What the surface holding the focus declares, or null when it declared
	//nothing (or nothing is up). The topmost open claim speaks; the content area
	//speaks only when no claim is open, the same order Apply decides the focus in.
	public IReadOnlyList<PlayBarEntry>? Declared()
	{
		return Open() is Claim claim ? claim.Actions?.Invoke() : _contentActions?.Invoke();
	}

	private Claim? Open()
	{
		foreach(Claim claim in _claims) {
			if(claim.IsOpen()) {
				return claim;
			}
		}
		return null;
	}

	//This window's arbiter, or null when the visual is not in a window yet. The
	//window itself asks for it on open; a view asks through the two helpers above
	//so it never holds a reference to the window's wiring.
	public static PlayFocusOnOpen? Of(Visual from)
	{
		return TopLevel.GetTopLevel(from) is TopLevel top && Installed.TryGetValue(top, out PlayFocusOnOpen? registry) ? registry : null;
	}
}
