using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//What the host sees of the backend's pressed keys is bounded by one number, and
//that number is this one (#895). It used to be a literal 3 written into both
//UI/Interop/InputApi.cs's UInt16[3] buffer and InteropDLL/InputApiWrapper.cpp's
//copy loop, so four or more keys held at once were silently truncated to the
//first three - on macOS that ate the keyboard, because MacOSKeyManager pushes
//pad buttons into the set before the keyboard does.
//
//Sizing the buffer and reading it back are the same rule, so it lives here where
//UI.Tests can pin it against a plain array, with no core and no P/Invoke. The
//native export takes the capacity as an explicit length instead of repeating the
//number, so the two sides cannot drift apart.
public static class PressedKeys
{
	//The backend fills the first N slots of the buffer the host hands it and
	//leaves the rest zero; zero is the empty sentinel, since scan code 0 is never
	//a pressed key. 32 is past anything a player can actually hold down - every
	//keyboard modifier plus a pad's whole button set lands well inside it - and
	//the host is the only side that allocates, so a larger set than this is the
	//host's to grow, never a silent drop inside the copy loop.
	public const int Capacity = 32;

	//Read every non-zero slot the native copy filled, up to the buffer's end so a
	//malformed length cannot read past it.
	public static List<ushort> Decode(IReadOnlyList<ushort> buffer)
	{
		List<ushort> keys = new List<ushort>();
		for(int i = 0; i < buffer.Count && i < Capacity; i++) {
			if(buffer[i] != 0) {
				keys.Add(buffer[i]);
			}
		}
		return keys;
	}
}
