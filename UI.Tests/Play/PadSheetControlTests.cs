using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Play;

//#1033 (ADR-0264 Decision 3): the library sheet's own controls are not the six
//the pad navigates with. Y opens search, the sheet's footer names it ("Y
//Search"), and the resolution of "which button is Y on this pad" is host-free -
//PadNavControls.SheetControls holds the names and PlayPadNavigation.IsSheetEdge
//holds the edge rule. This pins both, so a preset rename fails here rather than
//handing the player a button their pad does not have.
//
//The six navigation controls keep their own table (PadNavControls.Controls), and
//the reason the sheet's are a SECOND one is ADR-0255: a control in the
//navigation set is one no rebinding surface may offer, and Y stays the player's
//to bind for a console's own button. The assertion at the end of this file is
//that the two tables stay apart.
public class PadSheetControlTests
{
	//A stand-in for the host's key manager, with the answers the code under test
	//may rely on: the names the two presets write resolve to distinct non-zero
	//codes, and anything else to 0 - which is what the platform's GetKeyCode
	//answers for a name it does not define.
	//
	//The face buttons are the names KeyPresets itself writes: the Xbox layout
	//puts the pad's top face button on "Y", and the PS4 layout's But1..But4 are
	//the HID report order (square, cross, circle, triangle), so the triangle -
	//the button the pad prints where an Xbox pad prints Y - is But4.
	private static readonly Dictionary<string, ushort> _keyCodes = BuildKeyCodes();

	private static Dictionary<string, ushort> BuildKeyCodes(bool padOnly = false)
	{
		string[] pad = { "Up", "Down", "Left", "Right", "A", "B", "Y", "X" };
		string[] joy = { "DPad Up", "DPad Down", "DPad Left", "DPad Right", "But2", "But3", "But4", "But1" };
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

	private static ushort PadOnlyKeyCode(string name) => _padOnlyCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	//The sheet's search button, per family: the pad's own top face button on an
	//Xbox pad, and the triangle on a DualShock - and never one pad's name read
	//against the other pad's table.
	[Theory]
	[InlineData(PadFamily.Xbox, "Pad1 Y")]
	[InlineData(PadFamily.Ps4, "Joy1 But4")]
	public void The_search_button_is_the_familys_own(PadFamily family, string expected)
	{
		Assert.Equal(KeyCode(expected), PadNavControls.SheetCode(family, 0, PadSheetControl.Search, KeyCode));
	}

	//#1110 (ADR-0268 Decision 1): X is Favorite - the pad's left face button on an
	//Xbox pad and the square on a DualShock (the preset's SNES Y, Joy But1).
	[Theory]
	[InlineData(PadFamily.Xbox, "Pad1 X")]
	[InlineData(PadFamily.Ps4, "Joy1 But1")]
	public void The_favorite_button_is_the_familys_own(PadFamily family, string expected)
	{
		Assert.Equal(KeyCode(expected), PadNavControls.SheetCode(family, 0, PadSheetControl.Favorite, KeyCode));
	}

	//The second spelling, for the same reason NamesOf has one: which spelling a
	//host defines is the backend's business, not the pad's. macOS and Linux name
	//every pad "Pad" and define no "Joy" name at all, so the PS4 family's own
	//"Joy1 But4" answers 0 there and the Xbox layout's spelling is what is left.
	[Fact]
	public void A_host_that_defines_only_one_spelling_is_still_answered()
	{
		Assert.Equal(PadOnlyKeyCode("Pad1 Y"), PadNavControls.SheetCode(PadFamily.Ps4, 0, PadSheetControl.Search, PadOnlyKeyCode));
	}

	//A family the app cannot tell and a device index nobody resolved are not
	//guesses: the caller acts on the code it is given, and a guess would watch a
	//button on a pad the player is not holding. A name the host does not define
	//is the same answer.
	[Fact]
	public void An_unresolvable_pad_answers_nothing()
	{
		Assert.Null(PadNavControls.SheetCode(null, 0, PadSheetControl.Search, KeyCode));
		Assert.Null(PadNavControls.SheetCode(PadFamily.Xbox, -1, PadSheetControl.Search, KeyCode));
		Assert.Null(PadNavControls.SheetCode(PadFamily.Xbox, 0, PadSheetControl.Search, _ => 0));
	}

	//The edge rule, written the way the nav edge rule is: the button going DOWN,
	//never a button held across a tick. A code of 0 is "this pad has no such
	//control" and is never an edge - no real mapping can produce 0.
	[Fact]
	public void The_sheet_control_is_an_edge_not_a_held_button()
	{
		ushort code = PadNavControls.SheetCode(PadFamily.Xbox, 0, PadSheetControl.Search, KeyCode)!.Value;
		ushort[] none = System.Array.Empty<ushort>();

		Assert.True(PlayPadNavigation.IsSheetEdge(code, new[] { code }, none));
		Assert.False(PlayPadNavigation.IsSheetEdge(code, new[] { code }, new[] { code }));
		Assert.False(PlayPadNavigation.IsSheetEdge(code, none, none));
		Assert.False(PlayPadNavigation.IsSheetEdge(null, new[] { code }, none));
		Assert.False(PlayPadNavigation.IsSheetEdge(0, new[] { (ushort)0 }, none));
	}

	//ADR-0255: the pad's spare controls stay the player's to bind. The sheet's own
	//controls are a second table precisely so that Y is not one of the six a
	//rebinding surface must refuse.
	[Fact]
	public void The_sheets_controls_are_not_the_navigation_set()
	{
		foreach((PadSheetControl control, string xbox, string ps4) in PadNavControls.SheetControls) {
			foreach((PadNavAction action, string navXbox, string navPs4) in PadNavControls.Controls) {
				Assert.False(xbox == navXbox && ps4 == navPs4,
					$"{control} resolved to the same button as {action}, which no rebinding surface may offer");
			}
		}
	}
}
