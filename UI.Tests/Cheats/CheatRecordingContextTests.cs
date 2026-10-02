using System;
using System.Collections.Generic;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Cheats
{
	//ADR-0245 §3 and ADR-0184 §1 in the Remaster context: where the rule
	//applies, which cheats already on a Remaster recording refuses to start
	//with (named, ADR-0184 §1 "MUST name the offending code"), and which ones
	//are held back from the core while it records.
	public class CheatRecordingContextTests
	{
		private static readonly StoredCheat Genie = new("Infinite lives - 1P game", CheatType.NesGameGenie, "SZKGPAVG", true);
		private static readonly StoredCheat PrgCustom = new("Weapon", CheatType.NesCustom, "C000:04", true);
		private static readonly StoredCheat Lives = new("Lives", CheatType.NesCustom, "0032:09", true);
		private static readonly StoredCheat GenieOff = new("Jump", CheatType.NesGameGenie, "SXIOPO", false);

		[Theory]
		[InlineData(false, false, false)]
		[InlineData(true, false, true)]
		[InlineData(false, true, true)]
		[InlineData(true, true, true)]
		public void Art_is_being_recorded_in_remaster_or_while_a_remaster_recording_runs(bool remasterActive, bool remasterRecording, bool expected)
		{
			Assert.Equal(expected, CheatRecordingRule.IsRecordingArtContext(remasterActive, remasterRecording));
		}

		[Fact]
		public void A_recording_refuses_the_enabled_codes_that_are_not_ram_codes()
		{
			IReadOnlyList<StoredCheat> refused = CheatRecordingRule.Refused(new[] { Genie, Lives, PrgCustom, GenieOff }, disableAll: false);

			Assert.Equal(new[] { Genie, PrgCustom }, refused);
		}

		[Fact]
		public void Nothing_is_refused_when_every_cheat_is_switched_off()
		{
			Assert.Empty(CheatRecordingRule.Refused(new[] { Genie, PrgCustom }, disableAll: true));
		}

		[Fact]
		public void Ram_codes_alone_never_refuse_a_recording()
		{
			Assert.Empty(CheatRecordingRule.Refused(new[] { Lives, GenieOff }, disableAll: false));
		}

		[Fact]
		public void The_refusal_names_each_offending_code_with_its_description()
		{
			StoredCheat twoParts = new("Two", CheatType.NesGameGenie, "SXIOPO" + Environment.NewLine + "AAAAAA", true);

			Assert.Equal("Infinite lives - 1P game (SZKGPAVG), Two (SXIOPO, AAAAAA)", CheatRecordingRule.Names(new[] { Genie, twoParts }));
		}
	}
}
