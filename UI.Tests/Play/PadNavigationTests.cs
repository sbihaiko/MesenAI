using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

	//The fake host. "Start" is here for the one assertion that needs a pad button
	//that is *not* one of the six - a table without it made that assertion pass
	//against code 0, which no real mapping can produce.
	private static Dictionary<string, ushort> BuildKeyCodes(bool padOnly = false)
	{
		//The Xbox preset's names are XInput's; the PS4 preset's are DirectInput's,
		//whose face buttons are the HID report order the preset reads as
		//But1..But4 (square, cross, circle, triangle), so cross - the pad's
		//confirm - is But2 and circle - its back - is But3.
		//
		//padOnly is the macOS/Linux host, which names every pad "Pad" and defines
		//no "Joy" name at all (MacOSKeyManager and LinuxKeyManager); the default is
		//the Windows one, which defines both families (XInput and DirectInput).
		string[] pad = { "Up", "Down", "Left", "Right", "A", "B", "Start" };
		string[] joy = { "DPad Up", "DPad Down", "DPad Left", "DPad Right", "But2", "But3" };
		Dictionary<string, ushort> codes = new();
		for(int device = 0; device < 4; device++) {
			for(int i = 0; i < pad.Length; i++) {
				codes["Pad" + (device + 1) + " " + pad[i]] = (ushort)(0x1000 + device * 0x100 + i);
			}
			if(padOnly) {
				continue;
			}
			for(int i = 0; i < joy.Length; i++) {
				codes["Joy" + (device + 1) + " " + joy[i]] = (ushort)(0x2000 + device * 0x100 + i);
			}
		}
		return codes;
	}

	private static readonly Dictionary<string, ushort> _padOnlyCodes = BuildKeyCodes(padOnly: true);

	private static ushort KeyCode(string name) => _keyCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	//The first name production asks the host for - the family's own spelling.
	//Read off PadNavControls.NamesOf rather than copied, so a rename that forgot
	//KeyPresets fails here instead of resolving to nothing in the app.
	private static string NameOf(PadFamily family, PadNavAction action) => PadNavControls.NamesOf(family, 0, action)[0];

	private static ushort PadOnlyKeyCode(string name) => _padOnlyCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

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

	//The reducer has to emit Confirm and Back, not only the four directions: a
	//Next that answered None for both would leave the cabinet unable to activate a
	//row or go back, and the tie-break test alone does not catch it (it asserts
	//Right, which the directions already produce).
	[Theory]
	[InlineData(PadFamily.Xbox, PadNavAction.Confirm)]
	[InlineData(PadFamily.Xbox, PadNavAction.Back)]
	[InlineData(PadFamily.Ps4, PadNavAction.Confirm)]
	[InlineData(PadFamily.Ps4, PadNavAction.Back)]
	public void Confirm_and_back_are_reported_too(PadFamily family, PadNavAction action)
	{
		PadNavMapping nav = Nav(family);
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
		Assert.Equal(KeyCode(NameOf(PadFamily.Xbox, PadNavAction.Confirm)), nav.Confirm);
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

	//Decisions 1 and 2, in full, with the surfaces named rather than one coarse
	//"something is drawn over the game". The pair that matters: a surface is the
	//pad's only when the game is paused under it. A surface that does not pause
	//(the barcode tool sheet, Settings from a task door, the archive's ROM list)
	//is drawn over a running game but must not take the pad (Decision 1); a game
	//paused by the classic menus or an auto-pause with no Play surface over it has
	//nothing to drive, so it keeps its pad too. The load card (rows 7-8) is refused
	//because it has no focusable control, the BIOS sheet over the same load (row 3)
	//is not, and the on-load pack picker (rows 9-10) is granted because it must be
	//answered before play.
	[Theory]
	[InlineData(true, true, true, false, false, true)]    // W-P4 (or a sheet from it) over a game it paused
	[InlineData(true, true, false, false, false, false)]  // a surface that never paused the game
	[InlineData(true, false, false, false, false, true)]  // a sheet over the home, no game (the BIOS sheet inside a load shares this shape)
	[InlineData(false, false, false, false, false, true)] // the Play home, no game
	[InlineData(false, true, true, false, false, false)]  // paused, but no Play surface (classic menu / auto-pause)
	[InlineData(false, true, false, false, false, false)] // a game running unpaused (Decision 1)
	[InlineData(true, false, false, true, false, false)]  // the load card over the home
	[InlineData(true, true, true, true, false, false)]    // the load card over a paused game (a reload)
	[InlineData(true, true, false, false, true, true)]    // the on-load pack picker over an unpaused game
	[InlineData(true, true, false, true, true, true)]     // the on-load picker while the load card is still up
	public void The_pad_has_the_gui_only_when_a_pausing_surface_is_up_or_no_game_is_loaded(bool playSurfaceUp, bool gameLoaded, bool gamePaused, bool loadCardUp, bool firstRunPickerUp, bool expected)
	{
		Assert.Equal(expected, PlayPadNavigation.HasAuthority(playSurfaceUp, gameLoaded, gamePaused, loadCardUp, firstRunPickerUp));
	}

	//Decision 2 for the slot grid: Back is the grid's only way out from a pad, and
	//the Load/Save-state shortcuts open it with no authority to gate it - so the
	//edge is asked for directly, whatever the authority rule says.
	[Fact]
	public void Back_is_an_edge_even_without_menu_authority()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);

		Assert.True(PlayPadNavigation.IsBackEdge(new[] { nav.Back }, new ushort[0], nav));
		Assert.False(PlayPadNavigation.IsBackEdge(new[] { nav.Back }, new[] { nav.Back }, nav));
		Assert.False(PlayPadNavigation.IsBackEdge(new[] { nav.Confirm }, new ushort[0], nav));
		Assert.False(PlayPadNavigation.IsBackEdge(new[] { nav.Back }, new ushort[0], null));
	}

	//The door every rule here is asked inside, and a rule of its own because two
	//callers ask it: the bridge (authority, the Back edge) and the slot grid's own
	//pad branch. The grid is one control drawing both doors' grids - W-P2's tiles
	//and the Save/Load screens in Play, Advanced's game-selection and Save/Load
	//screens in the classic GUI - so a branch of it that reads the pad's preset has
	//to ask the same door the bridge does, or the classic grid loses the console
	//mapping it navigated with before ADR-0256 (the ADR is the Play GUI's).
	[Theory]
	[InlineData(true, true, true)]    // Player mode in a game-screen workspace: the Play door
	[InlineData(true, false, false)]  // Player mode with the game screen away (Remaster/Share)
	[InlineData(false, true, false)]  // the classic UI mode under a game-screen workspace
	[InlineData(false, false, false)]
	public void The_play_door_is_player_mode_in_a_game_screen_workspace(bool isPlayerMode, bool isPlayWorkspace, bool expected)
	{
		Assert.Equal(expected, PlayPadNavigation.InPlayDoor(isPlayerMode, isPlayWorkspace));
	}

	//Decision 4 for the grid: its four directions and Confirm come off the pad's
	//own preset, and Back is deliberately not one of them - leaving the grid is the
	//bridge's. That split is what tells the pad's B (the preset's Back) apart from
	//the console's A, which sits on the same button.
	[Theory]
	[InlineData(PadNavAction.Up)]
	[InlineData(PadNavAction.Down)]
	[InlineData(PadNavAction.Left)]
	[InlineData(PadNavAction.Right)]
	[InlineData(PadNavAction.Confirm)]
	public void The_grid_reads_its_directions_and_confirm_off_the_pad_preset(PadNavAction action)
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(action, PlayPadNavigation.GridAction(CodeOf(nav, action), nav));
	}

	[Fact]
	public void The_grid_never_reads_back_because_back_leaves_it()
	{
		PadNavMapping nav = Nav(PadFamily.Xbox);
		Assert.Equal(PadNavAction.None, PlayPadNavigation.GridAction(nav.Back, nav));
		Assert.Equal(PadNavAction.None, PlayPadNavigation.GridAction(KeyCode("Pad1 Start"), nav));
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
		Assert.NotEqual(0, KeyCode("Pad1 Start"));
		Assert.False(PadNavControls.NonRebindable(KeyCode("Pad1 Start"), Nav(PadFamily.Xbox)));
		Assert.False(PadNavControls.NonRebindable(KeyCode("Pad1 Start"), null));
	}

	//The macOS/Linux host defines no "Joy" name at all, so a DualShock read as
	//Ps4 - the preset the first run applies - has to resolve through the other
	//spelling of the same control rather than resolving to nothing. This is the
	//defect the review caught: without the fallback, the menu simply did not move
	//on those two hosts, and every test above still passed because they all fed
	//Resolve a host that defines both spellings.
	[Fact]
	public void A_pad_only_host_still_resolves_the_Ps4_family()
	{
		PadNavMapping? mapping = PadNavControls.Resolve(PadFamily.Ps4, 0, PadOnlyKeyCode);
		Assert.True(mapping.HasValue);
		PadNavMapping nav = mapping!.Value;
		Assert.Equal(PadOnlyKeyCode("Pad1 Up"), nav.Up);
		Assert.Equal(PadOnlyKeyCode("Pad1 A"), nav.Confirm);
		Assert.Equal(PadOnlyKeyCode("Pad1 B"), nav.Back);
	}

	//...and a host that defines neither spelling is still nothing, not a guess.
	[Fact]
	public void A_host_with_neither_spelling_resolves_to_nothing()
	{
		Assert.Null(PadNavControls.Resolve(PadFamily.Ps4, 0, _ => 0));
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

//#1064: a *ForTest member is a door for a headless case, and a door the
//shipping path leans on has stopped being a door - it is load-bearing, so it
//can no longer move, and the test that names it cannot follow it anywhere. The
//pad wiring's own focus decisions therefore read the open keyboard's field
//through their own accessor; the public KeyboardFieldForTest delegates to it,
//which is what keeps PlayerLibrarySearchTests' door open. This pins that the
//shipping file never calls a test-only member again.
public class PadWiringTestSeamTests
{
	private static readonly Regex ForTestCall = new(@"\b\w+ForTest\s*\(", RegexOptions.Compiled);

	[Fact]
	public void The_pad_wirings_focus_decisions_never_call_a_test_only_member()
	{
		string path = Path.Combine(FindRepoRoot(), "UI", "Windows", "PlayPadNavigationWiring.cs");
		List<string> offenders = new();
		string[] lines = File.ReadAllLines(path);
		for(int i = 0; i < lines.Length; i++) {
			string line = lines[i];
			//A declaration is a member, so it carries a modifier - static. A line
			//that names a ForTest member without one is a call, wherever it sits.
			//A comment is prose and never reaches the shipped binary.
			if(line.Contains("static") || line.TrimStart().StartsWith("//")) {
				continue;
			}
			if(ForTestCall.IsMatch(line)) {
				offenders.Add($"{path}({i + 1}): {line.Trim()}");
			}
		}

		Assert.True(offenders.Count == 0,
			"The pad wiring's production code calls a test-only member; read it through a private accessor and let the *ForTest member delegate to it:"
				+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	private static string FindRepoRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
			dir = dir.Parent;
		}
		if(dir == null) {
			throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}
		return dir.FullName;
	}
}
