using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Mesen.Interop;

namespace Mesen.Logic
{
	//P.10 (ADR-0245 §3, ADR-0184 §1): which cheats stay available while art is
	//being recorded. Only a RAM code may: a NES `AAAA:VV[:CC]` code
	//(CheatType.NesCustom) whose every address is below 0x0800, the NES
	//internal RAM, plus the console RAM-write types the Core itself marks
	//IsRamCode (GB GameShark, SMS Pro Action Replay). A NesCustom code with
	//a higher address is refused like a Game Genie code - ADR-0184 requires
	//both checks, because the custom form can also carry a PRG address.
	//Game Genie and Pro Action Rocky always patch what the CPU reads from PRG
	//ROM, which can corrupt the recorded art.
	//
	//Host-free (UI/Logic firewall, ADR-0123); the sheet's ViewModel feeds it
	//the "recording art" flag of its context.
	public static class CheatRecordingRule
	{
		public const string RefusedReason = "Changes the game itself — not allowed while recording art";
		public const string AllowedNote = "allowed while recording art";

		private const int NesInternalRamEnd = 0x0800;

		public static bool IsRamCode(CheatType type, string codes)
		{
			switch(type) {
				case CheatType.NesCustom:
					string[] parts = CheatConsoleScope.SplitCodes(codes);
					if(parts.Length == 0) {
						return false;
					}
					foreach(string code in parts) {
						int colon = code.IndexOf(':');
						if(colon <= 0 || !int.TryParse(code.AsSpan(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int address) || address >= NesInternalRamEnd) {
							return false;
						}
					}
					return true;

				case CheatType.GbGameShark:
				case CheatType.SmsProActionReplay:
					return true;

				default:
					return false;
			}
		}

		//The Remaster context (ADR-0245 §3): Remaster is the active workspace,
		//or a Remaster recording runs - switching profile never stops one
		//(PRD Part B §13.6), so Play over a running recording is recording art too.
		public static bool IsRecordingArtContext(bool remasterActive, bool remasterRecording)
		{
			return remasterActive || remasterRecording;
		}

		//User decision 2026-10-03 (ADR-0245 amendment, "Liberar e pausar gravação
		//(Recomendado)"): Play's passive automatic bootstrap never locks a cheat.
		//A code that changes the game (not a RAM code) turned on while it runs
		//stops it - what it saved stays, nothing corrupted enters the art - and
		//the core does not restart it until the next ROM load. A recording the
		//user started in Remaster (recordingArt) is not stopped: it holds the
		//code back instead.
		public static bool PausesPassiveBootstrap(IEnumerable<StoredCheat> stored, bool recordingArt, bool bootstrapping)
		{
			return bootstrapping && !recordingArt && Refused(stored, disableAll: false).Count > 0;
		}

		//The cheats that would reach the core and are not RAM codes: a Remaster
		//recording refuses to start while any is on (ADR-0184 §1: refuse, not
		//warn), and while one records they are held back from the core. None
		//when every cheat is switched off (CheatWindowConfig.DisableAllCheats).
		public static IReadOnlyList<StoredCheat> Refused(IEnumerable<StoredCheat> stored, bool disableAll)
		{
			if(disableAll) {
				return Array.Empty<StoredCheat>();
			}
			return stored.Where(c => c.Enabled && !IsRamCode(c.Type, c.Codes)).ToList();
		}

		//ADR-0184 §1: the refusal names each offending code - "description (code, code)".
		public static string Names(IEnumerable<StoredCheat> refused)
		{
			return string.Join(", ", refused.Select(c => c.Description + " (" + string.Join(", ", CheatConsoleScope.SplitCodes(c.Codes)) + ")"));
		}
	}
}
