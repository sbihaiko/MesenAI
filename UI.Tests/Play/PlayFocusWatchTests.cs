using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0256 (accepted 2026-10-04) Decision 3, #1129: the give-up watch a Play
//focus decision keeps once its turn bound is spent is production behaviour, and
//`PlayFocusOnOpen` is the only caller. The rule is host-free in
//UI/Logic/PlayFocusWatch; this pins it with a fake clock (the ticks are
//literals, never read from the machine, so the case cannot go flaky under load -
//which is the whole point of the deadline). The headless suite keeps only the
//wiring: the window's event, the attempt and the clock reading.
//
//The window is quoted from PlayFocusOnOpen's own comment and from the defect it
//fixes: five turns can be spent inside a single drain of the dispatcher queue,
//so the pass that makes a surface focusable needs to be waited for past them.
public class PlayFocusWatchTests
{
	//Two seconds (UI/Utilities/PlayFocusOnOpen, the #1129 comment above the
	//LayoutWatch it replaced). Pinned as a literal: the boundary cases below are
	//written against this number, not against the constant, so a change to the
	//window has to be a decision and not a silent drift of every expectation.
	private const long Window = 2000;

	[Fact]
	public void The_watch_waits_two_seconds_for_the_pass()
	{
		Assert.Equal(TimeSpan.FromSeconds(2), PlayFocusWatch.Window);
	}

	//A decision that never spent its turns has no watch, so a layout pass of an
	//unrelated surface (a resize, a theme change, a later screen) does nothing.
	[Fact]
	public void A_watch_that_was_never_armed_does_nothing_on_a_pass()
	{
		PlayFocusWatch watch = new();

		Assert.False(watch.IsArmed);
		Assert.Equal(PlayFocusWatchStep.Nothing, watch.Step(nowMilliseconds: 0, focusMoved: false));
	}

	[Fact]
	public void A_pass_inside_the_window_is_attempted()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 1000);

		Assert.True(watch.IsArmed);
		Assert.Equal(PlayFocusWatchStep.Attempt, watch.Step(nowMilliseconds: 1000, focusMoved: false));
		Assert.Equal(PlayFocusWatchStep.Attempt, watch.Step(nowMilliseconds: 1500, focusMoved: false));
		//The mark itself is still inside: the deadline is what the pass has to be
		//past, not what it has to reach.
		Assert.Equal(PlayFocusWatchStep.Attempt, watch.Step(nowMilliseconds: 3000, focusMoved: false));
	}

	//The bound has to be a real one. A surface that never becomes focusable is
	//given up on - and the pass at the deadline applies NOTHING, which is what
	//keeps a late layout from running one more Apply (and one more Focus())
	//against a window the decision has already left.
	[Fact]
	public void A_pass_past_the_deadline_detaches_without_an_attempt()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 1000);

		Assert.Equal(PlayFocusWatchStep.Detach, watch.Step(nowMilliseconds: 3001, focusMoved: false));
		Assert.False(watch.IsArmed);
		//And it stays off: the handler is coming off the window, so a stray pass
		//after it is nothing at all.
		Assert.Equal(PlayFocusWatchStep.Nothing, watch.Step(nowMilliseconds: 3002, focusMoved: false));
	}

	//The same abandonment rule every attempt has: the player moved the ring on
	//purpose, so the decision is not a decision any more.
	[Fact]
	public void A_focus_that_moved_off_the_holder_detaches_before_the_deadline()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 1000);

		Assert.Equal(PlayFocusWatchStep.Detach, watch.Step(nowMilliseconds: 1200, focusMoved: true));
		Assert.False(watch.IsArmed);
	}

	//Passes do not extend the watch: the deadline is read once, at the give-up,
	//so the attempts cannot push it back for as long as passes keep coming.
	[Fact]
	public void The_attempts_do_not_push_the_deadline_back()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 0);

		for(long now = 0; now <= 1999; now += 250) {
			Assert.Equal(PlayFocusWatchStep.Attempt, watch.Step(now, focusMoved: false));
		}

		Assert.Equal(PlayFocusWatchStep.Detach, watch.Step(nowMilliseconds: 2001, focusMoved: false));
	}

	//A new decision supersedes the previous one's watch (PlayFocusOnOpen.Refresh
	//disarms before posting the next one), and the new arm reads its own mark: a
	//re-arm must never inherit the earlier reading.
	[Fact]
	public void Re_arming_reads_its_own_deadline()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 1000);
		Assert.Equal(PlayFocusWatchStep.Detach, watch.Step(nowMilliseconds: 3001, focusMoved: false));

		watch.Arm(nowMilliseconds: 10000);

		Assert.True(watch.IsArmed);
		Assert.Equal(PlayFocusWatchStep.Attempt, watch.Step(nowMilliseconds: 11999, focusMoved: false));
		Assert.Equal(PlayFocusWatchStep.Detach, watch.Step(nowMilliseconds: 12001, focusMoved: false));
	}

	//Refresh disarms outright: the previous decision's watch is dropped in the
	//same turn the next one is posted, so it cannot apply on the way in.
	[Fact]
	public void A_disarmed_watch_does_nothing()
	{
		PlayFocusWatch watch = new();
		watch.Arm(nowMilliseconds: 0);
		watch.Disarm();

		Assert.False(watch.IsArmed);
		Assert.Equal(PlayFocusWatchStep.Nothing, watch.Step(nowMilliseconds: 0, focusMoved: false));
	}
}
