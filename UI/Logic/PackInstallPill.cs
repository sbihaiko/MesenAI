using System;

namespace Mesen.Logic;

//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P9; ADR-0146): an accepted community
//pack installs while the game plays, and the player sees it as a HUD pill -
//never a dialog, never a consent prompt. This is the pill's host-free state:
//when it shows, what it says, and when it goes away.
//
//#734 ("every wait has an animation"): the bar always moves. While the
//artifact downloads with a known size it fills with the bytes; without a
//size, and once the bytes are in (hash check, extract, install), it is
//indeterminate. The pill never shows a number.

public enum PackInstallPillState
{
	Hidden,
	//"Installing <pack>…"
	Installing,
	//"The pack could not be downloaded. Playing without it."
	Failed
}

public sealed class PackInstallPill
{
	//W-P9: the failure sentence stays 5 s, in the pill's place.
	public static readonly TimeSpan FailureDuration = TimeSpan.FromSeconds(5);

	private DateTime _failedAt;

	public PackInstallPillState State { get; private set; } = PackInstallPillState.Hidden;
	public string PackName { get; private set; } = "";
	//The bar's fill, 0..1; null while it is indeterminate.
	public double? Fraction { get; private set; }

	//W-P9 is a Play render: the pill shows in Player mode's Play while it has
	//something to say (the status line carries it everywhere else).
	public static bool ShowsOnScreen(PackInstallPillState state, bool playerMode, bool playWorkspace)
	{
		return state != PackInstallPillState.Hidden && playerMode && playWorkspace;
	}

	//The pill shows only for a download that changes the installed pack: a
	//catalog row already installed from the same artifact is the routine
	//"nothing to do" on every load of that game, and must not flash a pill.
	public static bool ShowsFor(string? installedSourceSha256, string catalogSha256)
	{
		if(string.IsNullOrWhiteSpace(catalogSha256)) {
			return false;
		}
		return !string.Equals((installedSourceSha256 ?? "").Trim(), catalogSha256.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	public void Begin(string packName)
	{
		PackName = packName ?? "";
		State = PackInstallPillState.Installing;
		Fraction = null;
	}

	//A download's bytes so far and its size (null or 0 when the host does
	//not say). True when the bar changed enough to redraw: a percent, or
	//between filling and indeterminate.
	public bool Report(long received, long? total)
	{
		if(State != PackInstallPillState.Installing) {
			return false;
		}
		double? next = total is long size && size > 0 && received < size ? Math.Max(0, (double)received / size) : null;
		double? previous = Fraction;
		if(next == null && previous == null) {
			return false;
		}
		if(next != null && previous != null && Math.Abs(next.Value - previous.Value) < 0.01) {
			return false;
		}
		Fraction = next;
		return true;
	}

	//installed: the pack is in place (the W-P3 "Applied …" toast follows on the
	//reload). Anything else - a failed download or install - is the one failure
	//sentence; the log has the rest (rule 6). A finish with no pill showing (a
	//routine skip) changes nothing.
	public void Finish(bool installed, DateTime now)
	{
		if(State != PackInstallPillState.Installing) {
			return;
		}
		if(installed) {
			State = PackInstallPillState.Hidden;
			return;
		}
		State = PackInstallPillState.Failed;
		_failedAt = now;
	}

	//Silent end: the install had nothing to do after all (up to date, disabled).
	public void Cancel()
	{
		if(State == PackInstallPillState.Installing) {
			State = PackInstallPillState.Hidden;
		}
	}

	public void Tick(DateTime now)
	{
		if(State == PackInstallPillState.Failed && now - _failedAt >= FailureDuration) {
			State = PackInstallPillState.Hidden;
		}
	}
}
