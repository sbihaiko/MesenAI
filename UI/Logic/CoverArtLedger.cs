using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//#1035 (ADR-0264 Decision 6.3): who owns the pictures a grid decoded.
//
//A tile's cover taken from the Recent list is a decoded screenshot - in the app
//an Avalonia Bitmap, which is an allocation outside the managed heap. The
//library rebuilds its whole grid on every visit, so without an owner a session
//of opening and closing the sheet leaves one picture per played game behind
//each time it is opened. This is that owner: the sheet registers what it
//decoded, and hands it all back in one call when the grid goes.
//
//Host-free (ADR-0123). It never sees a picture, only the allocation behind it -
//whatever can be handed back - so "the previous grid's covers are released when
//the grid is rebuilt" is unit-tested without a renderer, and the app's half is
//one Bitmap per cover created and released through the very calls this pins.
public sealed class CoverArtLedger : IDisposable
{
	private readonly List<IDisposable> _drawn = new();

	//Registers the allocation behind a cover the grid is about to draw. A tile
	//with no picture of its own registers nothing - null is what a generic cover
	//leaves here, and it is not a case the caller has to guard.
	public void Track(IDisposable? art)
	{
		if(art != null) {
			_drawn.Add(art);
		}
	}

	//Hands back ONE picture, for a tile the grid is dropping while it lives on.
	//
	//#1065: a query narrowing the grid, or a canonical title that stops matching
	//one, takes a tile off the sheet while the rest stay exactly where they are -
	//so the rebuild's Clear is not the call that owns that picture any more. The
	//ledger stops holding it in the same breath: a picture disposed here and still
	//registered would be handed back a second time by the next Clear.
	//
	//A tile whose cover is the console colour hands over null, and this is the
	//same "not a case the caller has to guard" as Track.
	public void Release(IDisposable? art)
	{
		if(art is null) {
			return;
		}
		_drawn.Remove(art);
		art.Dispose();
	}

	//Hands back every cover registered since the last Clear, and keeps whatever
	//is registered after this call: the caller rebuilds the grid in this order -
	//release the old, track the new - so a picture still being drawn is never
	//released by the rebuild that replaced its grid.
	public void Clear()
	{
		foreach(IDisposable art in _drawn) {
			art.Dispose();
		}
		_drawn.Clear();
	}

	//The sheet is not coming back: the covers it is still drawing go with it.
	public void Dispose()
	{
		Clear();
	}
}
