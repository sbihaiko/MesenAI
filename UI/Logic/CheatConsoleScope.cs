using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mesen.Interop;

namespace Mesen.Logic
{
	//P.10 (ADR-0245 §5, PRD Part B §13.3 rule 4): what the W-P11 Cheats sheet
	//can do per console. Only NES has a bundled list (CheatDb.Nes.json); every
	//other console gets the same sheet with manual entry only and the one-line
	//reason, and the search is shown disabled rather than hidden.
	//
	//TryParseCodes classifies a code typed into *Add a Code…* by its shape,
	//mirroring the validators in Core/Shared/CheatManager.cpp. It is a separate
	//entry point from CheatTypeDetector.FromCode on purpose: that one is the
	//parity-frozen database import (ADR-0128), which still throws for GB/SMS;
	//ADR-0245 §5 is the product decision that gives GB/SMS manual entry.
	//Host-free (UI/Logic firewall, ADR-0123).
	public static class CheatConsoleScope
	{
		public const string NoListReason = "No cheat list for this console yet";
		public const string InvalidCodeReason = "Not a code this console understands";

		private static readonly Regex NesGameGenie = new("^([APZLGITYEOXUKSVN]{6}|[APZLGITYEOXUKSVN]{8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex NesCustom = new("^[0-9A-F]{4}:[0-9A-F]{2}(:[0-9A-F]{2})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex EightHex = new("^[0-9A-F]{8}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex DashedGenie = new("^[0-9A-F]{3}-[0-9A-F]{3}(-[0-9A-F]{3})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex SnesGenie = new("^[0-9A-F]{4}-[0-9A-F]{4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		private static readonly char[] Separators = { '\r', '\n', ';', ',', '+', ' ', '\t' };

		public static bool HasCheatList(ConsoleType console) => console == ConsoleType.Nes;

		//The type the classic database import gives a bundled code (the whole
		//`;`-joined entry, as CheatListWindowViewModel passes it).
		public static CheatType DatabaseCodeType(ConsoleType console, string code) => CheatTypeDetector.FromCode(console, code);

		//Splits a stored or typed multi-part code into its upper-case parts.
		public static string[] SplitCodes(string codes)
		{
			return codes.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(c => c.ToUpperInvariant())
				.ToArray();
		}

		//Every part must parse, and all parts must be one type; the parts are
		//joined with NewLine, the same form CheatCode.Codes holds in the classic list.
		public static bool TryParseCodes(ConsoleType console, string text, out CheatType type, out string codes)
		{
			type = default;
			codes = "";
			string[] parts = SplitCodes(text);
			if(parts.Length == 0) {
				return false;
			}

			CheatType? first = null;
			foreach(string part in parts) {
				CheatType? partType = Classify(console, part);
				if(partType == null || (first != null && first != partType)) {
					return false;
				}
				first = partType;
			}

			type = first!.Value;
			codes = string.Join(Environment.NewLine, parts);
			return true;
		}

		private static CheatType? Classify(ConsoleType console, string code)
		{
			switch(console) {
				case ConsoleType.Nes:
					if(NesGameGenie.IsMatch(code)) {
						return CheatType.NesGameGenie;
					}
					if(NesCustom.IsMatch(code)) {
						return CheatType.NesCustom;
					}
					return EightHex.IsMatch(code) ? CheatType.NesProActionRocky : null;

				case ConsoleType.Gameboy:
					if(DashedGenie.IsMatch(code)) {
						return CheatType.GbGameGenie;
					}
					return EightHex.IsMatch(code) ? CheatType.GbGameShark : null;

				case ConsoleType.Sms:
					if(DashedGenie.IsMatch(code)) {
						return CheatType.SmsGameGenie;
					}
					return EightHex.IsMatch(code) ? CheatType.SmsProActionReplay : null;

				case ConsoleType.Snes:
					if(SnesGenie.IsMatch(code)) {
						return CheatType.SnesGameGenie;
					}
					return EightHex.IsMatch(code) ? CheatType.SnesProActionReplay : null;

				default:
					return null;
			}
		}
	}
}
