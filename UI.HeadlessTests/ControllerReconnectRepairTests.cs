using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 5, the driver: notice a pad that came back at a different device
//index, move its keys, and write only when a key actually moved. The decision is
//host-free (UI.Tests/Play/DeviceReconnectTests) and the walk is in
//ControllerKeyMigrationTests; what this pins is the driver's own ordering -
//observe first, then rewrite, then ApplyConfig/Save, and never write when the
//move touched no key or when the move is ambiguous.
//
//The pads and the apply/save sink are injected, so the default ReadHostPads
//(which names InputApi.GetConnectedGamepadCount) is never called. The guard's IL
//walk still sees it through the driver's constructor, which is the indirect path
//[NativeCoreFree] exists to excuse: no argument here takes that branch.
[NativeCoreFree("Pads and the apply/save sink are injected; the default ReadHostPads is never called.")]
public class ControllerReconnectRepairTests
{
	private static ushort Key(int device, int button) => (ushort)(0x1000 + device * 0x100 + button);

	//Evdev: a one-family identified backend, the shape this repair is about.
	private static PadIdentity Pad(int index, uint vendorId, uint productId) => new(GamepadBackend.Evdev, index, vendorId, productId);

	private const uint Vid = 0x054C, Pid = 0x0CE6; //DualSense

	[Fact]
	public void A_reconnect_moves_the_pads_keys_and_only_then_writes()
	{
		Configuration config = new();
		config.Nes.Port2.Mapping1.A = Key(1, 3);
		List<PadIdentity> pads = new() { Pad(1, Vid, Pid) };
		int applied = 0, saved = 0;
		ControllerReconnectRepair repair = new() {
			Target = config,
			ReadPads = () => pads,
			Apply = () => applied++,
			Save = () => saved++
		};

		//First sighting: the baseline. Nothing moved and nothing was written.
		Assert.Empty(repair.Check());
		Assert.Equal(Key(1, 3), config.Nes.Port2.Mapping1.A);
		Assert.Equal(0, applied);
		Assert.Equal(0, saved);

		//Back at device 0: the keys move with it, then ApplyConfig and Save run.
		pads[0] = Pad(0, Vid, Pid);
		DeviceMove move = Assert.Single(repair.Check());
		Assert.Equal(new DeviceMove(GamepadBackend.Evdev, 1, 0), move);
		Assert.Equal(Key(0, 3), config.Nes.Port2.Mapping1.A);
		Assert.Equal(1, applied);
		Assert.Equal(1, saved);
	}

	[Fact]
	public void A_pad_that_reconnects_with_no_keys_bound_writes_nothing()
	{
		Configuration config = new();
		List<PadIdentity> pads = new() { Pad(1, Vid, Pid) };
		int applied = 0, saved = 0;
		ControllerReconnectRepair repair = new() {
			Target = config,
			ReadPads = () => pads,
			Apply = () => applied++,
			Save = () => saved++
		};
		repair.Check();

		//The reconnect is still observed, but no key carried that index: there is
		//nothing to apply, and saving would only churn settings.json.
		pads[0] = Pad(0, Vid, Pid);
		Assert.Single(repair.Check());
		Assert.Equal(0, applied);
		Assert.Equal(0, saved);
	}

	[Fact]
	public void An_unidentified_pad_never_writes()
	{
		Configuration config = new();
		config.Nes.Port1.Mapping1.A = Key(1, 3);
		//VID:PID 0:0000 cannot be keyed; every macOS pad and every Windows XInput
		//pad is invisible to the repair this way.
		List<PadIdentity> pads = new() { Pad(1, 0, 0) };
		int applied = 0;
		ControllerReconnectRepair repair = new() {
			Target = config,
			ReadPads = () => pads,
			Apply = () => applied++,
			Save = () => { }
		};
		repair.Check();

		pads[0] = Pad(0, 0, 0);
		Assert.Empty(repair.Check());
		Assert.Equal(0, applied);
		Assert.Equal(Key(1, 3), config.Nes.Port1.Mapping1.A);
	}

	[Fact]
	public void Two_pads_sharing_an_identity_never_write()
	{
		//The review's finding 1 at the driver level: two pads of the same model
		//must not merge one player's bindings onto the other's device index.
		Configuration config = new();
		config.Nes.Port2.Mapping1.A = Key(1, 3);
		List<PadIdentity> pads = new() { Pad(1, Vid, Pid) };
		int applied = 0;
		ControllerReconnectRepair repair = new() {
			Target = config,
			ReadPads = () => pads,
			Apply = () => applied++,
			Save = () => { }
		};
		repair.Check();

		//Both of the same model present: no move may be guessed.
		pads.Clear();
		pads.Add(Pad(0, Vid, Pid));
		pads.Add(Pad(1, Vid, Pid));
		Assert.Empty(repair.Check());

		//One unplugged; the survivor is not a reconnect of the other.
		pads.Clear();
		pads.Add(Pad(1, Vid, Pid));
		Assert.Empty(repair.Check());

		Assert.Equal(0, applied);
		Assert.Equal(Key(1, 3), config.Nes.Port2.Mapping1.A);
	}
}
