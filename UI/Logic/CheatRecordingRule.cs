using System;
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
	}
}
