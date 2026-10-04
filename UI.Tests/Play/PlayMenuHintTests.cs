using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0251: the W-P3 entry toast teaches the way into W-P4 during the first
	//three game starts, naming the binding of the device that started the game,
	//and ToggleOverlay gets a default controller binding.
	public class PlayMenuHintTests
	{
		private static readonly string[] Esc = { "Esc" };
		private static readonly string[] SelectStart = { "Pad1 Select", "Pad1 Start" };
		private static readonly string[] None = Array.Empty<string>();

		[Theory]
		[InlineData(0, true)]
		[InlineData(1, true)]
		[InlineData(2, true)]
		[InlineData(3, false)]
		[InlineData(4, false)]
		[InlineData(100, false)]
		public void The_hint_shows_during_the_first_three_starts_only(int hintsShown, bool shows)
		{
			Assert.Equal(shows, PlayMenuHint.Shows(hintsShown));
		}

		[Fact]
		public void Never_after_the_third_start()
		{
			int shown = 0;
			List<bool> hinted = new();
			for(int start = 0; start < 6; start++) {
				PlayEntryToast? toast = PlayMenuHint.EntryToast("Contra 80s — textures", gameStart: true, inPlay: true, shown, "Esc");
				hinted.Add(toast!.ShowsHint);
				shown = PlayMenuHint.Next(shown, toast);
			}
			Assert.Equal(new[] { true, true, true, false, false, false }, hinted);
			Assert.Equal(3, shown);
		}

		[Fact]
		public void A_pack_toast_ends_with_the_hint()
		{
			PlayEntryToast? toast = PlayMenuHint.EntryToast("Contra 80s — textures", gameStart: true, inPlay: true, 0, "Esc");
			Assert.NotNull(toast);
			Assert.Equal("MEP", toast!.Title);
			Assert.Equal("MepPackApplied", toast.Message);
			Assert.Equal("Contra 80s — textures · Esc for the menu", toast.Param);
			Assert.True(toast.ShowsHint);
		}

		[Fact]
		public void A_pack_toast_after_the_third_start_is_the_plain_toast()
		{
			PlayEntryToast? toast = PlayMenuHint.EntryToast("Contra 80s — textures", gameStart: true, inPlay: true, 3, "Esc");
			Assert.Equal(new PlayEntryToast("MEP", "MepPackApplied", "Contra 80s — textures", false), toast);
		}

		[Fact]
		public void A_game_without_a_pack_gets_the_hint_alone()
		{
			PlayEntryToast? toast = PlayMenuHint.EntryToast(null, gameStart: true, inPlay: true, 1, "Esc");
			Assert.Equal(new PlayEntryToast("GameLoaded", "Esc for the menu", "", true), toast);
			Assert.Equal(2, PlayMenuHint.Next(1, toast));
		}

		[Fact]
		public void A_game_without_a_pack_gets_no_toast_once_the_hints_are_spent()
		{
			Assert.Null(PlayMenuHint.EntryToast("", gameStart: true, inPlay: true, 3, "Esc"));
		}

		//A pack pick power-cycles the game: the reload is not a new start, so it
		//neither shows nor spends a hint.
		[Fact]
		public void A_power_cycle_is_not_a_game_start()
		{
			PlayEntryToast? toast = PlayMenuHint.EntryToast("Contra 80s", gameStart: false, inPlay: true, 0, "Esc");
			Assert.Equal(new PlayEntryToast("MEP", "MepPackApplied", "Contra 80s", false), toast);
			Assert.Equal(0, PlayMenuHint.Next(0, toast));
			Assert.Null(PlayMenuHint.EntryToast(null, gameStart: false, inPlay: true, 0, "Esc"));
		}

		//The hint teaches Play's pause menu; Remaster and Share use Esc otherwise.
		[Fact]
		public void Outside_Play_there_is_no_hint()
		{
			Assert.False(PlayMenuHint.EntryToast("Contra 80s", gameStart: true, inPlay: false, 0, "Esc")!.ShowsHint);
			Assert.Null(PlayMenuHint.EntryToast(null, gameStart: true, inPlay: false, 0, "Esc"));
		}

		[Fact]
		public void Without_any_binding_there_is_no_hint()
		{
			Assert.False(PlayMenuHint.EntryToast("Contra 80s", gameStart: true, inPlay: true, 0, null)!.ShowsHint);
			Assert.Null(PlayMenuHint.EntryToast(null, gameStart: true, inPlay: true, 0, ""));
		}

		[Fact]
		public void Names_the_device_that_started_the_game()
		{
			Assert.Equal("Esc", PlayMenuHint.BindingName(PlayInputDevice.Keyboard, Esc, SelectStart));
			Assert.Equal("Select+Start", PlayMenuHint.BindingName(PlayInputDevice.Controller, Esc, SelectStart));
			Assert.Equal("Home", PlayMenuHint.BindingName(PlayInputDevice.Controller, Esc, new[] { "Pad1 Home" }));
		}

		//Each slot is classified by its keys, so a rebinding that swaps the slots
		//still names the right one.
		[Fact]
		public void The_binding_is_found_in_either_slot()
		{
			Assert.Equal("Esc", PlayMenuHint.BindingName(PlayInputDevice.Keyboard, SelectStart, Esc));
			Assert.Equal("Select+Start", PlayMenuHint.BindingName(PlayInputDevice.Controller, SelectStart, Esc));
		}

		//ADR-0255 slice 4: a shortcut may hold a button and no key, so the pad
		//slot is a slot like the other two - a pad whose overlay binding is Home
		//alone is told "Home", and the keyboard is still told "Esc".
		[Fact]
		public void The_pad_slot_names_the_control_when_no_key_does()
		{
			Assert.Equal("Home", PlayMenuHint.BindingName(PlayInputDevice.Controller, Esc, None, new[] { "Pad1 Home" }));
			Assert.Equal("Esc", PlayMenuHint.BindingName(PlayInputDevice.Keyboard, Esc, None, new[] { "Pad1 Home" }));
		}

		//The pad slot does not outrank a controller key combination: it is the
		//last slot that speaks for the device, not a preference over the others.
		[Fact]
		public void A_controller_key_combination_outranks_the_pad_slot()
		{
			Assert.Equal("Select+Start", PlayMenuHint.BindingName(PlayInputDevice.Controller, Esc, SelectStart, new[] { "Pad1 Home" }));
		}

		//A device without a binding of its own falls back to the other one: an
		//Esc hint still says how in; with nothing bound there is nothing to say.
		[Fact]
		public void A_device_without_a_binding_names_the_other_one()
		{
			Assert.Equal("Esc", PlayMenuHint.BindingName(PlayInputDevice.Controller, Esc, None));
			Assert.Equal("Select+Start", PlayMenuHint.BindingName(PlayInputDevice.Keyboard, None, SelectStart));
			Assert.Null(PlayMenuHint.BindingName(PlayInputDevice.Keyboard, None, None));
		}

		[Fact]
		public void A_keyboard_combination_keeps_its_key_names()
		{
			Assert.Equal("Ctrl+M", PlayMenuHint.BindingName(PlayInputDevice.Keyboard, new[] { "Ctrl", "M" }, None));
		}

		[Theory]
		[InlineData("Pad1 Select", true)]
		[InlineData("Pad12 Start", true)]
		[InlineData("Joy1 But3", true)]
		[InlineData("Esc", false)]
		[InlineData("Pad", false)]
		[InlineData("Page Down", false)]
		public void Controller_keys_are_the_pad_and_joystick_buttons(string keyName, bool isController)
		{
			Assert.Equal(isController, PlayMenuHint.IsControllerKey(keyName));
		}

		[Fact]
		public void A_connected_controller_is_the_active_input()
		{
			Assert.Equal(PlayInputDevice.Keyboard, PlayMenuHint.ActiveDevice(0));
			Assert.Equal(PlayInputDevice.Controller, PlayMenuHint.ActiveDevice(1));
		}

		[Fact]
		public void The_default_controller_binding_prefers_Home()
		{
			HashSet<string> keys = new() { "Pad1 Home", "Pad1 Select", "Pad1 Start" };
			Assert.Equal(new[] { "Pad1 Home" }, PlayMenuHint.DefaultControllerKeys(keys.Contains));
		}

		[Fact]
		public void The_default_controller_binding_accepts_Guide()
		{
			HashSet<string> keys = new() { "Pad1 Guide", "Pad1 Select", "Pad1 Start" };
			Assert.Equal(new[] { "Pad1 Guide" }, PlayMenuHint.DefaultControllerKeys(keys.Contains));
		}

		[Fact]
		public void Without_Home_the_default_is_Select_and_Start_together()
		{
			HashSet<string> keys = new() { "Pad1 Select", "Pad1 Start" };
			Assert.Equal(SelectStart, PlayMenuHint.DefaultControllerKeys(keys.Contains));
		}

		//XInput names Select "Back".
		[Fact]
		public void XInput_pads_use_Back_and_Start()
		{
			HashSet<string> keys = new() { "Pad1 Back", "Pad1 Start" };
			Assert.Equal(new[] { "Pad1 Back", "Pad1 Start" }, PlayMenuHint.DefaultControllerKeys(keys.Contains));
		}

		[Fact]
		public void A_platform_without_pad_buttons_gets_no_controller_binding()
		{
			Assert.Empty(PlayMenuHint.DefaultControllerKeys(_ => false));
		}

		//An upgrade seeds the controller binding only into an empty second slot,
		//and only when no other shortcut already uses that combination: a
		//binding the user made is never replaced or shadowed.
		[Theory]
		[InlineData(true, false, true)]
		[InlineData(false, false, false)]
		[InlineData(true, true, false)]
		[InlineData(false, true, false)]
		public void An_upgrade_seeds_the_binding_only_where_nothing_is_bound(bool secondSlotEmpty, bool combinationTaken, bool seeds)
		{
			Assert.Equal(seeds, PlayMenuHint.SeedsControllerBinding(secondSlotEmpty, combinationTaken, SelectStart));
		}

		[Fact]
		public void No_seed_without_a_default()
		{
			Assert.False(PlayMenuHint.SeedsControllerBinding(true, false, None));
		}
	}
}
