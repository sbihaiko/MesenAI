using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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

	private sealed record Claim(Func<bool> IsOpen, Func<Control?> Target);

	public PlayFocusOnOpen(Window window)
	{
		_window = window;
		Installed.AddOrUpdate(window, this);
	}

	//The surface's own properties, watched so that a close re-arbitrates as well
	//as an open: closing a sheet has to hand the focus back to whatever is under
	//it, and that is the same decision read the other way round. `isOpen` is the
	//expression the surface is rendered from - never a second copy of it.
	public void When(INotifyPropertyChanged source, string[] properties, Func<bool> isOpen, Func<Control?> target)
	{
		Claim claim = new(isOpen, target);
		_claims.Add(claim);
		Watch(source, properties);
	}

	//The content area below every surface: the home's primary action, the slot
	//grid over a game. Deliberately not a claim - it is not in the Esc stack,
	//and the screens that make it up also run in Advanced, where no surface
	//above it exists. It is what is left when no claim is open.
	public void Content(INotifyPropertyChanged source, string[] properties, Func<Control?> target)
	{
		_content = target;
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
	//that is only now visible cannot take the focus in the same turn, and at
	//Loaded so the layout pass has run.
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
		if(Enter(target ?? _content?.Invoke() ?? _window.GetControl<Panel>("RendererPanel")) || claim is null) {
			//Nothing is up, so there is nothing that could still become
			//focusable: the content area and the renderer are what is left, and
			//a window with neither keeps whatever focus it has.
			return;
		}
		Retry(attempt, held);
	}

	private void Retry(int attempt, object? held)
	{
		if(attempt + 1 >= Attempts) {
			return;
		}
		Dispatcher.UIThread.Post(() => Apply(attempt + 1, held), DispatcherPriority.Loaded);
	}

	//True while a Play surface is up over the content area. A screen that asks
	//for the focus on its own (StateGrid, which navigates its own SelectedIndex
	//and also runs in Advanced) asks this first, so it cannot take the focus
	//from the sheet that is over it - which is what used to happen.
	public static bool SurfaceIsUp(Visual from)
	{
		return Of(from)?.Open() is not null;
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
