using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//What the focused control does with its own value, which the host classifies
//(PlayPadNavigationWiring.KindOf) and the rule below answers for.
public enum PadValueKind
{
	None,
	Slider,
	Popup,
	Hold
}

//None hands the press back to the focus engine and Confirm's activation, as
//before #964. Consume is a press an open drop-down owns and ignores, so the
//focus cannot walk away from a popup that is still on screen.
public enum PadValueVerb
{
	None,
	Step,
	Open,
	Walk,
	Commit,
	Cancel,
	Consume,
	HoldStart,
	HoldEnd
}

public readonly record struct PadValueAnswer(PadValueVerb Verb, int Delta = 0);

//#964, ADR-0256's stop rule ("every Play path ... is reachable and reversible
//from a pad alone"): a slider, a drop-down and Hold to Compare were reachable by
//the pad but not operable. The focused control's own value semantics come first,
//and focus movement is what is left over. Host-free (ADR-0123), applied by the
//one bridge (PlayPadNavigationWiring) rather than by any view - per-view pad code
//is ADR-0256's explicit non-goal.
public static class PlayPadValueRules
{
	//What one press means to the focused control. Slider: Left/Right step it
	//(a horizontal slider's own arrows), Up/Down still move the focus. Popup:
	//Confirm opens it; while open Up/Down walk the rows, Confirm commits, Back
	//closes without changing the value - Avalonia's own Enter/Escape. Hold: the
	//Confirm press starts it; its release is EndsHold's, because a release is
	//not an action the pad's edge rule produces.
	public static PadValueAnswer Next(PadValueKind kind, bool popupOpen, PadNavAction action)
	{
		switch(kind) {
			case PadValueKind.Slider:
				return action switch {
					PadNavAction.Left => new(PadValueVerb.Step, -1),
					PadNavAction.Right => new(PadValueVerb.Step, 1),
					_ => new(PadValueVerb.None)
				};
			case PadValueKind.Popup when popupOpen:
				return action switch {
					PadNavAction.Up => new(PadValueVerb.Walk, -1),
					PadNavAction.Down => new(PadValueVerb.Walk, 1),
					PadNavAction.Confirm => new(PadValueVerb.Commit),
					PadNavAction.Back => new(PadValueVerb.Cancel),
					PadNavAction.None => new(PadValueVerb.None),
					_ => new(PadValueVerb.Consume)
				};
			case PadValueKind.Popup:
				return action == PadNavAction.Confirm ? new(PadValueVerb.Open) : new(PadValueVerb.None);
			case PadValueKind.Hold:
				return action == PadNavAction.Confirm ? new(PadValueVerb.HoldStart) : new(PadValueVerb.None);
			default:
				return new(PadValueVerb.None);
		}
	}

	//A hold lasts while Confirm stays down. A pad that no longer resolves (null
	//mapping) cannot keep one alive, or compare would stick on with nothing
	//left to release it.
	public static bool EndsHold(bool holding, IReadOnlyCollection<ushort> pressed, PadNavMapping? mapping)
	{
		return holding && (mapping is not PadNavMapping nav || !pressed.Contains(nav.Confirm));
	}

	//One SmallChange, clamped to the slider's range.
	public static double Step(double value, double smallChange, double min, double max, int delta)
	{
		return Math.Clamp(value + smallChange * delta, min, max);
	}

	//One row, stopping at both ends (no wrap, as Avalonia's own list keys). No
	//selection (-1) starts on the first row; an empty list has no row (-1).
	public static int Walk(int index, int count, int delta)
	{
		if(count <= 0) {
			return -1;
		}
		if(index < 0) {
			return 0;
		}
		return Math.Clamp(index + delta, 0, count - 1);
	}
}
