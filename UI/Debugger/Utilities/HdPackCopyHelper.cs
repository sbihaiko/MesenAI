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
	public static class HdPackCopyHelper
	{
		public static bool IsActionAllowed(MemoryType type)
		{
			return type switch {
				MemoryType.NesPpuMemory => true,
				MemoryType.NesChrRam => true,
				MemoryType.NesChrRom => true,
				_ => false,
			};
		}

		public static string ToHdPackFormat(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite)
		{
			if(!TryReadTileKey(tileAddr, rawPalette, paletteIndex, forSprite, out string tileData, out string palette, out int tileIndex)) {
				return "";
			}

			//The `<tile>` key's own field: a CHR ROM game keys by index, a CHR RAM game
			//by the 16 bytes of the tile shape (HdPackTileInfo::ToString).
			string key = tileIndex >= 0 ? tileIndex.ToString("X2") : tileData;
			return key + "," + palette;
		}

		//PRD Phase 12 F12.2: the same key as one MEP sheet sidecar entry
		//(`cells[].tiles[]`), the form `mep_build.py` reads back (ADR-0153, ADR-0172 §2).
		//The inherited Copy tile hands the author a `<tile>` line; this hands them the
		//object the sheet carries, so a key picked by eye in the viewer pastes into a
		//sheet project without anyone reading hires.txt.
		public static string ToMepSheetCell(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite)
		{
			if(!TryReadTileKey(tileAddr, rawPalette, paletteIndex, forSprite, out string tileData, out string palette, out int tileIndex)) {
				return "";
			}

			return MepSheetCell.Format(tileData, palette, tileIndex);
		}

		//One reader for both copy formats, so the two can never disagree about which
		//bytes a tile has or which palette word goes beside it. `tileIndex` is the
		//absolute CHR index on a CHR ROM game (ADR-0172 §4: what HdPackTileInfo holds,
		//computed by HdBuilderPpu as AbsoluteTileAddr / 16) and -1 on CHR RAM, where the
		//field means nothing.
		private static bool TryReadTileKey(AddressInfo tileAddr, UInt32[] rawPalette, int paletteIndex, bool forSprite, out string tileData, out string palette, out int tileIndex)
		{
			tileData = "";
			palette = "";
			tileIndex = MepSheetCell.UnknownIndex;

			if(tileAddr.Type == MemoryType.NesPpuMemory) {
				tileAddr = DebugApi.GetAbsoluteAddress(tileAddr);
			}

			if(tileAddr.Type != MemoryType.NesChrRom && tileAddr.Type != MemoryType.NesChrRam) {
				return false;
			}
			//A tile is 16 bytes; a shape read past the end of CHR would be a silent
			//out-of-bounds read inside the core, so a key that does not fit is dropped.
			if(tileAddr.Address < 0 || tileAddr.Address + 16 > DebugApi.GetMemorySize(tileAddr.Type)) {
				return false;
			}

			if(tileAddr.Type == MemoryType.NesChrRom) {
				tileIndex = tileAddr.Address / 16;
			}

			StringBuilder sb = new StringBuilder();
			for(int i = 0; i < 16; i++) {
				sb.Append(DebugApi.GetMemoryValue(tileAddr.Type, (uint)(tileAddr.Address + i)).ToString("X2"));
			}
			tileData = sb.ToString();

			StringBuilder pal = new StringBuilder();
			if(forSprite) {
				pal.Append("FF");
				for(int i = 1; i < 4; i++) {
					pal.Append(rawPalette[(paletteIndex + 4) * 4 + i].ToString("X2"));
				}
			} else {
				pal.Append(rawPalette[0].ToString("X2")); //Always use color 0 for palette index 0
				for(int i = 1; i < 4; i++) {
					pal.Append(rawPalette[paletteIndex * 4 + i].ToString("X2"));
				}
			}
			palette = pal.ToString();

			return true;
		}

		public static void CopyToHdPackFormat(int address, MemoryType memoryType, UInt32[] palette, int paletteIndex, bool forSprite, bool isLargeSprite = false)
		{
			AddressInfo addr = new AddressInfo() { Address = address, Type = memoryType };
			string hdPackTile = HdPackCopyHelper.ToHdPackFormat(addr, palette, paletteIndex, forSprite);

			if(isLargeSprite && hdPackTile.Length > 0) {
				//Also copy the bottom tile's information to the clipboard
				addr.Address += 16;
				hdPackTile += Environment.NewLine + HdPackCopyHelper.ToHdPackFormat(addr, palette, paletteIndex, forSprite);
			}

			if(hdPackTile.Length > 0) {
				ApplicationHelper.GetMainWindow()?.Clipboard?.SetTextAsync(hdPackTile);
			}
		}

		public static void CopyAsMepSheetCell(int address, MemoryType memoryType, UInt32[] palette, int paletteIndex, bool forSprite, bool isLargeSprite = false)
		{
			AddressInfo addr = new AddressInfo() { Address = address, Type = memoryType };
			string cell = HdPackCopyHelper.ToMepSheetCell(addr, palette, paletteIndex, forSprite);

			if(isLargeSprite && cell.Length > 0) {
				//The second half of a 16px-tall sprite is its own tile, so it is its own
				//entry. One object per line and no comma between them: the entry the paste
				//lands next to decides whether a separator is needed, and a comma the
				//artist did not ask for is the one thing that breaks the sidecar's JSON.
				addr.Address += 16;
				cell += Environment.NewLine + HdPackCopyHelper.ToMepSheetCell(addr, palette, paletteIndex, forSprite);
			}

			if(cell.Length > 0) {
				ApplicationHelper.GetMainWindow()?.Clipboard?.SetTextAsync(cell);
			}
		}
	}
}
