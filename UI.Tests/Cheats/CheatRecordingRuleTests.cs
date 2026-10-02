using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//P.10 (ADR-0245 §3, ADR-0184 §1): while recording art only RAM codes are
	//allowed. A NES RAM code is the `AAAA:VV[:CC]` form with AAAA below 0x0800
	//(the NES internal RAM); Game Genie and Pro Action Rocky always patch PRG.
	public class CheatRecordingRuleTests
	{
		[Theory]
		[InlineData(CheatType.NesCustom, "00B0:FF", true)]
		[InlineData(CheatType.NesCustom, "07FF:01:00", true)]
		[InlineData(CheatType.NesCustom, "0032:03\n0033:04", true)]
		[InlineData(CheatType.NesCustom, "0800:01", false)]
		[InlineData(CheatType.NesCustom, "6000:01", false)]
		[InlineData(CheatType.NesCustom, "0032:03\nC000:04", false)]
		[InlineData(CheatType.NesCustom, "zz:01", false)]
		[InlineData(CheatType.NesGameGenie, "SXIOPO", false)]
		[InlineData(CheatType.NesProActionRocky, "00B0FF00", false)]
		[InlineData(CheatType.GbGameShark, "01FF16D0", true)]
		[InlineData(CheatType.GbGameGenie, "00A-17B-C49", false)]
		[InlineData(CheatType.SmsProActionReplay, "00C0FF09", true)]
		[InlineData(CheatType.SmsGameGenie, "00A-17B-C49", false)]
		public void Only_ram_codes_are_allowed_while_recording_art(CheatType type, string codes, bool allowed)
		{
			Assert.Equal(allowed, CheatRecordingRule.IsRamCode(type, codes.Replace("\n", Environment.NewLine)));
		}

		private static readonly CheatDbGame Contra = new("Contra (USA)", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", new[] {
			new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
			new CheatDbCode("Invincibility (star effect)", "00B0:FF"),
		});

		[Fact]
		public void While_recording_art_a_game_genie_row_is_disabled_with_its_reason()
		{
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), recordingArt: true, "");

			CheatSheetRow genie = rows[0];
			Assert.False(genie.CanToggle);
			Assert.Equal("Changes the game itself — not allowed while recording art", genie.Note);

			CheatSheetRow ram = rows[1];
			Assert.True(ram.CanToggle);
			Assert.Equal("allowed while recording art", ram.Note);
		}

		[Fact]
		public void Outside_a_recording_every_row_is_available_and_unmarked()
		{
			IReadOnlyList<CheatSheetRow> rows = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), recordingArt: false, "");

			Assert.All(rows, r => Assert.True(r.CanToggle));
			Assert.All(rows, r => Assert.Equal("", r.Note));
		}

		[Fact]
		public void A_refused_row_does_not_turn_on_even_when_toggled()
		{
			CheatSheetRow genie = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, Array.Empty<StoredCheat>(), recordingArt: true, "")[0];

			Assert.Empty(CheatSheet.Toggle(Array.Empty<StoredCheat>(), genie, recordingArt: true));
		}

		[Fact]
		public void A_game_genie_code_already_on_can_still_be_turned_off_while_recording_art()
		{
			IReadOnlyList<StoredCheat> stored = new[] { new StoredCheat("Infinite lives - 1P game", CheatType.NesGameGenie, "SZKGPAVG", true) };
			CheatSheetRow genie = CheatSheet.BuildRows(ConsoleType.Nes, Contra, false, stored, recordingArt: true, "")[0];

			Assert.True(genie.IsOn);
			Assert.True(genie.CanToggle);
			Assert.Equal(CheatRecordingRule.RefusedReason, genie.Note);
			Assert.False(Assert.Single(CheatSheet.Toggle(stored, genie, recordingArt: true)).Enabled);
		}

		[Fact]
		public void A_game_genie_code_typed_while_recording_art_is_refused()
		{
			(IReadOnlyList<StoredCheat> stored, string error) = CheatSheet.AddCode(Array.Empty<StoredCheat>(), ConsoleType.Nes, "", "SXIOPO", recordingArt: true);

			Assert.Empty(stored);
			Assert.Equal(CheatRecordingRule.RefusedReason, error);

			(stored, error) = CheatSheet.AddCode(Array.Empty<StoredCheat>(), ConsoleType.Nes, "Lives", "0032:09", recordingArt: true);
			Assert.Equal("", error);
			Assert.Equal(CheatType.NesCustom, Assert.Single(stored).Type);
		}
	}
}
