namespace Mesen.Logic;

//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P7): the Enhancements sheet. Since
//G.4 a toggle no longer acts on its own: the four switches edit a draft, and
//the one button applies the draft and names the biggest restart it causes
//(ADR-0244 Decision 3), so the player decides when the game restarts.
//One place per switch (ADR-0250 amendment 2026-10-03): the pack's layers
//(Textures, Music) are W-P6's, per game; this sheet keeps Modern instruments
//(the synth, applied live), Border, Widescreen and Overclock.
//Hi-res filter is not here: it lives once, as Settings › Look › Pixels
//(ADR-0246, rule 12).

public enum EnhancementToggle
{
	ModernInstruments,
	Border,
	Widescreen,
	Overclock
}

public sealed record EnhancementsState(bool ModernInstruments, bool Border, bool Widescreen, bool Overclock);

public enum EnhancementsApplyKind
{
	//Nothing changed: the button is Done and only closes the sheet.
	None,
	//Applies with no reload: Widescreen alone (renderer-only), or a pack layer
	//change once an in-place swap keeps the player's place (P.9).
	Apply,
	//The Border pack layer changed: the ROM reloads.
	Reload,
	//Overclock changed: a power cycle, which loses unsaved progress.
	Restart
}

//How the panel was left, which decides what the next open shows. Only the
//button applies the draft (ADR-0244 Decision 3), so a detour that is not a
//decision about the switches must not throw the flips away.
public enum EnhancementsDraftExit
{
	//Esc, the button itself, the game changing: the visit is over, and the
	//next open reads the switches from what is applied.
	Closed,
	//The Pack row: W-P5/W-P6 open over the panel to look at the pack, and the
	//player comes back to the switches already flipped.
	PackRow
}

public static class EnhancementsDraftVisit
{
	//Only the Pack row's detour carries the draft: looking at the pack is not
	//a decision about the switches.
	public static bool Holds(EnhancementsDraftExit exit) => exit == EnhancementsDraftExit.PackRow;
}

public static class EnhancementsSheet
{
	//A switch the console cannot use is shown disabled with its reason (rule 4)
	//and never flips: Overclock on SMS, and Widescreen on a game with nothing
	//beside the picture (ADR-0253 §4, W.5).
	public static EnhancementsState Flip(EnhancementsState state, EnhancementToggle toggle, bool overclockSupported, bool widescreenSupported = true)
	{
		return toggle switch {
			EnhancementToggle.ModernInstruments => state with { ModernInstruments = !state.ModernInstruments },
			EnhancementToggle.Border => state with { Border = !state.Border },
			EnhancementToggle.Widescreen => widescreenSupported ? state with { Widescreen = !state.Widescreen } : state,
			EnhancementToggle.Overclock => overclockSupported ? state with { Overclock = !state.Overclock } : state,
			_ => state
		};
	}

	public static bool LayersChanged(EnhancementsState applied, EnhancementsState draft)
	{
		return applied.Border != draft.Border;
	}

	//The way back from the Pack row (EnhancementsDraftVisit.Holds): a switch the
	//player flipped keeps the value they set, and a switch they left alone
	//follows what is applied now - so a switch turned elsewhere during the
	//detour (Settings › Audio's synth, Look's widescreen) is shown as it is,
	//never stale, and a flip that now matches what is applied is not left
	//looking like a change still pending.
	public static EnhancementsState Resume(EnhancementsState appliedAtLeave, EnhancementsState draftAtLeave, EnhancementsState appliedNow)
	{
		return new EnhancementsState(
			Flipped(appliedAtLeave.ModernInstruments, draftAtLeave.ModernInstruments, appliedNow.ModernInstruments),
			Flipped(appliedAtLeave.Border, draftAtLeave.Border, appliedNow.Border),
			Flipped(appliedAtLeave.Widescreen, draftAtLeave.Widescreen, appliedNow.Widescreen),
			Flipped(appliedAtLeave.Overclock, draftAtLeave.Overclock, appliedNow.Overclock));
	}

	private static bool Flipped(bool appliedAtLeave, bool draftAtLeave, bool appliedNow)
	{
		return appliedAtLeave != draftAtLeave ? draftAtLeave : appliedNow;
	}

	//layerChangeKeepsPlace: whether a pack layer change can apply in place
	//(ADR-0244, slice P.9, through its refusal rules - a ROM patch, a movie,
	//a shared replay or netplay keep the reload). False until P.9 ships.
	public static EnhancementsApplyKind Pending(EnhancementsState applied, EnhancementsState draft, bool layerChangeKeepsPlace)
	{
		if(applied.Overclock != draft.Overclock) {
			return EnhancementsApplyKind.Restart;
		}
		if(LayersChanged(applied, draft)) {
			return layerChangeKeepsPlace ? EnhancementsApplyKind.Apply : EnhancementsApplyKind.Reload;
		}
		//Widescreen is renderer-only and Modern instruments is the live synth
		//config: neither reloads.
		if(applied.Widescreen != draft.Widescreen || applied.ModernInstruments != draft.ModernInstruments) {
			return EnhancementsApplyKind.Apply;
		}
		return EnhancementsApplyKind.None;
	}
}
