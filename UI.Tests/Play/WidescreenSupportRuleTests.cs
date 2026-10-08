using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0253 §4 (slice W.5): the Enhancements sheet's Widescreen switch is the
	//only control for the automatic mode, and it is shown disabled with a
	//one-line reason when the loaded game cannot use any widescreen mode.
	//ADR-0267 stage 1 (option B) narrows that: a console with no side map is not
	//one of those games any more - its switch stays enabled and applies the
	//Widescreen fill, with the reason reworded onto that fill.
	public class WidescreenSupportRuleTests
	{
		private const string Sha1 = "0000000000000000000000000000000000000000";
		private static readonly string Reason = WidescreenSupportRule.UnavailableReasonKey;

		[Fact]
		public void A_console_with_a_side_map_keeps_the_switch_on_until_measured()
		{
			WidescreenSwitchState state = WidescreenSupportRule.Switch(consoleHasSideMap: true, WidescreenSupport.Unknown, hasWidescreenPackArt: false);
			Assert.True(state.Enabled);
			Assert.Equal("", state.ReasonKey);
		}

		[Fact]
		public void A_game_measured_with_nothing_beside_the_picture_is_disabled_with_its_reason()
		{
			WidescreenSwitchState state = WidescreenSupportRule.Switch(consoleHasSideMap: true, WidescreenSupport.Unsupported, hasWidescreenPackArt: false);
			Assert.False(state.Enabled);
			Assert.Equal(Reason, state.ReasonKey);
		}

		[Fact]
		public void A_console_with_no_side_map_keeps_the_switch_enabled_and_fills()
		{
			//ADR-0267 stage 1 (option B): SMS/SG-1000 have nothing beside the
			//picture, so there is no Reveal to offer - but the switch is not
			//disabled for that. Turning it on applies the Widescreen fill
			//(AspectRatioMath's 16:9, the pre-ADR-0253 behaviour), and the
			//one-line reason names the fill instead of claiming a Reveal.
			//ADR-0253 §4's "disabled at once" is what B amends.
			WidescreenSwitchState state = WidescreenSupportRule.Switch(consoleHasSideMap: false, WidescreenSupport.Unknown, false);
			Assert.True(state.Enabled);
			Assert.Equal(Reason, state.ReasonKey);
			//The fill is a real mode: what the player turns on is applied, and
			//the saved preference still decides whether it is on.
			Assert.True(WidescreenSupportRule.EffectiveWidescreen(savedOn: true, state));
			Assert.False(WidescreenSupportRule.EffectiveWidescreen(savedOn: false, state));
		}

		[Fact]
		public void An_sms_game_shows_the_switch_enabled_with_the_fill_reason()
		{
			//The per-console state the sheet reads (SwitchForLoadedGame), not
			//just the boolean: an SMS game - the console of the report - keeps
			//its switch usable, with the fill sentence under it.
			WidescreenSwitchState state = WidescreenSupportRule.SwitchForLoadedGame(
				ConsoleType.Sms, gameGear: false, WidescreenSupport.Unknown, hasWidescreenPackArt: false);
			Assert.True(state.Enabled);
			Assert.Equal(Reason, state.ReasonKey);

			//A Game Gear keeps the same console's switch but reveals for real
			//(§2: its 160-px screen is a window on the same map), so the fill
			//sentence is not shown over it.
			Assert.Equal("", WidescreenSupportRule.SwitchForLoadedGame(
				ConsoleType.Sms, gameGear: true, WidescreenSupport.Unknown, hasWidescreenPackArt: false).ReasonKey);
		}

		[Fact]
		public void Pack_art_enables_the_switch_even_where_the_console_cannot_reveal()
		{
			//ADR-0253 §3/§4: widescreen pack art is a mode of its own, and
			//installing one re-enables the switch. It is a real Reveal, so the
			//fill sentence is not shown over it (ADR-0267 stage 1).
			WidescreenSwitchState state = WidescreenSupportRule.Switch(consoleHasSideMap: false, WidescreenSupport.Unsupported, hasWidescreenPackArt: true);
			Assert.True(state.Enabled);
			Assert.Equal("", state.ReasonKey);
		}

		[Fact]
		public void A_later_run_that_finds_content_clears_the_disabled_state()
		{
			//The core keeps a positive forever (§4's re-check), so a Supported
			//verdict is simply not remembered as unsupported.
			Assert.True(WidescreenSupportRule.Switch(true, WidescreenSupport.Supported, false).Enabled);
		}

		[Fact]
		public void A_game_measured_unsupported_is_widened_when_its_pack_ships_widescreen_art()
		{
			//ADR-0253 §3 × §4 (the W.3 × W.5 seam): the measurement only ever read
			//the game's own map, so a pack shipping widescreen art is a mode of its
			//own and overrules a settled "unsupported" - the switch stays enabled
			//and the game is widened, its sides showing the pack's art.
			WidescreenSwitchState state = WidescreenSupportRule.SwitchForLoadedGame(
				ConsoleType.Nes, gameGear: false, WidescreenSupport.Unsupported, hasWidescreenPackArt: true);
			Assert.True(state.Enabled);
			Assert.Equal("", state.ReasonKey);
			Assert.True(WidescreenSupportRule.EffectiveWidescreen(savedOn: true, state));

			//The same recorded game without the art is the disabled switch W.5 ships.
			Assert.False(WidescreenSupportRule.SwitchForLoadedGame(
				ConsoleType.Nes, gameGear: false, WidescreenSupport.Unsupported, hasWidescreenPackArt: false).Enabled);
		}

		[Fact]
		public void A_recorded_game_announces_its_reason_once_per_session()
		{
			//ADR-0253 §4: "the switch then shows disabled from the next load,
			//and a toast says so once". Once per load of that ROM, not on every
			//game start and not on every sheet open.
			Assert.True(WidescreenSupportRule.ShouldAnnounceUnavailable(Sha1, rememberedUnsupported: true, alreadyAnnouncedFor: "", hasWidescreenPackArt: false));
			Assert.False(WidescreenSupportRule.ShouldAnnounceUnavailable(Sha1, rememberedUnsupported: true, alreadyAnnouncedFor: Sha1, hasWidescreenPackArt: false));
		}

		[Fact]
		public void Only_a_game_recorded_as_unsupported_announces_anything()
		{
			Assert.False(WidescreenSupportRule.ShouldAnnounceUnavailable(Sha1, rememberedUnsupported: false, alreadyAnnouncedFor: "", hasWidescreenPackArt: false));
			//No ROM loaded: nothing to say, and nothing to key the memory on.
			Assert.False(WidescreenSupportRule.ShouldAnnounceUnavailable("", rememberedUnsupported: true, alreadyAnnouncedFor: "", hasWidescreenPackArt: false));
			//A second game still gets its own notice.
			Assert.True(WidescreenSupportRule.ShouldAnnounceUnavailable("bbbb", rememberedUnsupported: true, alreadyAnnouncedFor: Sha1, hasWidescreenPackArt: false));
		}

		[Fact]
		public void A_pack_with_widescreen_art_silences_the_unavailable_notice()
		{
			//ADR-0253 §4: "installing a pack with widescreen art re-enables the
			//switch" - so the toast saying the game has no widescreen mode must not
			//fire over a game the loaded pack just gave one, or the disabled-switch
			//sentence would contradict the switch right beside it.
			Assert.False(WidescreenSupportRule.ShouldAnnounceUnavailable(Sha1, rememberedUnsupported: true, alreadyAnnouncedFor: "", hasWidescreenPackArt: true));
			Assert.True(WidescreenSupportRule.ShouldAnnounceUnavailable(Sha1, rememberedUnsupported: true, alreadyAnnouncedFor: "", hasWidescreenPackArt: false));
		}

		[Fact]
		public void A_terminal_verdict_is_recorded_when_the_window_closes()
		{
			//ADR-0253 §4: the answer has to be written as the window closes, not
			//the next time a sheet happens to open - otherwise a player who never
			//opens Enhancements never gets the record, and the switch never comes
			//up disabled.
			Assert.False(WidescreenSupportRule.ShouldRecord(WidescreenSupport.Unknown, alreadyRecorded: false));
			Assert.True(WidescreenSupportRule.ShouldRecord(WidescreenSupport.Supported, alreadyRecorded: false));
			Assert.True(WidescreenSupportRule.ShouldRecord(WidescreenSupport.Unsupported, alreadyRecorded: false));
			//Once per run: the window closes once.
			Assert.False(WidescreenSupportRule.ShouldRecord(WidescreenSupport.Unsupported, alreadyRecorded: true));
			Assert.False(WidescreenSupportRule.ShouldRecord(WidescreenSupport.Supported, alreadyRecorded: true));
		}

		[Fact]
		public void A_game_that_cannot_use_widescreen_is_off_without_losing_the_saved_preference()
		{
			//ADR-0253 §1: the switch "keeps its saved value, so the next game that
			//supports it gets it back". A game that cannot use widescreen is shown
			//and applied as off - turned off on screen, not written off in Config.
			WidescreenSwitchState off = WidescreenSupportRule.Switch(consoleHasSideMap: true, WidescreenSupport.Unsupported, hasWidescreenPackArt: false);
			Assert.False(WidescreenSupportRule.EffectiveWidescreen(savedOn: true, off));
			Assert.False(WidescreenSupportRule.EffectiveWidescreen(savedOn: false, off));

			WidescreenSwitchState on = WidescreenSupportRule.Switch(consoleHasSideMap: true, WidescreenSupport.Supported, hasWidescreenPackArt: false);
			Assert.True(WidescreenSupportRule.EffectiveWidescreen(savedOn: true, on));
			Assert.False(WidescreenSupportRule.EffectiveWidescreen(savedOn: false, on));
		}

		[Theory]
		[InlineData(ConsoleType.Nes, false, true)]
		[InlineData(ConsoleType.Gameboy, false, true)]
		[InlineData(ConsoleType.Gba, false, true)]
		[InlineData(ConsoleType.Sms, true, true)]  //Game Gear: a window on the SMS map
		[InlineData(ConsoleType.Sms, false, false)] //SMS/SG-1000: the map is the screen
		[InlineData(ConsoleType.Snes, false, false)]
		[InlineData(ConsoleType.PcEngine, false, false)]
		[InlineData(ConsoleType.Ws, false, false)]
		public void The_per_console_reveal_scope_is_the_adrs(ConsoleType console, bool gameGear, bool expected)
		{
			Assert.Equal(expected, WidescreenSupportRule.ConsoleHasSideMap(console, gameGear));
		}
	}
}
