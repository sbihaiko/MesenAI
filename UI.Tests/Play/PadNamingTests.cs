using Mesen.Logic;
using System;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Play;

//ADR-0256 (accepted 2026-10-04) Decisions 2 and 5: the bridge has to know which
//pad a pressed key came from - to navigate with the pad in the player's hand,
//and to resolve the codes that pad's preset binds. A code cannot answer it: each
//family numbers its own block from its own base, so 0x2000 is joystick 1 to a
//Windows DirectInput pad and pad 17 to a backend that numbers every pad "Pad{N}"
//(macOS, Linux), and the presets' names differ per family too. The answer comes
//from the backend, which is why PadInHand.OnPressed takes a probe; this pins the
//probe the window layer wires to InputApi.GetKeyName.
public class PadNamingTests
{
	//A stand-in for a backend's name table, built the way the platform key
	//managers build theirs: a 1-based "{Family}{N} " prefix over that family's
	//button names, plus the shared keyboard names beside them. The numbers are
	//the host's business - which is the whole point of reading names.
	private static readonly Dictionary<ushort, string> _names = new() {
		[0x1000] = "Pad1 A",
		[0x1001] = "Pad1 B",
		[0x1002] = "Pad1 Up",
		[0x1100] = "Pad2 A",
		[0x10C3] = "Pad12 DPad Up",
		[0x2000] = "Joy1 But2",
		[0x2010] = "Joy1 DPad Up",
		[0x2100] = "Joy2 But3",
		[0x104] = "Escape",
		[0x20] = "Space"
	};

	private static string Name(ushort keyCode) => _names.TryGetValue(keyCode, out string? name) ? name : "";

	//The deviceless form: "Pad{N}" is the XInput-shaped family, "Joy{N}" the
	//DirectInput one, and N is 1-based in both, so device 0 is "Pad1"/"Joy1".
	[Theory]
	[InlineData(0x1000, 0, PadFamily.Xbox)]
	[InlineData(0x1100, 1, PadFamily.Xbox)]
	[InlineData(0x10C3, 11, PadFamily.Xbox)]
	[InlineData(0x2000, 0, PadFamily.Ps4)]
	[InlineData(0x2100, 1, PadFamily.Ps4)]
	public void A_pads_own_name_is_the_pad_and_the_family(ushort keyCode, int device, PadFamily family)
	{
		Assert.Equal(new PadId(device, family), PadNaming.Of(keyCode, Name));
	}

	//The trap this rule exists to close, stated as the assertion: the block
	//arithmetic every single-family helper in the app uses (ControllerDevices.
	//DeviceOf) answers 16 for a Windows "Joy1" code, and a bridge that believed
	//it would watch the seventeenth device's buttons - which is nobody's - while
	//the pad in the player's hand moved nothing.
	[Fact]
	public void A_joy_code_is_the_first_joystick_and_not_device_sixteen()
	{
		Assert.Equal(16, (0x2000 - 0x1000) >> 8);
		Assert.Equal(new PadId(0, PadFamily.Ps4), PadNaming.Of(0x2000, Name));
		Assert.NotEqual(16, PadNaming.Of(0x2000, Name)!.Value.Device);
	}

	//Null is a real answer, not a failure: a key no pad sent. The keyboard and
	//the mouse reach the same handler, and a code this backend has no name for
	//(a pad button another platform's backend defines) is not a pad here.
	[Fact]
	public void A_key_no_pad_sent_is_no_pad()
	{
		Assert.Null(PadNaming.Of(0x104, Name));
		Assert.Null(PadNaming.Of(0x20, Name));
		Assert.Null(PadNaming.Of(0x3000, Name));
	}

	//A name that only looks like a pad is not one. The shared keyboard table has
	//no such name today - that is what makes the prefix safe to read - so the
	//cases that must not slip through are the near misses.
	[Theory]
	[InlineData("")]
	[InlineData("Pad")]
	[InlineData("Pad1")]
	[InlineData("Pad1A")]
	[InlineData("Pad X")]
	[InlineData("Padlock")]
	[InlineData("Pad0 A")]
	[InlineData("Joyful")]
	[InlineData("Joy A")]
	public void A_name_that_only_looks_like_a_pad_is_not_one(string name)
	{
		Assert.Null(PadNaming.Parse(name));
	}

	//The other direction: the names a backend really writes resolve, including
	//the two-digit pad the macOS key manager goes up to.
	[Fact]
	public void Every_name_a_backend_writes_resolves()
	{
		foreach((ushort keyCode, string name) in _names) {
			if(name.StartsWith("Pad", StringComparison.Ordinal) || name.StartsWith("Joy", StringComparison.Ordinal)) {
				Assert.NotNull(PadNaming.Of(keyCode, Name));
			}
		}
	}
}
