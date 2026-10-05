using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//What the host sees of the backend's pressed keys is bounded by how much room it
//asked for, and by nothing else (#895). It used to be a literal 3 written into
//both UI/Interop/InputApi.cs's UInt16[3] buffer and InteropDLL/InputApiWrapper.cpp's
//copy loop, so four or more keys held at once were silently truncated to the
//first three - on macOS that ate the keyboard, because MacOSKeyManager pushes
//pad buttons into the set before the keyboard does.
//
//Sizing the buffer and reading it back are the same rule, so it lives here where
//UI.Tests can pin it against a plain array, with no core and no P/Invoke. The
//native export takes the capacity as an explicit length instead of repeating the
//number, and answers the size of the set it holds, so a set larger than the
//first ask is grown rather than lost.
public static class PressedKeys
{
	//The first ask. A cabinet's realistic set fits inside it, but the backends'
	//own maxima do not: Windows pushes up to 144 DirectInput buttons per
	//joystick (WindowsKeyManager.cpp, `j < 16 + 128`) on top of 26 per XInput
	//slot, Linux up to 55 per controller, macOS 24 per controller with no cap on
	//the device count. Two pads held at once on macOS is already 48 - so a set
	//past this is a real case and Read grows for it, rather than this number
	//being the end of what the host can see.
	public const int Capacity = 32;

	//The most the host will allocate for one read. Not a device limit: 4096 is
	//past any input set a machine can hold down, so a backend answering more than
	//this is not describing pressed keys - it is a value the host has no reason
	//to allocate for (and, since the export's return is read by a caller that a
	//stale core library could pair an older, count-less version of, a ceiling
	//here keeps a garbage answer from becoming a garbage allocation).
	public const int MaxKeys = 4096;

	//Read the backend's pressed set, growing the buffer when the backend answers
	//a larger set than the first ask had room for.
	//
	//The export reports the SIZE of the set it holds, not merely how many slots
	//it filled, which is what makes the growth a read of the backend's truth
	//instead of a guess: the host asks again with room for exactly that many, and
	//the copy loop stops at the caller's bound, so both sides still read one
	//number (Capacity, here).
	public static List<ushort> Read(Func<ushort[], int> fill)
	{
		ushort[] buffer = new ushort[Capacity];
		int reported = fill(buffer);
		if(reported > buffer.Length) {
			buffer = new ushort[Math.Min(reported, MaxKeys)];
			fill(buffer);
		}
		return Decode(buffer);
	}

	//Read every non-zero slot of the buffer the caller handed over. Zero is the
	//empty sentinel, since scan code 0 is never a pressed key. The array's own
	//length is the bound: the host allocates it, and Read sizes it from what the
	//backend reported.
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
