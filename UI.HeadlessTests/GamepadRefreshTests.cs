using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Mesen.Interop;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#813 / ADR-0255 slice 2: a player row finds its pad by the block its
//(Backend, Slot) builds, so the tester must keep that identity current - not only
//when the connected count changes. A pad swapped in at an index the count still
//covers (the reconnect this ADR is about, or one joystick replacing another on
//the same port) leaves the item's Backend/Slot stale under a count-only rule, and
//a row would then name - or hand the port to - the wrong physical pad.
//
//The host reads are injected, so no physical pad and no built MesenCore are
//needed; the class is in the serial collection only because the guard's IL walk
//follows GamepadTesterViewModel's constructor to InputApi's method group.
[Collection(NativeCoreCollection.Name)]
public class GamepadRefreshTests
{
	private static GamepadInfo Info(string name, GamepadBackend backend, uint slot) =>
		new() { Name = name, Backend = backend, Slot = slot };

	[AvaloniaFact]
	public void The_tester_re_reads_a_pads_identity_when_the_count_did_not_change()
	{
		GamepadTesterViewModel tester = new();
		Dictionary<uint, GamepadInfo> infos = new() {
			[0] = Info("Pad Zero", GamepadBackend.XInput, 0),
			[1] = Info("Pad One", GamepadBackend.XInput, 1)
		};
		tester.ConnectedGamepadCount = () => 2;
		tester.GamepadInfoOf = index => infos.TryGetValue(index, out GamepadInfo i) ? i : null;
		tester.GamepadStateOf = _ => null;

		tester.Refresh();
		Assert.Equal("Pad One", tester.Gamepads[1].Name);
		Assert.Equal(GamepadBackend.XInput, tester.Gamepads[1].Backend);
		Assert.Equal((uint)1, tester.Gamepads[1].Slot);

		//One pad out, another in - the count is still 2, so the count says
		//nothing changed. The identity has to follow the pad that is there now.
		infos[1] = Info("Joystick", GamepadBackend.DirectInput, 0);
		tester.Refresh();

		Assert.Equal("Joystick", tester.Gamepads[1].Name);
		Assert.Equal(GamepadBackend.DirectInput, tester.Gamepads[1].Backend);
		Assert.Equal((uint)0, tester.Gamepads[1].Slot);
	}
}
