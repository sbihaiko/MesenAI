using System;

namespace Mesen.Logic;

//ADR-0256 (accepted 2026-10-04) Decision 3, #1129: the give-up watch a Play
//focus decision keeps once its turn bound is spent.
//
//`PlayFocusOnOpen` posts a bounded number of turns for the surface it chose and
//retries while the surface is still settling. Under a loaded dispatcher all of
//those turns can be spent inside a single drain of the queue, and a surface that
//was found, focusable and enabled and is still not yet *effectively visible*
//(the #824 reading) is then left with no focus at all - a surface that is simply
//opening changes no watched property, so nothing re-asks the decision. The
//window's own LayoutUpdated is the event that says the pass which makes it
//visible has happened, and that pass is this watch's second chance.
//
//The watch is bounded, and this type is the bound: it is armed with a clock
//reading, answers what one layout pass does with the deadline read BEFORE the
//attempt, and is dropped at the deadline, the moment the decision lands and the
//moment the focus moves off the element the attempts are anchored to. The
//decision lives here rather than in the window so it is asserted without one
//(ADR-0123; UI.Tests/Play/PlayFocusWatchTests) - `PlayFocusOnOpen` keeps only
//the wiring: the event, the attempt and the clock it reads.
public sealed class PlayFocusWatch
{
	//How long a spent decision waits for the pass that makes its surface
	//focusable. Not a tuning knob: it is a patience bound on a surface that never
	//becomes focusable, and the pass it waits for lands within a few frames of the
	//open in every measured case.
	public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

	private long _until;

	//True while a pass may still be attempted. Read by the wiring to keep the
	//window's LayoutUpdated handler from being attached twice.
	public bool IsArmed { get; private set; }

	//Armed with the tick the decision gave up. Arming again re-reads the deadline
	//from the new reading, so a later arm never inherits an earlier one's mark.
	public void Arm(long nowMilliseconds)
	{
		_until = nowMilliseconds + (long)Window.TotalMilliseconds;
		IsArmed = true;
	}

	public void Disarm() => IsArmed = false;

	//What one layout pass does. The deadline is read BEFORE the attempt: a pass
	//that lands after the mark applies nothing at all, which is the bound the
	//window's comment promises and the reason a resize or a later screen cannot
	//move the focus once the decision has given up. So does a focus that has
	//moved off the element the attempts are anchored to - the same abandonment
	//rule every attempt has.
	public PlayFocusWatchStep Step(long nowMilliseconds, bool focusMoved)
	{
		if(!IsArmed) {
			return PlayFocusWatchStep.Nothing;
		}
		if(focusMoved || nowMilliseconds > _until) {
			IsArmed = false;
			return PlayFocusWatchStep.Detach;
		}
		return PlayFocusWatchStep.Attempt;
	}
}

//The three answers one layout pass can get: no watch is armed (`Nothing`), the
//watch is still inside its window (`Attempt`), or the watch is done and the
//handler has to come off the window (`Detach`).
public enum PlayFocusWatchStep
{
	Nothing,
	Attempt,
	Detach
}
