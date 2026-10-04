using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0256 (accepted 2026-10-04): the pad is Player 1's controller, so its menu
//authority is a function of the pause state (Decisions 1 and 2), navigation is
//never rebindable (Decision 4) and confirm and back follow the pad's own preset
//(Decision 6). The three host-free rules live in UI/Logic/PlayPadNavigation,
//PadNavControls and PadInHand; this pins them, and the wiring that reads
//overlayOpen/gameLoaded and asks the host for the pad in hand is a later slice.
public class PadNavigationTests
{
	//A stand-in for the host's key manager, with the two answers the code under
	//test may rely on: every name the two presets write resolves to a distinct
	//non-zero code, and anything else to 0 - which is what the platform's
	//GetKeyCode answers for a name it does not define. The numbers themselves are
	//not the contract; that the six codes differ from each other is.
	private static readonly Dictionary<string, ushort> _keyCodes = BuildKeyCodes();

	private static Dictionary<string, ushort> BuildKeyCodes()
	{
		//The Xbox preset's names are XInput's; the PS4 preset's are DirectInput's,
		//whose face buttons are the HID report order the preset reads as
		//But1..But4 (square, cross, circle, triangle), so cross - the pad's
		//confirm - is But2 and circle - its back - is But3.
		string[] xbox = { "Up", "Down", "Left", "Right", "A", "B" };
		string[] ps4 = { "DPad Up", "DPad Down", "DPad Left", "DPad Right", "But2", "But3" };
		Dictionary<string, ushort> codes = new();
		for(int device = 0; device < 4; device++) {
			for(int i = 0; i < xbox.Length; i++) {
				codes["Pad" + (device + 1) + " " + xbox[i]] = (ushort)(0x1000 + device * 0x100 + i);
			}
			for(int i = 0; i < ps4.Length; i++) {
				codes["Joy" + (device + 1) + " " + ps4[i]] = (ushort)(0x2000 + device * 0x100 + i);
			}
		}
		return codes;
	}

	private static ushort KeyCode(string name) => _keyCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	//The mapping resolved the way the app will resolve it, asserted rather than
	//assumed so a name this table stopped defining fails here and not in a test
	//about navigation.
	private static PadNavMapping Nav(PadFamily family, int device = 0)
	{
		PadNavMapping? mapping = PadNavControls.Resolve(family, device, KeyCode);
		Assert.True(mapping.HasValue);
		return mapping.GetValueOrDefault();
	}

	private static ushort CodeOf(PadNavMapping mapping, PadNavAction action)
	{
		return action switch {
			PadNavAction.Up => mapping.Up,
			PadNavAction.Down => mapping.Down,
			PadNavAction.Left => mapping.Left,
			PadNavAction.Right => mapping.Right,
			PadNavAction.Confirm => mapping.Confirm,
			_ => mapping.Back
		};
	}

