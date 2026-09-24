using Avalonia;
using Avalonia.Input.Platform;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Debugger.Utilities
{
	//Which frame context the viewer can offer for the tile being copied
	//(ADR-0215 OPEN 3: one rule, three viewers, and the viewer that has no
	//context says so instead of guessing).
	public readonly struct HdPackCopyContext
	{
		public int TileMapAddress { get; }
		public int SpriteY { get; }
		public int SpriteHeight { get; }
		public bool HasFrameContext { get; }

		private HdPackCopyContext(int tileMapAddress, int spriteY, int spriteHeight, bool hasFrameContext)
		{
			TileMapAddress = tileMapAddress;
			SpriteY = spriteY;
			SpriteHeight = spriteHeight;
			HasFrameContext = hasFrameContext;
		}

		//The Tilemap Viewer: a nametable address, which the scroll trace can be
		//inverted against.
		public static HdPackCopyContext ForTilemap(int tileMapAddress) => new(tileMapAddress, -1, 0, true);
		//The Sprite Viewer: a Y, which names the scanlines the sprite was fetched on.
		public static HdPackCopyContext ForSprite(int spriteY, int spriteHeight) => new(-1, spriteY, spriteHeight, true);
		//The Tile Viewer: neither a row nor a frame.
		public static HdPackCopyContext None() => new(-1, -1, 0, false);
	}

	//What one copy did, so the caller can both put it on the clipboard and say so
	//out loud. Issue #340: across all 28 cold-read sessions nothing after the copy
	//said it had worked or which tile it took - the first confirmation was a
	//`build` line two steps later, by which point a wrong tile, a wrong slot and a
	//key lost to precedence all present as the same symptom, an unchanged frame.
	public readonly struct HdPackCopyResult
	{
		//The text put on the clipboard, or "" when the action refused.
		public string Text { get; }
		//One line for the OSD, always present - a refusal has to say why on screen
		//or it trades a silent wrong key for a silent nothing (ADR-0215).
		public string Receipt { get; }
		public bool Copied => Text.Length > 0;

		public HdPackCopyResult(string text, string receipt)
		{
			Text = text;
			Receipt = receipt;
		}
	}

	public static class HdPackCopyHelper
	{
		//ADR-0167's OSD toast is where the other debugger actions put their
		//feedback (ShortcutHandler's layer toggles, LiveRecordingSession's
		//failures), so the copy's receipt goes through the same seam - which also
		//means the headless HUD capture can see it.
		public const string OsdTitle = "MEP";

		public static bool IsActionAllowed(MemoryType type)
		{
			return type switch {
				MemoryType.NesPpuMemory => true,
				MemoryType.NesChrRam => true,
				MemoryType.NesChrRom => true,
				_ => false,
			};
		}

		public static string ToHdPackFormat(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite, HdPackCopyContext context)
		{
			if(!TryReadTileKey(tileAddr, rawPalette, paletteIndex, forSprite, context, out TileKey key)) {
				return "";
			}
			return Render(key, mepFormat: false);
		}

		//PRD Phase 12 F12.2: one MEP sheet cell, **unplaced** - `count` and `tiles[]`,
		//no `index`/`x`/`y` (ADR-0216 OPEN 1(b)). The inherited Copy tile hands the
		//author a `<tile>` line; this hands them the object the sheet carries, so a
		//key picked by eye in the viewer pastes into a sheet project without anyone
		//reading hires.txt.
		//
		//The slot is deliberately not filled in here. Choosing a sheet and a free
		//slot means reading the artist's `textures/sheets/*.json`, which the emulator
		//has no export for and no business doing: `scripts/mep_add_cell.py <pack>`
		//places this text. Pasting it into a `cells[]` by hand still works, which is
		//the point of leaving the payload a complete cell rather than a confirmation.
		public static string ToMepSheetCell(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite, HdPackCopyContext context)
		{
			if(!TryReadTileKey(tileAddr, rawPalette, paletteIndex, forSprite, context, out TileKey key)) {
				return "";
			}
			return Render(key, mepFormat: true);
		}

		private static string Render(TileKey key, bool mepFormat)
		{
			if(mepFormat) {
				return MepSheetCell.FormatCell(key.TileData, key.Palette, key.TileIndex);
			}
			//The `<tile>` key's own field: a CHR ROM game keys by index, a CHR RAM game
			//by the 16 bytes of the tile shape (HdPackTileInfo::ToString).
			return (key.TileIndex >= 0 ? key.TileIndex.ToString("X2") : key.TileData) + "," + key.Palette;
		}

		private struct TileKey
		{
			public string TileData;
			public string Palette;
			public int TileIndex;
			//Why the action refused, or the note a successful copy carries.
			public string Note;
			public int Scanline;
		}

		//One reader for both copy formats, so the two can never disagree about which
		//bytes a tile has or which palette word goes beside it. `TileIndex` is the
		//absolute CHR index on a CHR ROM game (ADR-0172 §4: what HdPackTileInfo holds,
		//computed by HdBuilderPpu as AbsoluteTileAddr / 16) and -1 on CHR RAM, where the
		//field means nothing.
		//
		//ADR-0215: a PPU-space address is resolved through the CHR mapping that DREW
		//the frame, never through `_chrPages` as the paused emulator holds it, and the
		//palette is checked against the rules the loaded pack actually keys this tile
		//under. Either check can refuse; `key.Note` then says why.
		private static bool TryReadTileKey(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite, HdPackCopyContext context, out TileKey key)
		{
			key = new TileKey() { TileData = "", Palette = "", TileIndex = MepSheetCell.UnknownIndex, Note = "", Scanline = -1 };

			if(tileAddr.Type == MemoryType.NesPpuMemory) {
				NesDrawnTileAddress drawn = ResolveDrawnAddress(tileAddr.Address, context);
				if(drawn.IsRefusal) {
					key.Note = drawn.Reason;
					return false;
				}
				key.Scanline = drawn.Scanline;
				if(drawn.Status == NesDrawnTileStatus.Resolved) {
					tileAddr = new AddressInfo() { Address = drawn.AbsoluteAddress, Type = MemoryType.NesChrRom };
				} else {
					//NotChrRom: no bank lives in this key, so the paused resolution is
					//the same answer the drawing scanline would have given.
					tileAddr = DebugApi.GetAbsoluteAddress(tileAddr);
				}
			}

			if(tileAddr.Type != MemoryType.NesChrRom && tileAddr.Type != MemoryType.NesChrRam) {
				key.Note = "this address is not backed by CHR ROM or CHR RAM";
				return false;
			}
			//A tile is 16 bytes; a shape read past the end of CHR would be a silent
			//out-of-bounds read inside the core, so a key that does not fit is dropped.
			if(tileAddr.Address < 0 || tileAddr.Address + 16 > DebugApi.GetMemorySize(tileAddr.Type)) {
				key.Note = $"a 16-byte tile at ${tileAddr.Address:X6} would read past the end of {tileAddr.Type}";
				return false;
			}

			if(tileAddr.Type == MemoryType.NesChrRom) {
				key.TileIndex = tileAddr.Address / 16;
			}

			byte[] bytes = new byte[16];
			StringBuilder sb = new StringBuilder();
			for(int i = 0; i < 16; i++) {
				bytes[i] = DebugApi.GetMemoryValue(tileAddr.Type, (uint)(tileAddr.Address + i));
				sb.Append(bytes[i].ToString("X2"));
			}
			key.TileData = sb.ToString();

			//#431: the frame's own palettes go with the live one, so a recorded palette
			//palette RAM does not hold (a fade the recording saw) is never handed out.
			uint live = NesPackTilePalette.PaletteWord(rawPalette, paletteIndex, forSprite);
			NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(
				live, DebugApi.GetNesHdPackTilePalettes(key.TileIndex, bytes, tileAddr.Type == MemoryType.NesChrRam),
				NesPackTilePalette.FramePalettes(rawPalette, forSprite));
			if(verdict.IsRefusal) {
				key.Note = verdict.Reason;
				return false;
			}
			key.Palette = verdict.Palette.ToString("X8");
			key.Note = verdict.Reason;

			return true;
		}

		private static NesDrawnTileAddress ResolveDrawnAddress(int ppuTileAddress, HdPackCopyContext context)
		{
			if(!context.HasFrameContext) {
				return NesDrawnTileResolver.NoFrameContext();
			}
			NesScanlineTraceStatus status = DebugApi.GetNesScanlineTrace(out UInt32[] scroll, out UInt32[] chrBank);
			return NesDrawnTileResolver.Resolve(status, scroll, chrBank, context.TileMapAddress, context.SpriteY, context.SpriteHeight, ppuTileAddress);
		}

		public static HdPackCopyResult CopyToHdPackFormat(int address, MemoryType memoryType, UInt32[] palette, int paletteIndex, bool forSprite, HdPackCopyContext context, bool isLargeSprite = false)
		{
			AddressInfo addr = new AddressInfo() { Address = address, Type = memoryType };
			return Copy(addr, palette, paletteIndex, forSprite, context, isLargeSprite, "HD pack tile", false);
		}

		public static HdPackCopyResult CopyAsMepSheetCell(int address, MemoryType memoryType, UInt32[] palette, int paletteIndex, bool forSprite, HdPackCopyContext context, bool isLargeSprite = false)
		{
			AddressInfo addr = new AddressInfo() { Address = address, Type = memoryType };
			return Copy(addr, palette, paletteIndex, forSprite, context, isLargeSprite, "MEP sheet cell", true);
		}

		private static HdPackCopyResult Copy(AddressInfo addr, UInt32[] palette, int paletteIndex, bool forSprite, HdPackCopyContext context, bool isLargeSprite, string what, bool mepFormat)
		{
			//The receipt is built from the same TileKey the clipboard text is, so it
			//can never describe a tile the clipboard does not carry.
			bool read = TryReadTileKey(addr, palette, paletteIndex, forSprite, context, out TileKey key);
			string text = read ? Render(key, mepFormat) : "";

			if(text.Length == 0) {
				string why = key.Note.Length > 0 ? key.Note : "this tile has no key";
				return new HdPackCopyResult("", $"{what} not copied: {why}");
			}

			if(isLargeSprite) {
				//The second half of a 16px-tall sprite is its own cell, not a second
				//`tiles[]` entry of the first: `mep_build._cell_crops` lays a cell's
				//entries out row-major 2x2, so entry 1 draws to the *right* of entry 0
				//rather than below it. One object per line and no comma between them:
				//the entry the paste lands next to decides whether a separator is
				//needed, and a comma the artist did not ask for is the one thing that
				//breaks the sidecar's JSON. `mep_add_cell.py` reads the two lines as
				//two cells for the same reason.
				AddressInfo bottom = new AddressInfo() { Address = addr.Address + 16, Type = addr.Type };
				if(TryReadTileKey(bottom, palette, paletteIndex, forSprite, context, out TileKey second)) {
					string secondText = Render(second, mepFormat);
					if(secondText.Length > 0) {
						text += Environment.NewLine + secondText;
					}
				}
			}

			ApplicationHelper.GetMainWindow()?.Clipboard?.SetTextAsync(text);

			string named = key.TileIndex >= 0 ? $"index {key.TileIndex}" : $"CHR RAM tile {key.TileData[..8]}";
			string from = key.Scanline >= 0 ? $", as scanline {key.Scanline} drew it" : "";
			string note = key.Note.Length > 0 ? $" - {key.Note}" : "";
			return new HdPackCopyResult(text, $"{what} copied: {named}, palette {key.Palette}{from}{note}");
		}

		//Issue #340: the receipt goes to the OSD at the moment of the copy, where
		//the other debugger actions put theirs. Every entry point calls this - the
		//Tile Viewer's and the Sprite Viewer's copies need it as much as the
		//Tilemap Viewer's.
		public static void Announce(HdPackCopyResult result)
		{
			if(result.Receipt.Length > 0) {
				DisplayMessageHelper.DisplayMessage(OsdTitle, result.Receipt);
			}
		}
	}
}
