using System;
using System.Collections.Generic;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//P.10 (ADR-0245 §5, rule 4): only NES has a bundled cheat list. GB/SMS
	//still get the sheet, with manual entry only and a one-line reason; the
	//search is the one element that cannot work there, shown disabled.
	public class CheatConsoleScopeTests
	{
		[Theory]
		[InlineData(ConsoleType.Nes, true)]
		[InlineData(ConsoleType.Gameboy, false)]
		[InlineData(ConsoleType.Sms, false)]
		[InlineData(ConsoleType.Gba, false)]
		public void Only_nes_has_a_cheat_list(ConsoleType console, bool hasList)
		{
			Assert.Equal(hasList, CheatConsoleScope.HasCheatList(console));
		}

		[Theory]
		[InlineData(ConsoleType.Gameboy)]
		[InlineData(ConsoleType.Sms)]
		public void Gb_and_sms_say_why_the_list_is_missing(ConsoleType console)
		{
			Assert.Equal("No cheat list for this console yet", CheatSheet.StatusLine(console, null, false, 0));
			Assert.Equal(CheatConsoleScope.NoListReason, CheatSheet.StatusLine(console, null, false, 3));
		}

		[Fact]
		public void Gb_and_sms_rows_are_only_the_codes_you_added()
		{
			IReadOnlyList<StoredCheat> stored = new[] { new StoredCheat("Infinite health", CheatType.GbGameShark, "01FF16D0", true) };

			CheatSheetRow row = Assert.Single(CheatSheet.BuildRows(ConsoleType.Gameboy, null, false, stored, false, ""));
			Assert.Equal(CheatRowSource.Yours, row.Source);
			Assert.True(row.IsOn);
		}

		[Theory]
		[InlineData(ConsoleType.Nes, "SXIOPO", CheatType.NesGameGenie)]
		[InlineData(ConsoleType.Nes, "aeusgzap", CheatType.NesGameGenie)]
		[InlineData(ConsoleType.Nes, "0032:09", CheatType.NesCustom)]
		[InlineData(ConsoleType.Nes, "0032:09:03", CheatType.NesCustom)]
		[InlineData(ConsoleType.Nes, "00B0FF00", CheatType.NesProActionRocky)]
		[InlineData(ConsoleType.Gameboy, "00A-17B-C49", CheatType.GbGameGenie)]
		[InlineData(ConsoleType.Gameboy, "01FF16D0", CheatType.GbGameShark)]
		[InlineData(ConsoleType.Sms, "00A-17B", CheatType.SmsGameGenie)]
		[InlineData(ConsoleType.Sms, "00C0FF09", CheatType.SmsProActionReplay)]
		public void A_typed_code_is_classified_per_console(ConsoleType console, string code, CheatType expected)
		{
			Assert.True(CheatConsoleScope.TryParseCodes(console, code, out CheatType type, out _));
			Assert.Equal(expected, type);
		}

		[Fact]
		public void Several_codes_of_one_type_are_joined_like_the_classic_list()
		{
			Assert.True(CheatConsoleScope.TryParseCodes(ConsoleType.Nes, " aauzgzap ; PEUZTZTP ", out CheatType type, out string codes));
			Assert.Equal(CheatType.NesGameGenie, type);
			Assert.Equal("AAUZGZAP" + Environment.NewLine + "PEUZTZTP", codes);
		}

		[Theory]
		[InlineData(ConsoleType.Nes, "")]
		[InlineData(ConsoleType.Nes, "SXIOP")]
		[InlineData(ConsoleType.Nes, "SXIOPO 0032:09")]
		[InlineData(ConsoleType.Gameboy, "SXIOPO")]
		[InlineData(ConsoleType.Sms, "0032:09")]
		[InlineData(ConsoleType.Gba, "01FF16D0")]
		public void Malformed_mixed_or_unsupported_codes_are_refused(ConsoleType console, string code)
		{
			Assert.False(CheatConsoleScope.TryParseCodes(console, code, out _, out _));
		}

		[Fact]
		public void Database_codes_are_typed_like_the_classic_import()
		{
			Assert.Equal(CheatType.NesGameGenie, CheatConsoleScope.DatabaseCodeType(ConsoleType.Nes, "SZKGPAVG"));
			Assert.Equal(CheatType.NesCustom, CheatConsoleScope.DatabaseCodeType(ConsoleType.Nes, "0032:03;0033:03"));
		}
	}
}
