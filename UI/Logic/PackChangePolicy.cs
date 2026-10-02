using Mesen.Interop;

namespace Mesen.Logic
{
	//ADR-0244 (P.9): the byte EmuApi.ReloadRomKeepingState returns. Mirrors
	//Core's InPlaceReloadResult (Core/Shared/Emulator.h) value for value.
	public enum InPlaceReloadResult : byte
	{
		//The ROM was reloaded and the player's place restored.
		Restored = 0,
		//Reloaded, but the state did not load back: the game restarted.
		Restarted = 1,
		//Nothing done: no game, a movie playing or recording, or netplay.
		Refused = 2,
		//Reloaded fresh, no state kept: a pack's ROM patch on either side.
		PatchRestarted = 3,
	}

	public enum PackChangeRoute
	{
		//Save state to memory, reload, load the state back (ReloadRomKeepingState).
		InPlace,
		//The pre-ADR-0244 path: the game reloads from power-on.
		Restart,
	}

	//What a pack change does, and the HUD message (a Core MessageManager key,
	//shown through EmuApi.DisplayMessage("MEP", key)) that says why, or null.
	public readonly record struct PackChangePlan(PackChangeRoute Route, string? NoticeKey);

	//What the caller does once ReloadRomKeepingState has answered: whether it
	//still has to restart the game itself, and the message to show.
	public readonly record struct PackChangeOutcome(bool RestartNeeded, string? NoticeKey);

	//ADR-0244 sections 2-3: which pack changes keep the player's place.
	//
	//The pack switches the player drives (Textures/Audio/Border in the
	//Enhancements panel, and a pack chosen in the picker) take the in-place
	//path on the consoles where scripts/pack_swap_exactness.py measured every
	//one of those transitions exact (the per-transition table is in ADR-0244's
	//Status line). Everything else keeps the restart:
	//  - a movie playing or recording, a shared-replay recording (a movie too)
	//    or netplay - "Not while recording" / "Not during netplay";
	//  - a pack whose ROM patch applies before or after the change - Core
	//    detects that (only it knows whether a patch applied) and answers
	//    PatchRestarted, "This pack changes the game itself - it restarts";
	//  - a console the harness did not measure - silently, as before.
	//Overclock is not a pack change and keeps its power cycle (ADR-0244
	//Non-goals); it never goes through here.
	//
	//Host-free (BCL + the dual-compiled Mesen.Interop.ConsoleType, ADR-0123):
	//the caller reads the movie/netplay state and calls the native export.
	public static class PackChangePolicy
	{
		public const string ChangedKey = "MepPackChangedInPlace";
		public const string FallbackRestartKey = "MepPackChangeRestarted";
		public const string PatchRestartKey = "MepPackChangePatchRestart";
		public const string RecordingKey = "MepPackChangeNotWhileRecording";
		public const string NetplayKey = "MepPackChangeNotDuringNetplay";

		//The consoles scripts/pack_swap_exactness.py measured exact on every
		//transition it runs there. Gameboy covers GB and GBC (one ConsoleType).
		public static bool IsMeasured(ConsoleType console)
		{
			return console switch {
				ConsoleType.Nes => true,
				ConsoleType.Sms => true,
				ConsoleType.Gameboy => true,
				_ => false
			};
		}

		public static PackChangePlan Plan(ConsoleType console, bool movieActive, bool netplayActive)
		{
			if(netplayActive) {
				return new PackChangePlan(PackChangeRoute.Restart, NetplayKey);
			}
			if(movieActive) {
				return new PackChangePlan(PackChangeRoute.Restart, RecordingKey);
			}
			if(!IsMeasured(console)) {
				return new PackChangePlan(PackChangeRoute.Restart, null);
			}
			return new PackChangePlan(PackChangeRoute.InPlace, null);
		}

		//Refused means the state changed between Plan and the call (a movie or
		//netplay started meanwhile, or the game closed): nothing was done, so
		//the caller falls back to the plain restart, as before ADR-0244.
		public static PackChangeOutcome Outcome(InPlaceReloadResult result)
		{
			return result switch {
				InPlaceReloadResult.Restored => new PackChangeOutcome(false, ChangedKey),
				InPlaceReloadResult.Restarted => new PackChangeOutcome(false, FallbackRestartKey),
				InPlaceReloadResult.PatchRestarted => new PackChangeOutcome(false, PatchRestartKey),
				_ => new PackChangeOutcome(true, null)
			};
		}
	}
}
