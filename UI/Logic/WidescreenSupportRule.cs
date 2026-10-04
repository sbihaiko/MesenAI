using Mesen.Interop;

namespace Mesen.Logic;

//ADR-0253 §4 (slice W.5): the Enhancements sheet's Widescreen switch is the
//only control for the automatic mode. It is shown disabled, with a one-line
//reason, when the loaded game cannot use any widescreen mode at all - a console
//with no side map, or a game the core measured with nothing beside the picture
//(NesWidescreenSupport::Probe) and whose pack ships no widescreen art (W.3's
//`widescreen` section, §1's Pack-art mode). While that measurement is still
//running the switch stays enabled. The app keeps the per-ROM answer
//(PlayerEnhancementsConfig.RomWidescreenSupport); this class is the host-free
//rule between the core's verdict and the switch's state.
//
//Host-free (BCL + the already-dual-compiled ConsoleType, ADR-0123) so UI.Tests
//exercises it without Avalonia/EmuApi.
public enum WidescreenSupport
{
	//Not measured yet (or the core is still measuring): the switch is usable.
	Unknown = 0,
	//Mirrors NesWidescreenSupport::Verdict's values.
	Supported = 1,
	//The measurement window went by with nothing beside the picture.
	Unsupported = 2
}

//The switch's realized state: whether it can be turned on, and the resource id
//of the one-line reason a disabled switch shows ("" when it is enabled). The
//owning ViewModel resolves the id with ResourceHelper, the way it does for the
//Overclock reason.
public sealed record WidescreenSwitchState(bool Enabled, string ReasonKey);

public static class WidescreenSupportRule
{
	//ADR-0253 §2, the per-console Reveal scope: NES, GB/GBC/GG and GBA have a
	//background map beside the picture; SMS/SG-1000 do not (their name table is
	//exactly as wide as the screen and wraps onto itself), so only pack art can
	//serve them. ConsoleType.Sms covers SMS, SG-1000 and Game Gear, and the
	//Game Gear's 160-px screen is a window on the same 256-px map the SMS uses,
	//so the caller passes whether the loaded ROM is a Game Gear one.
	public static bool ConsoleHasSideMap(ConsoleType console, bool gameGear)
	{
		return console switch {
			ConsoleType.Nes => true,
			ConsoleType.Gameboy => true,
			ConsoleType.Gba => true,
			ConsoleType.Sms => gameGear,
			_ => false
		};
	}

	//hasWidescreenPackArt: ADR-0253 §3's first fallback source. Installing a
	//pack with widescreen art gives the game a mode of its own and re-enables
	//the switch, which is why the MEP <widescreen> section (W.3) can clear a
	//recorded "unsupported". It comes from the core (EmuApi.HasWidescreenPackArt),
	//which resolves the same winning pack the renderer's decode draws from.
	public static WidescreenSwitchState Switch(bool consoleHasSideMap, WidescreenSupport measured, bool hasWidescreenPackArt)
	{
		if(hasWidescreenPackArt) {
			return Enabled;
		}
		if(measured == WidescreenSupport.Unsupported || !consoleHasSideMap) {
			return new WidescreenSwitchState(false, UnavailableReasonKey);
		}
		return Enabled;
	}

	//ADR-0253 §3 x §4 (the W.3 x W.5 seam): the switch's state for the loaded
	//game, from the three answers the core gives about it - the console's own
	//side map (§2), what the measurement settled (§4, W.5) and whether a pack
	//shipping widescreen art is loaded (§3, W.3). The measurement only ever read
	//the game's own map, so the art overrules an "unsupported": the game is
	//widened in Pack-art mode instead. Keeping the three inputs together here is
	//what makes the seam host-free testable - the ViewModel is a pass-through of
	//the core's answers (EmuApi.GetWidescreenSupportVerdict / HasWidescreenPackArt).
	public static WidescreenSwitchState SwitchForLoadedGame(ConsoleType console, bool gameGear, WidescreenSupport measured, bool hasWidescreenPackArt)
	{
		return Switch(ConsoleHasSideMap(console, gameGear), measured, hasWidescreenPackArt);
	}

	//ADR-0253 §4: the core's measurement closes after the first gameplay
	//seconds, and the answer is written as it closes - the window's tick hands
	//it here while a game runs, so the per-ROM record does not depend on the
	//player ever opening the Enhancements sheet. Undecided means the window is
	//still open; a terminal verdict is written once per run.
	public static bool ShouldRecord(WidescreenSupport measured, bool alreadyRecorded)
	{
		return measured != WidescreenSupport.Unknown && !alreadyRecorded;
	}

	//ADR-0253 §1/§4: whether widescreen is on for the loaded game. A game that
	//cannot use it is off whatever the saved preference says, but the
	//preference itself is never written off - §1's "the switch keeps its saved
	//value, so the next game that supports it gets it back" (which is why this
	//is not the Overclock treatment, whose setting is per console).
	public static bool EffectiveWidescreen(bool savedOn, WidescreenSwitchState state)
	{
		return savedOn && state.Enabled;
	}

	//ADR-0253 §4: a game the core recorded as unsupported says so once, when it
	//loads - the same one-line reason the disabled switch shows. calledFor is
	//the ROM the session already announced ("" for none), which is what keeps a
	//second load of the same game quiet while still letting the next game speak.
	//hasWidescreenPackArt silences it (ADR-0253 §4's "installing a pack with
	//widescreen art re-enables the switch"): the record was made without the
	//pack, and this game now has a mode, so the disabled-switch sentence would
	//contradict the switch right beside it.
	public static bool ShouldAnnounceUnavailable(string romSha1, bool rememberedUnsupported, string alreadyAnnouncedFor, bool hasWidescreenPackArt)
	{
		return rememberedUnsupported
			&& !hasWidescreenPackArt
			&& !string.IsNullOrEmpty(romSha1)
			&& romSha1 != alreadyAnnouncedFor;
	}

	//"EnhancementsWidescreenUnavailable" (resources.en.xml).
	public const string UnavailableReasonKey = "EnhancementsWidescreenUnavailable";

	private static readonly WidescreenSwitchState Enabled = new(true, "");
}
