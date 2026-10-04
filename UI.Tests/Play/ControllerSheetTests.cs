using Mesen.Interop;
using Mesen.Logic;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0255 slice 1 (W-P17): the Controller sheet lights a key of W-P15's
	//drawn pad from the pad's own buttons, and the button it reads is a bit of
	//the core's GamepadState.Buttons - the order GamepadTestItem's list is in.
	//That table is the whole host-free rule the sheet adds; the headless suite
	//pins it against the tester's real list, so these keep it complete and
	//unambiguous on its own side.
	public class ControllerSheetTests
	{
		//The console pad's keys and the core's bit each one stands for. A modern
		//pad puts "Menu" right of centre and "Options" left of it, which is how
		//W-P15 draws the two pills.
		private static readonly (SetupButton Key, int Bit)[] DrawnKeys = {
			(SetupButton.A, 0),
			(SetupButton.B, 1),
			(SetupButton.L, 4),
			(SetupButton.R, 5),
			(SetupButton.Start, 6),
			(SetupButton.Select, 7),
			(SetupButton.Up, 8),
			(SetupButton.Down, 9),
			(SetupButton.Left, 10),
			(SetupButton.Right, 11)
		};

		[Fact]
		public void Every_drawn_key_reads_the_button_the_core_reports_for_it()
		{
			//The reference console-pad backend (macOS/GameController): the drawn
			//pad's keys are its own button names, so it is the one backend whose
			//table is the plain console-pad order. The other two backends' orders
			//are pinned against the Core's table in
			//UI.HeadlessTests/ControllerSheetPadTests.
			foreach((SetupButton key, int bit) in DrawnKeys) {
				Assert.Equal(bit, ControllerLivePad.BitOf(key, GamepadBackend.GameController));
			}

			//One bit per key: a duplicate would light two keys at once, which is
			//what a hand-edited table does.
			Dictionary<SetupButton, int?> bits = DrawnKeys.ToDictionary(e => e.Key, e => ControllerLivePad.BitOf(e.Key, GamepadBackend.GameController));
			Assert.Equal(DrawnKeys.Length, bits.Values.Distinct().Count());
		}

		[Fact]
		public void The_drawn_pad_is_every_key_a_console_pad_has()
		{
			//No key of SetupButton may be missing from the picture: the drawn pad
			//is the console's, and an absent arm reads as a pad that has none.
			SetupButton[] all = System.Enum.GetValues<SetupButton>();
			Assert.Equal(all.OrderBy(k => k).ToArray(), ControllerLivePad.Keys.OrderBy(k => k).ToArray());
			Assert.Equal(all.Length, DrawnKeys.Length);
		}

		[Theory]
		[InlineData(true, true, true)]
		[InlineData(true, false, false)]
		[InlineData(false, true, false)]
		[InlineData(false, false, false)]
		public void The_sheet_reads_the_pad_only_while_visible_over_a_paused_game(bool visible, bool paused, bool wanted)
		{
			//ADR-0255 Consequences: the reads are scoped, and a sheet that kept
			//polling behind a running game would break it. The headless suite
			//covers the timer that follows this answer; the one case it cannot
			//drive - the game resuming under a visible sheet - lands on the same
			//tick, so it is this row.
			Assert.Equal(wanted, ControllerSheetReads.Wanted(visible, paused));
		}

		[Theory]
		[InlineData(true, true)]
		[InlineData(false, false)]
		public void The_poll_runs_while_the_sheet_is_visible_whatever_the_game_does(bool visible, bool polls)
		{
			//#821 follow-up: the timer's lifetime is the sheet's visibility, not the
			//stricter read condition - nothing observes the pause state, so a tick
			//is what notices a game pausing again under the sheet. A timer that
			//stopped on resume never restarted and the sheet froze forever.
			Assert.Equal(polls, ControllerSheetReads.Polls(visible));
		}

		[Fact]
		public void The_poll_is_wider_than_the_reads()
		{
			//A visible sheet over a game that resumed: the timer keeps ticking (so
			//it can see a later pause) while the device reads stop.
			Assert.True(ControllerSheetReads.Polls(true));
			Assert.False(ControllerSheetReads.Wanted(true, gamePaused: false));
		}

		[Theory]
		[InlineData(SetupButton.A, true)]
		[InlineData(SetupButton.B, true)]
		[InlineData(SetupButton.L, true)]
		[InlineData(SetupButton.R, true)]
		[InlineData(SetupButton.Up, false)]
		[InlineData(SetupButton.Down, false)]
		[InlineData(SetupButton.Select, false)]
		[InlineData(SetupButton.Start, false)]
		public void Only_the_round_and_shoulder_keys_carry_a_word(SetupButton key, bool worded)
		{
			//Wordiness is W-P15's own rule, so both pads word their keys alike; the
			//name itself is every key's, because the accessibility tree has no
			//"wordless" - an unnamed D-pad arm would announce as nothing.
			Assert.Equal(worded, ControllerPadLayout.ShowsLabel(key));
			Assert.Equal(key.ToString(), ControllerLivePad.KeyName(key));
		}
	}
}