	[Theory]
	[InlineData(PadNavAction.Up)]
	[InlineData(PadNavAction.Down)]
	[InlineData(PadNavAction.Left)]
	[InlineData(PadNavAction.Right)]
	public void A_d_pad_press_is_the_direction_it_names(PadNavAction action)
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(action, PlayPadNavigation.Next(new[] { CodeOf(nav, action) }, new ushort[0], nav, hasAuthority: true));
	}

	//Decisions 4 and 6: confirm and back are the pad's own buttons, so the codes
	//come out of the preset - the Xbox pad's A and B, the DualShock's cross and
	//circle. The two families disagree by construction, which is the point: a
	//resolution that answered the same thing for both would be a guess.
	[Fact]
	public void Confirm_and_back_follow_the_Xbox_preset()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(KeyCode("Pad1 A"), nav.Confirm);
		Assert.Equal(KeyCode("Pad1 B"), nav.Back);
		Assert.Equal(KeyCode("Pad1 Up"), nav.Up);
		Assert.Equal(KeyCode("Pad1 Down"), nav.Down);
		Assert.Equal(KeyCode("Pad1 Left"), nav.Left);
		Assert.Equal(KeyCode("Pad1 Right"), nav.Right);
	}

	[Fact]
	public void Confirm_and_back_follow_the_Ps4_preset()
	{
		PadNavMapping nav = Nav(PadFamily.Ps4);
		Assert.Equal(KeyCode("Joy1 But2"), nav.Confirm);
		Assert.Equal(KeyCode("Joy1 But3"), nav.Back);
		Assert.Equal(KeyCode("Joy1 DPad Up"), nav.Up);
		Assert.Equal(KeyCode("Joy1 DPad Down"), nav.Down);
		Assert.Equal(KeyCode("Joy1 DPad Left"), nav.Left);
		Assert.Equal(KeyCode("Joy1 DPad Right"), nav.Right);
	}

	//A second pad of the same family is the same buttons at its own device, not
	//the first pad's codes - the whole reason the mapping carries the device.
	[Fact]
	public void A_preset_is_resolved_for_the_device_it_was_asked_about()
	{
		Assert.Equal(KeyCode("Pad2 A"), Nav(PadFamily.Xbox, 1).Confirm);
		Assert.Equal(KeyCode("Joy2 But3"), Nav(PadFamily.Ps4, 1).Back);
	}

	//Decision 2, in full: W-P4 up, or no game loaded. Decision 1 is the other
	//three rows - a game that runs unpaused keeps its pad, and so does a game
	//paused with no Play surface over it.
	[Theory]
	[InlineData(true, true, true)]
	[InlineData(true, false, true)]
	[InlineData(false, false, true)]
	[InlineData(false, true, false)]
	public void The_pad_has_the_gui_when_the_overlay_is_up_or_no_game_is_loaded(bool overlayOpen, bool gameLoaded, bool expected)
	{
		Assert.Equal(expected, PlayPadNavigation.HasAuthority(overlayOpen, gameLoaded));
	}

	[Fact]
	public void A_press_with_no_authority_is_none()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(PadNavAction.None, PlayPadNavigation.Next(new[] { nav.Confirm }, new ushort[0], nav, hasAuthority: false));
	}

	//A mapping that resolved to nothing cannot fire anything - including the
	//family the app could not tell, which is the case an arcade cabinet hits when
	//its pad is neither preset.
	[Fact]
	public void A_press_with_no_mapping_is_none()
	{
		Assert.Equal(PadNavAction.None, PlayPadNavigation.Next(new[] { KeyCode("Pad1 A") }, new ushort[0], null, hasAuthority: true));
	}

	//The edge: a button held across ticks is one press, and releasing and pressing
	//again is two - without which a held direction would fly the cursor to the end
	//of every list.
	[Fact]
	public void A_held_key_does_not_fire_again_until_it_is_released()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		ushort[] pressed = { nav.Right };

		Assert.Equal(PadNavAction.Right, PlayPadNavigation.Next(pressed, new ushort[0], nav, hasAuthority: true));
		Assert.Equal(PadNavAction.None, PlayPadNavigation.Next(pressed, pressed, nav, hasAuthority: true));
		Assert.Equal(PadNavAction.None, PlayPadNavigation.Next(new ushort[0], pressed, nav, hasAuthority: true));
		Assert.Equal(PadNavAction.Right, PlayPadNavigation.Next(pressed, new ushort[0], nav, hasAuthority: true));
	}

	//The tie-break is the fixed navigation order, not the order the host happened
	//to enumerate its pressed keys in - which is a set, and has none.
	[Fact]
	public void Two_keys_pressed_in_one_tick_break_by_the_navigation_order()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(PadNavAction.Right, PlayPadNavigation.Next(new[] { nav.Right, nav.Confirm }, new ushort[0], nav, hasAuthority: true));
		Assert.Equal(PadNavAction.Right, PlayPadNavigation.Next(new[] { nav.Confirm, nav.Right }, new ushort[0], nav, hasAuthority: true));
	}

	//Decision 4 and ADR-0255 slice 4: the six navigation controls, and only them,
	//are the ones no rebinding surface may offer.
	[Fact]
	public void Every_navigation_control_is_not_rebindable()
	{
		Assert.Equal(6, PadNavControls.Navigation.Count);
		foreach(PadFamily family in new[] { PadFamily.Xbox, PadFamily.Ps4 }) {
			PadNavMapping nav = Nav(family);
			foreach(PadNavAction action in PadNavControls.Navigation) {
				Assert.True(PadNavControls.NonRebindable(CodeOf(nav, action), nav), family + " " + action);
			}
		}

		//A control that is not one of the six is still offerable, and a family
		//that resolved to nothing protects nothing.
		Assert.False(PadNavControls.NonRebindable(KeyCode("Pad1 Start"), Nav(PadFamily.Xbox)));
		Assert.False(PadNavControls.NonRebindable(KeyCode("Pad1 Start"), null));
	}

	//Degrading safely, the three ways it comes up: no family the app can tell, no
	//device index under it, and a backend whose key manager has none of the names
	//(the PS4 preset's "Joy" names do not exist on macOS or Linux, whose pads are
	//all "Pad").
	[Fact]
	public void An_unknown_family_resolves_to_nothing_instead_of_guessing()
	{
		Assert.Null(PadNavControls.Resolve(null, 0, KeyCode));
		Assert.Null(PadNavControls.Resolve(PadFamily.Xbox, -1, KeyCode));
		Assert.Null(PadNavControls.Resolve(PadFamily.Ps4, 0, _ => 0));
	}

	//The device a code came from, the way the host layer will answer it: the two
	//block bases are the ones KeyPresets' prefixes are written over. Kept in the
	//test because it is the *caller's* rule, not the logic's.
	private static PadId? PadOf(ushort key)
	{
		return key >= 0x2000 ? new PadId((key - 0x2000) >> 8, PadFamily.Ps4)
			: key >= 0x1000 ? new PadId((key - 0x1000) >> 8, PadFamily.Xbox)
			: null;
	}

	[Fact]
	public void The_pad_in_hand_follows_the_last_press()
	{
		PadInHand hand = new();
		Assert.False(hand.HasPad);

		hand.OnPressed(new[] { KeyCode("Pad1 A") }, PadOf);
		Assert.Equal(new PadId(0, PadFamily.Xbox), hand.Current);

		//A keyboard key is nobody's pad, and holding the first pad's button does
		//not take the hand back from the pad that pressed last.
		hand.OnPressed(new[] { KeyCode("Pad1 A"), (ushort)0x20 }, PadOf);
		Assert.Equal(new PadId(0, PadFamily.Xbox), hand.Current);
		hand.OnPressed(new[] { KeyCode("Pad1 A"), KeyCode("Pad3 B") }, PadOf);
		Assert.Equal(new PadId(2, PadFamily.Xbox), hand.Current);
		hand.OnPressed(new[] { KeyCode("Pad1 A"), KeyCode("Pad3 B") }, PadOf);
		Assert.Equal(new PadId(2, PadFamily.Xbox), hand.Current);

		//The other family numbers its devices its own way, so the same block is a
		//different device.
		hand.OnPressed(new[] { KeyCode("Joy1 But3") }, PadOf);
		Assert.Equal(new PadId(0, PadFamily.Ps4), hand.Current);
	}

	[Fact]
	public void Two_pads_pressing_in_the_same_tick_are_broken_by_the_lowest_code()
	{
		PadInHand hand = new();
		hand.OnPressed(new[] { KeyCode("Pad3 B"), KeyCode("Pad1 A") }, PadOf);
		Assert.Equal(new PadId(0, PadFamily.Xbox), hand.Current);
	}

	//"Cannot tell" is an answer the caller may give, and it leaves the hand empty
	//rather than naming a pad the app would then navigate with on a guess.
	[Fact]
	public void A_pad_whose_family_cannot_be_told_is_not_the_pad_in_hand()
	{
		PadInHand hand = new();
		hand.OnPressed(new[] { KeyCode("Pad1 A") }, _ => null);
		Assert.False(hand.HasPad);
		Assert.Null(hand.Current);
	}
}
