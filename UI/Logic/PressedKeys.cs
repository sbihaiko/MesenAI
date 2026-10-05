using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//What the host sees of the backend's pressed keys (#895). It used to be a literal
//3 written into both UI/Interop/InputApi.cs's UInt16[3] buffer and
//InteropDLL/InputApiWrapper.cpp's copy loop, so four or more keys held at once
//were silently truncated to the first three - on macOS that ate the keyboard,
//because MacOSKeyManager pushes pad buttons into the set before the keyboard
//does.
//
//Sizing the buffer and reading it back are the same rule, so it lives here where
//UI.Tests can pin it against a plain array, with no core and no P/Invoke. The
//native export takes the capacity as an explicit length instead of repeating the
//number, and answers the size of the set it holds, so the host asks again with
//room for what it was told instead of keeping the first Capacity entries of it.
public static class PressedKeys
{
	//The first ask. It is not a bound the host is stuck with: the backends push
	//more than this - macOS 24 indices per controller with no cap on how many
	//controllers are connected (six of those pairs are opposed axis directions, so
	//one pad holds at most 18 of them down at once, and two are 36), Linux 55 per
	//controller, Windows 26 per XInput slot and 144 per DirectInput joystick, per
	//each platform's own GetPressedKeys. Read grows for a set past this; the
	//number is only the common case's single allocation.
	public const int Capacity = 32;

	//The most the host will allocate for one read, and so the one place a count can
	//still be lost: a set larger than this is read up to the ceiling and no
	//further, and nothing signals it. It is not a device limit - 4096 is past what
	//a machine with real controllers can hold down (it would take about a hundred
	//pads) - so an answer past it is a value that is not describing pressed keys.
	//That is also the shape a MISMATCHED core library produces, and this caps the
	//allocation that answer would otherwise ask for; it does NOT detect that
	//library, which nothing here can, since the export's symbol name does not
	//encode its return type.
	public const int MaxKeys = 4096;

	//How many times Read asks the backend for its set. One is the common case (the
	//set fits the first buffer); more is spent only when the answer did not fit,
	//where each ask is sized from the previous answer, so a set that is merely
	//larger converges on the second. The cap is what keeps a set growing faster
	//than it is read from turning this into an unbounded loop; at the cap Read
	//gives up and keeps what fits.
	public const int MaxAsks = 4;

	//Read the backend's pressed set, asking again whenever an answer says the set
	//is larger than the buffer that was handed over.
	//
	//Every ask takes a fresh snapshot (WindowsKeyManager refreshes the XInput and
	//DirectInput states inside it, and the emulation thread calls it too), so the
	//answer to the first ask is not a promise about the second. Each answer is
	//believed, and the buffer grows to what the LAST one said - which is why the
	//fill's return is read on every pass and not only on the first: a set that
	//grows between two asks is followed rather than truncated to the size the
	//first one reported.
	public static List<ushort> Read(Func<ushort[], int> fill)
	{
		ushort[] buffer = new ushort[Capacity];
		int reported = fill(buffer);
		for(int ask = 1; ask < MaxAsks && reported > buffer.Length && buffer.Length < MaxKeys; ask++) {
			buffer = new ushort[Math.Min(reported, MaxKeys)];
			reported = fill(buffer);
		}
		return Decode(buffer);
	}

	//Read every non-zero slot of the buffer the caller handed over. Zero is
	//treated as "no key here": the native copy leaves the slots past the set
	//untouched and the caller's `new ushort[]` is zeroed, and the host's own
	//consumers apply the same convention (StateGrid skips a code of 0 before it
	//does anything with it). It is a convention and not a property of every
	//backend: MacOSKeyManager maps a few unassigned key codes to 0, so a key that
	//lands on that code is invisible here and in every host consumer - upstream
	//behaviour that predates this read, and not something it can fix from here.
	//The array's own length is the bound: the host allocates it, and Read sizes it
	//from what the backend reported.
	public static List<ushort> Decode(IReadOnlyList<ushort> buffer)
	{
		List<ushort> keys = new List<ushort>();
		for(int i = 0; i < buffer.Count; i++) {
			if(buffer[i] != 0) {
				keys.Add(buffer[i]);
			}
		}
		return keys;
	}
}
