using System;

namespace Mesen.Logic
{
	//Host-free half of the debugger viewers' "Copy as MEP sheet cell" (PRD Phase 12
	//F12.2): formats one tile's `(tileData, palette)` key as the JSON object
	//`mep_build.py` reads out of a sheet sidecar's `cells[].tiles[]` entry
	//(ADR-0153, ADR-0172). Stateful partner: UI/Debugger/Utilities/HdPackCopyHelper,
	//which reads the bytes and the live palette out of the running core and puts
	//the result on the clipboard.
	//
	//The two hex fields are the sidecar's own contract, not a presentation choice:
	//`tile` is 32 uppercase hex characters (the 16 CHR bytes of the tile shape) and
	//`palette` is 8 (four NES palette bytes, `FF` leading a sprite's transparent
	//color 0). `mep_build._cell_crops` rejects an entry whose `tile` is not 32 hex
	//or whose `palette` is not 8, so a key that does not fit either field is never
	//emitted - an empty return means "this tile has no sheet-cell form".
	public static class MepSheetCell
	{
		public const int TileDataLength = 32;
		public const int PaletteLength = 8;

		//ADR-0172 §2: `index` is the absolute CHR index, written only when it is
		//known (a CHR ROM game). A CHR RAM tile leaves the field out - the readers
		//treat a missing index as "CHR RAM game, or a pack recorded before the ADR".
		public const int UnknownIndex = -1;

		//The entry's text, in the same shape a sidecar writes it (`json.dumps` of the
		//object): field order `tile`, `palette`, `index`, on one line so it pastes
		//into a `tiles[]` entry as a single value. ADR-0172's example is the same
		//object pretty-printed; JSON whitespace is not part of the format, the fields
		//are. Returns "" when the key cannot be a sheet cell, which is the callers'
		//signal to leave the clipboard alone (the inherited Copy tile does the same
		//for an unsupported memory type).
		public static string Format(string tileData, string palette, int tileIndex = UnknownIndex)
		{
			string data = (tileData ?? "").Trim().ToUpperInvariant();
			string pal = (palette ?? "").Trim().ToUpperInvariant();

			if(!IsHex(data, TileDataLength) || !IsHex(pal, PaletteLength)) {
				return "";
			}

			if(tileIndex >= 0) {
				return $"{{\"tile\": \"{data}\", \"palette\": \"{pal}\", \"index\": {tileIndex}}}";
			}
			return $"{{\"tile\": \"{data}\", \"palette\": \"{pal}\"}}";
		}

		//ADR-0216 OPEN 1(b): the clipboard carries the whole cell, **unplaced** -
		//`count` and `tiles[]`, and deliberately no `index`, `x` or `y`. Those three
		//are the sheet's arithmetic, not the tile's: which slot is free depends on
		//`columns`/`cell`/`gutter` and on how many cells the sidecar already holds,
		//and the emulator never opens the artist's tree to find out. `scripts/mep_add_cell.py`
		//is what places it.
		//
		//The one trap the ADR flags: `index` means two different things in a
		//whole-cell document. The cell's own `index` is its ordinal in its sheet and
		//is absent here on purpose; the `index` inside `tiles[]` is the tile's
		//absolute CHR index (ADR-0172 §2) and is the one this text carries. The
		//placer sets the first and never touches the second.
		//
		//One tile per cell: a 16px-tall sprite's second half is a second cell, not a
		//second entry, because `mep_build._cell_crops` lays a cell's `tiles[]` out
		//row-major 2x2 - entry 1 would land to the *right* of entry 0, not below it.
		public static string FormatCell(string tileData, string palette, int tileIndex = UnknownIndex)
		{
			string entry = Format(tileData, palette, tileIndex);
			if(entry.Length == 0) {
				return "";
			}
			return "{\"count\": 1, \"tiles\": [" + entry + "]}";
		}

		private static bool IsHex(string text, int length)
		{
			if(text.Length != length) {
				return false;
			}
			foreach(char c in text) {
				bool hex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
				if(!hex) {
					return false;
				}
			}
			return true;
		}
	}
}
