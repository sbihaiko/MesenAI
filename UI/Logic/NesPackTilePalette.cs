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
	//answers one of three ways - never a plausible key it knows cannot match:
	//
	//  - the live palette is one of them (or the pack keys it with a defaultTile
	//    wildcard, which matches any palette): keep the live palette;
	//  - the pack keys it under exactly one other palette: name that one, and say
	//    so, because that is the key a paste has to carry to match;
	//  - the pack holds no rule for the tile, or holds several and the live
	//    palette is none of them: refuse and say which, because "the pack does not
	//    hold this tile" is a better answer than a key that cannot match.
	public enum NesPackPaletteStatus
	{
		//No pack loaded, so there is nothing to check the live palette against.
		Unchecked,
		//The live palette is one the pack keys this tile under.
		LiveMatches,
		//The pack keys this tile under one palette, and it is not the live one.
		Substituted,
		//The pack holds no rule for this tile at all.
		NoRule,
		//Several rules, none of them the live palette.
		Ambiguous
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

		public bool IsRefusal => Status == NesPackPaletteStatus.NoRule || Status == NesPackPaletteStatus.Ambiguous;
	}

	public static class NesPackTilePalette
	{
		//HdTileKey::GetKey(true)'s own wildcard: a defaultTile rule is matched with
		//the palette bits masked out, so it matches whatever is live. The Core
		//export emits it in place of such a rule's recorded palette.
		public const uint DefaultTileWildcard = 0xFFFFFFFF;

		//`packPalettes` is null when there is no pack to ask (the export's -1), and
		//empty when the pack holds no rule for this tile.
		public static NesPackPaletteVerdict Resolve(uint livePalette, IReadOnlyList<uint>? packPalettes)
		{
			if(packPalettes == null) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.Unchecked, livePalette, "");
			}
			if(packPalettes.Count == 0) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.NoRule, 0,
					"the loaded pack holds no rule for this tile, so no palette can make the paste match at run time");
			}
			foreach(uint palette in packPalettes) {
				if(palette == livePalette || palette == DefaultTileWildcard) {
					return new NesPackPaletteVerdict(NesPackPaletteStatus.LiveMatches, livePalette, "");
				}
			}
			if(packPalettes.Count == 1) {
				return new NesPackPaletteVerdict(NesPackPaletteStatus.Substituted, packPalettes[0],
					$"the pack keys this tile under {packPalettes[0]:X8}, not the {livePalette:X8} live in palette RAM now");
			}
			string list = string.Join(", ", packPalettes.Select(p => p.ToString("X8")));
			return new NesPackPaletteVerdict(NesPackPaletteStatus.Ambiguous, 0,
				$"the pack keys this tile under {packPalettes.Count} palettes ({list}) and none is the {livePalette:X8} " +
				"live in palette RAM now, so nothing here says which one the paste should carry");
		}
	}
}
