using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1104 (spec #1102, ADR-0256 Decision 6): every pad-drivable Play surface
	//renders its footer from the actions it declares, in the fixed order A, X, Y,
	//shoulders, B, naming the control in words for the pad in hand. The labels are
	//resource keys here, resolved by the identity so the expected strings are the
	//rule's own output and not copy.
	public class PlayActionBarTests
	{
		private static readonly PlayBarEntry Play = new(PlayAction.Confirm, "Play");
		private static readonly PlayBarEntry Favorite = new(PlayAction.Favorite, "Favorite");
		private static readonly PlayBarEntry Search = new(PlayAction.Search, "Search");
		private static readonly PlayBarEntry Console = new(PlayAction.ConsoleFilter, "Console");
		private static readonly PlayBarEntry Back = new(PlayAction.Back, "Back");

		//Declared out of order on purpose: the bar sorts, a surface does not have to.
		private static readonly PlayBarEntry[] Library = { Back, Console, Search, Favorite, Play };

		private static string Text(PlayInputDevice device, PadFamily? family, bool keyboardOpen = false, IReadOnlyList<PlayBarEntry>? declared = null)
			=> PlayActionBar.Text(declared ?? Library, device, family, keyboardOpen, key => key);

		[Fact]
		public void An_Xbox_pad_is_named_A_X_Y_LB_RB_B_in_that_order()
		{
			Assert.Equal("A Play     X Favorite     Y Search     LB / RB Console     B Back", Text(PlayInputDevice.Controller, PadFamily.Xbox));
		}

		[Fact]
		public void A_PlayStation_pad_is_named_in_words()
		{
			Assert.Equal("Cross Play     Square Favorite     Triangle Search     L1 / R1 Console     Circle Back", Text(PlayInputDevice.Controller, PadFamily.Ps4));
		}

		//Decision 4's reason: a family the app cannot tell names no button.
		[Fact]
		public void A_pad_whose_family_is_unresolved_names_no_button()
		{
			Assert.Equal("Play     Favorite     Search     Console     Back", Text(PlayInputDevice.Controller, null));
		}

		//With no pad the bar names keys, and an action only a pad performs is not
		//listed: every label stays true.
		[Fact]
		public void No_pad_names_the_keyboard_keys_and_drops_pad_only_actions()
		{
			Assert.Equal("Enter Play     Esc Back", Text(PlayInputDevice.Keyboard, null));
		}

		[Fact]
		public void The_keyboard_stays_the_keyboard_beside_a_pad_family()
		{
			Assert.Equal("Enter Play     Esc Back", Text(PlayInputDevice.Keyboard, PadFamily.Xbox));
		}

		[Fact]
		public void Only_the_declared_actions_are_listed()
		{
			Assert.Equal("A Select     B Resume", Text(PlayInputDevice.Controller, PadFamily.Xbox, declared: new[] { new PlayBarEntry(PlayAction.Back, "Resume"), new PlayBarEntry(PlayAction.Confirm, "Select") }));
		}

		//ADR-0262: every press is the keyboard's, so the surface's entries are
		//replaced - a bar that still said "B Back" would promise a leave that is a
		//cancel.
		[Theory]
		[InlineData(PlayInputDevice.Controller, PadFamily.Xbox, "D-pad BarKeyboardMove     A BarKeyboardPress     B BarKeyboardCancel")]
		[InlineData(PlayInputDevice.Controller, PadFamily.Ps4, "D-pad BarKeyboardMove     Cross BarKeyboardPress     Circle BarKeyboardCancel")]
		[InlineData(PlayInputDevice.Controller, null, "BarKeyboardMove     BarKeyboardPress     BarKeyboardCancel")]
		[InlineData(PlayInputDevice.Keyboard, null, "Arrows BarKeyboardMove     Enter BarKeyboardPress     Esc BarKeyboardCancel")]
		public void While_the_on_screen_keyboard_is_open_the_bar_shows_its_own_entries(PlayInputDevice device, PadFamily? family, string expected)
		{
			Assert.Equal(expected, Text(device, family, keyboardOpen: true));
		}

		[Fact]
		public void No_declared_actions_is_no_bar()
		{
			Assert.Equal("", Text(PlayInputDevice.Controller, PadFamily.Xbox, declared: System.Array.Empty<PlayBarEntry>()));
		}
	}
}
