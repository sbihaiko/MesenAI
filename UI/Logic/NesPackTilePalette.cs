using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic
{
	//ADR-0215 / issue #342. Host-free half of the palette side of the debugger
	//viewers' copy actions.
	//
	//The copy reads the tile's palette out of live PPU palette RAM at the moment
	//of the copy. A pack's rules are keyed on the palette that was live when each
	//tile was *recorded*. On Metroid the two never intersect for the tiles the
	//artist wanted: the copy hands out `0F361506`, every rule the pack holds for
	//those bitmaps is keyed `0F0F0F0F`, and six verbatim pastes built clean,
	//linted clean and changed no pixel.
	//
	//So the copy asks the loaded pack which palettes it keys the tile under, and
	//answers one of these ways - never a plausible key it knows cannot match:
	//
	//  - the live palette is one of them (or the pack keys it with a defaultTile
	//    wildcard, which matches any palette): keep the live palette;
	//  - the pack keys it under exactly one other palette that palette RAM holds
	//    now: name that one, and say so, because that is the key a paste has to
	//    carry to match;
	//  - the pack holds several that palette RAM holds and the live palette is
	//    none of them: refuse and list them, because nothing says which one the
	//    paste should carry;
	//  - the pack holds no rule for the tile (ADR-0215, amendment of 2026-09-24):
	//    keep the live palette, the one the runtime asks for, and say that the
	//    paste adds a new key;
	//  - every palette the pack keys it under is one palette RAM does not hold
	//    (#431: the fade a bootstrap recording saw): keep the live palette, the one
	//    the runtime asks for, and say so.
	public enum NesPackPaletteStatus
	{
		//No pack loaded, so there is nothing to check the live palette against.
		Unchecked,
		//The live palette is one the pack keys this tile under.
		LiveMatches,
		//The pack keys this tile under one palette, and it is not the live one.
		Substituted,
		//The pack holds no rule for this tile at all: the live palette is kept.
		NoRule,
		//Several rules, none of them the live palette.
		Ambiguous,
		//The pack keys this tile only under palettes palette RAM does not hold, so
		//no pasted copy of them can match on this frame: the live palette is kept.
		RecordedNotDrawn
	}

	public readonly struct NesPackPaletteVerdict
	{
		public NesPackPaletteStatus Status { get; }
		//The palette the copy should carry, or 0 when it refuses.
		public uint Palette { get; }
		//One clause, en-US, for the receipt.
		public string Reason { get; }

		public NesPackPaletteVerdict(NesPackPaletteStatus status, uint palette, string reason)
		{
			Status = status;
			Palette = palette;
			Reason = reason;
		}

		public bool IsRefusal => Status == NesPackPaletteStatus.Ambiguous;
	}

	public static class NesPackTilePalette
	{
		//HdTileKey::GetKey(true)'s own wildcard: a defaultTile rule is matched with
		//the palette bits masked out, so it matches whatever is live. The Core
		//export emits it in place of such a rule's recorded palette.
		public const uint DefaultTileWildcard = 0xFFFFFFFF;

		//`packPalettes` is null when there is no pack to ask (the export's -1), and
		//empty when the pack holds no rule for this tile.
		public static NesPackPaletteVerdict Resolve(uint livePalette, IReadOnlyList<uint>? packPalettes, IReadOnlyCollection<uint> framePalettes)
		{
			if(packPalettes == null) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.Unchecked, livePalette, "");
			}
			//ADR-0215, amendment of 2026-09-24: no rule is not a refusal. The runtime
			//asks for the tile under the live palette, so a new key carrying it does
			//match (#431's E2E: 274 432 magenta pixels on Tetris 2).
			if(packPalettes.Count == 0) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.NoRule, livePalette,
					$"the loaded pack holds no rule for this tile, so the live {livePalette:X8} is kept and the paste adds a new key");
			}
			foreach(uint palette in packPalettes) {
				if(palette == livePalette || palette == DefaultTileWildcard) {
					return new NesPackPaletteVerdict(NesPackPaletteStatus.LiveMatches, livePalette, "");
				}
			}
			//#431: a recorded palette is a candidate only when palette RAM holds it for
			//this layer now. A bootstrap recording keys most tiles under the fade it
			//saw (Tetris 2: 474 of 518 rules under 0F0F0F0F); a key carrying a palette
			//the frame cannot draw builds, lints and changes no pixel. The runtime asks
			//for the tile under the live palette, so with no drawable candidate that is
			//the key to hand out, and the receipt says why.
			List<uint> drawable = packPalettes.Where(framePalettes.Contains).Distinct().ToList();
			if(drawable.Count == 0) {
				string recorded = string.Join(", ", packPalettes.Distinct().Select(p => p.ToString("X8")));
				return new NesPackPaletteVerdict(NesPackPaletteStatus.RecordedNotDrawn, livePalette,
					$"the pack keys this tile only under {recorded}, which palette RAM does not hold now, so the live " +
					$"{livePalette:X8} is kept and the paste adds a new key");
			}
			if(drawable.Count == 1) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.Substituted, drawable[0],
					$"the pack keys this tile under {drawable[0]:X8}, not the {livePalette:X8} live in palette RAM now");
			}
			string list = string.Join(", ", drawable.Select(p => p.ToString("X8")));
			return new NesPackPaletteVerdict(NesPackPaletteStatus.Ambiguous, 0,
				$"the pack keys this tile under {drawable.Count} palettes palette RAM holds ({list}) and none is the " +
				$"{livePalette:X8} live in palette RAM now, so nothing here says which one the paste should carry");
		}

		//The palette word as HdTileKey::PaletteColors packs it: color 0 in the high
		//byte, then colors 1-3. A sprite's color 0 is transparent, so it is FF.
		public static uint PaletteWord(UInt32[] rawPalette, int paletteIndex, bool forSprite)
		{
			int baseIndex = forSprite ? (paletteIndex + 4) * 4 : paletteIndex * 4;
			uint color0 = forSprite ? 0xFFu : (rawPalette[0] & 0xFF);
			return (color0 << 24) | ((rawPalette[baseIndex + 1] & 0xFF) << 16) | ((rawPalette[baseIndex + 2] & 0xFF) << 8) | (rawPalette[baseIndex + 3] & 0xFF);
		}

		//The four palettes one layer can draw with right now, packed as above.
		public static uint[] FramePalettes(UInt32[] rawPalette, bool forSprite)
		{
			return Enumerable.Range(0, 4).Select(i => PaletteWord(rawPalette, i, forSprite)).ToArray();
		}
	}
}
