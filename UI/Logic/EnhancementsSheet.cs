namespace Mesen.Logic;

//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P7): the Enhancements sheet. Since
//G.4 a toggle no longer acts on its own: the five switches edit a draft, and
//the one button applies the draft and names the biggest restart it causes
//(ADR-0244 Decision 3), so the player decides when the game restarts.
//Hi-res filter is not here: it lives once, as Settings › Look › Pixels
//(ADR-0246, rule 12).

public enum EnhancementToggle
{
	Textures,
	Audio,
	Border,
	Widescreen,
	Overclock
}

public sealed record EnhancementsState(bool Textures, bool Audio, bool Border, bool Widescreen, bool Overclock);

public enum EnhancementsApplyKind
{
	//Nothing changed: the button is Done and only closes the sheet.
	None,
	//Applies with no reload: Widescreen alone (renderer-only), or a pack layer
	//change once an in-place swap keeps the player's place (P.9).
	Apply,
	//A pack layer (Textures, Audio, Border) changed: the ROM reloads.
	Reload,
	//Overclock changed: a power cycle, which loses unsaved progress.
	Restart
}

public static class EnhancementsSheet
{
	//A switch the console cannot use (Overclock on SMS) is shown disabled with
	//its reason (rule 4) and never flips.
	public static EnhancementsState Flip(EnhancementsState state, EnhancementToggle toggle, bool overclockSupported)
	{
		return toggle switch {
			EnhancementToggle.Textures => state with { Textures = !state.Textures },
			EnhancementToggle.Audio => state with { Audio = !state.Audio },
			EnhancementToggle.Border => state with { Border = !state.Border },
			EnhancementToggle.Widescreen => state with { Widescreen = !state.Widescreen },
			EnhancementToggle.Overclock => overclockSupported ? state with { Overclock = !state.Overclock } : state,
			_ => state
		};
	}

	public static bool LayersChanged(EnhancementsState applied, EnhancementsState draft)
	{
		return applied.Textures != draft.Textures || applied.Audio != draft.Audio || applied.Border != draft.Border;
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
		if(applied.Widescreen != draft.Widescreen) {
			return EnhancementsApplyKind.Apply;
		}
		return EnhancementsApplyKind.None;
	}
}
