using System;
using System.Collections.Generic;

namespace Mesen.Logic
{
	//ADR-0215 (decided 2026-09-19) / issue #341. Host-free half of the debugger
	//viewers' copy actions: given the two per-scanline traces ADR-0169 keeps, say
	//which absolute CHR ROM address a tile had *at the scanline that drew it*,
	//rather than under `_chrPages` as the paused emulator happens to hold it.
	//
	//The rule is one rule, under one name, in all three viewers (ADR-0215 OPEN 3):
	//a PPU-space tile address is named with the mapping that drew it, and where
	//the drawing scanline is not knowable the action refuses and says why.
	//
	//  - Tilemap Viewer: the drawing scanlines of a nametable cell come from
	//    inverting `_scanlineVideoRamAddr` (OPEN 1 option (a)); the first one
	//    names the tile, and an empty set or a set whose members disagree about
	//    the CHR page is a refusal.
	//  - Sprite Viewer: a sprite has a Y, so its scanlines are known.
	//  - Tile Viewer: no row and no frame context, so it refuses - unless its
	//    source is already an absolute CHR memory type, where `_chrPages` was
	//    never consulted and there is nothing to resolve.
	//
	//What this does NOT do is change the viewers' picture (OPEN 2: the Tilemap
	//Viewer stays a view of PPU memory). On a mid-frame-split game the image and
	//the key can therefore disagree; the receipt is what tells the artist so.
	//
	//Stateful partner: UI/Debugger/Utilities/HdPackCopyHelper.
	public enum NesDrawnTileStatus
	{
		//The tile's absolute CHR ROM address is known, from the mapping that drew it.
		Resolved,
		//No trace to consult (not NES, no frame rendered yet).
		NoTrace,
		//This viewer has no frame context at all - the Tile Viewer.
		NoFrameContext,
		//Nothing on screen drew this cell in the frame the trace describes.
		NotDrawnThisFrame,
		//The scanlines that drew it do not agree on the CHR bank behind the tile.
		BanksDisagree,
		//Those scanlines resolve the tile's PPU page to something that is not CHR
		//ROM (CHR RAM, mapper RAM): there is no bank in the key, nothing to fix.
		NotChrRom,
		//Issue #419: the trace predates the last state load or reset - no frame
		//has been drawn since, so it does not describe the frame on screen.
		NotDrawnSinceLoad
	}

	//Issue #419: what the core says about its per-scanline trace, as
	//`GetNesScanlineTrace` returns it. The values are the export's own.
	public enum NesScanlineTraceStatus
	{
		//Not an NES game, or no ROM loaded: nothing was written.
		Unavailable = 0,
		//The trace describes the last whole frame the PPU drew.
		Current = 1,
		//No whole frame has been drawn since the last state load or reset, so
		//the trace still holds whatever was there before it.
		NotDrawnSinceLoad = 2
	}

	public readonly struct NesDrawnTileAddress
	{
		public NesDrawnTileStatus Status { get; }
		//The absolute CHR ROM byte address, or -1.
		public int AbsoluteAddress { get; }
		//The scanline the address was read from, or -1.
		public int Scanline { get; }
		//One clause, en-US, ready to be pasted into the receipt after "because".
		public string Reason { get; }

		public NesDrawnTileAddress(NesDrawnTileStatus status, int absoluteAddress, int scanline, string reason)
		{
			Status = status;
			AbsoluteAddress = absoluteAddress;
			Scanline = scanline;
			Reason = reason;
		}

		public bool IsRefusal => Status != NesDrawnTileStatus.Resolved && Status != NesDrawnTileStatus.NotChrRom;
	}

	public static class NesDrawnTileResolver
	{
		public const int VisibleScanlines = 240;
		//$0000-$1FFF of PPU space, one entry per 256-byte page - the granularity
		//BaseMapper::GetChrPageOffsets publishes, which covers every ROM-backed
		//mapper's own bank size with one accessor.
		public const int PpuPageCount = 0x20;
		//GetChrPageOffsets' own "this page is not CHR ROM" marker.
		public const uint NotChrRomPage = 0xFFFFFFFF;

		//A scanline fetches 33 tile columns: the 32 it shows plus one more for the
		//fine-X shift.
		private const int ColumnsFetchedPerScanline = 33;
		//The horizontal tile space of the two horizontally adjacent nametables.
		private const int HorizontalTileSpace = 64;

		//ADR-0215 OPEN 1 (a): invert `_scanlineVideoRamAddr`. Each entry is the
		//loopy `v` that governed that scanline, captured at cycle 257, so its coarse
		//Y / nametable Y are the row that scanline drew and its coarse X /
		//nametable X are where that row's fetches started. A cell is drawn by a
		//scanline when the rows match and the cell falls inside the 33 columns the
		//scanline fetched, wrapping across the two horizontal nametables.
		//
		//`nametable` is 0-3 as the Tilemap Viewer's tabs number them (bit 0 = X,
		//bit 1 = Y), which is also `v`'s own bit 10 / bit 11 layout. Under
		//mirroring two tabs show the same memory but only one of them is the
		//nametable `v` names, so the mirror tab has no drawing scanline - a
		//refusal, not a guess.
		public static List<int> ScanlinesThatDrew(uint[] scrollTrace, int nametable, int row, int column)
		{
			List<int> scanlines = new();
			if(scrollTrace == null || scrollTrace.Length < VisibleScanlines) {
				return scanlines;
			}
			if(nametable < 0 || nametable > 3 || row < 0 || row > 29 || column < 0 || column > 31) {
				return scanlines;
			}

			int target = ((nametable & 0x01) * 32) + column;
			int targetRowY = (nametable >> 1) & 0x01;

			for(int scanline = 0; scanline < VisibleScanlines; scanline++) {
				uint v = scrollTrace[scanline];
				int coarseY = (int)((v >> 5) & 0x1F);
				int nametableY = (int)((v >> 11) & 0x01);
				if(coarseY != row || nametableY != targetRowY) {
					continue;
				}
				int start = (int)(((v >> 10) & 0x01) * 32 + (v & 0x1F));
				int offset = (target - start) & (HorizontalTileSpace - 1);
				if(offset < ColumnsFetchedPerScanline) {
					scanlines.Add(scanline);
				}
			}
			return scanlines;
		}

		//A tilemap cell, named by its PPU nametable address ($2000-$2FFF as
		//NesPpuTools::GetTileInfo builds it) and the PPU-space address of the tile
		//it points at ($0000-$1FFF).
		public static NesDrawnTileAddress ResolveTilemapTile(uint[] scrollTrace, uint[] chrBankTrace, int tileMapAddress, int ppuTileAddress)
		{
			if(scrollTrace == null || chrBankTrace == null) {
				return NoTrace();
			}
			int nametable = (tileMapAddress >> 10) & 0x03;
			int row = (tileMapAddress & 0x3FF) / 32;
			int column = tileMapAddress & 0x1F;
			List<int> scanlines = ScanlinesThatDrew(scrollTrace, nametable, row, column);
			if(scanlines.Count == 0) {
				return new NesDrawnTileAddress(NesDrawnTileStatus.NotDrawnThisFrame, -1, -1,
					$"no visible scanline of the last frame drew nametable {nametable} row {row}, column {column}");
			}
			return ResolveAtScanlines(chrBankTrace, scanlines, ppuTileAddress);
		}

		//A sprite is fetched during cycles 257-320 of the scanline before the one it
		//appears on, which is the same cycle-257 point the traces are sampled at, so
		//`_scanlineChrBankOffsets[y + 1]` is the mapping its first row was fetched
		//under. `spriteY` is OAM's byte 0.
		public static NesDrawnTileAddress ResolveSpriteTile(uint[] chrBankTrace, int spriteY, int spriteHeight, int ppuTileAddress)
		{
			if(chrBankTrace == null) {
				return NoTrace();
			}
			List<int> scanlines = new();
			for(int offset = 0; offset < Math.Max(spriteHeight, 1); offset++) {
				int scanline = spriteY + 1 + offset;
				if(scanline >= 0 && scanline < VisibleScanlines) {
					scanlines.Add(scanline);
				}
			}
			if(scanlines.Count == 0) {
				return new NesDrawnTileAddress(NesDrawnTileStatus.NotDrawnThisFrame, -1, -1,
					$"a sprite at Y={spriteY} is off the visible screen, so no scanline fetched it");
			}
			return ResolveAtScanlines(chrBankTrace, scanlines, ppuTileAddress);
		}

		//The one entry the copy actions use (HdPackCopyHelper), for a viewer that
		//has a frame context: a tilemap cell when `tileMapAddress` is >= 0, else
		//a sprite at `spriteY`.
		//
		//Issue #419: the traces are not part of a save state, so a trace the core
		//marks NotDrawnSinceLoad is a well-formed record of the frame drawn BEFORE
		//the load - it resolves cleanly and names the wrong bank (F14.2: Dr.
		//Mario's cell (0,0) as 1276, not 252) or nothing at all (Super Mario
		//Bros., Zelda). It is refused here, before it can resolve anything.
		public static NesDrawnTileAddress Resolve(NesScanlineTraceStatus status, uint[] scrollTrace, uint[] chrBankTrace, int tileMapAddress, int spriteY, int spriteHeight, int ppuTileAddress)
		{
			if(status == NesScanlineTraceStatus.NotDrawnSinceLoad) {
				return NotDrawnSinceLoad();
			}
			if(status != NesScanlineTraceStatus.Current) {
				return NoTrace();
			}
			if(tileMapAddress >= 0) {
				return ResolveTilemapTile(scrollTrace, chrBankTrace, tileMapAddress, ppuTileAddress);
			}
			return ResolveSpriteTile(chrBankTrace, spriteY, spriteHeight, ppuTileAddress);
		}

		//ADR-0215 OPEN 3: the Tile Viewer reading PPU memory has neither a row nor a
		//frame context, so under the one rule it can only refuse.
		public static NesDrawnTileAddress NoFrameContext()
		{
			return new NesDrawnTileAddress(NesDrawnTileStatus.NoFrameContext, -1, -1,
				"the Tile Viewer has no row and no frame context, so nothing says which CHR bank drew this tile - " +
				"pick it in the Tilemap or Sprite Viewer, or point the source at CHR ROM directly");
		}

		//Issue #419. The receipt names the way out, because it is one step: any
		//whole frame drawn after the load gives the copy a trace of the frame on
		//screen.
		public static NesDrawnTileAddress NotDrawnSinceLoad()
		{
			return new NesDrawnTileAddress(NesDrawnTileStatus.NotDrawnSinceLoad, -1, -1,
				"no frame has been drawn since the last state load or reset, so nothing says which CHR bank drew " +
				"this tile - run one frame (the debugger's Run one frame, or unpause) and copy again");
		}

		public static NesDrawnTileAddress NoTrace()
		{
			return new NesDrawnTileAddress(NesDrawnTileStatus.NoTrace, -1, -1,
				"the core published no per-scanline CHR trace for this frame");
		}

		//The shared tail: every scanline in the set must agree on the CHR ROM offset
		//behind the tile's PPU page, and the first one names it. Disagreement is the
		//mid-frame split case ADR-0215 measured on Lemmings and Ninja Gaiden; where
		//it happens for a single cell there is no one right answer, so it refuses.
		public static NesDrawnTileAddress ResolveAtScanlines(uint[] chrBankTrace, IReadOnlyList<int> scanlines, int ppuTileAddress)
		{
			if(chrBankTrace == null || chrBankTrace.Length < VisibleScanlines * PpuPageCount) {
				return NoTrace();
			}
			if(ppuTileAddress < 0 || ppuTileAddress + 16 > PpuPageCount * 0x100) {
				return NoTrace();
			}

			int page = ppuTileAddress >> 8;
			uint first = 0;
			int firstScanline = -1;
			foreach(int scanline in scanlines) {
				if(scanline < 0 || scanline >= VisibleScanlines) {
					continue;
				}
				uint offset = chrBankTrace[scanline * PpuPageCount + page];
				if(firstScanline < 0) {
					first = offset;
					firstScanline = scanline;
				} else if(offset != first) {
					return new NesDrawnTileAddress(NesDrawnTileStatus.BanksDisagree, -1, -1,
						$"scanlines {firstScanline} and {scanline} drew this cell under different CHR banks " +
						$"(${first:X6} and ${offset:X6}), so no single index names it");
				}
			}
			if(firstScanline < 0) {
				return NoTrace();
			}
			if(first == NotChrRomPage) {
				return new NesDrawnTileAddress(NesDrawnTileStatus.NotChrRom, -1, firstScanline,
					"this PPU page is not CHR ROM, so the key carries no bank");
			}
			return new NesDrawnTileAddress(NesDrawnTileStatus.Resolved, (int)(first + (uint)(ppuTileAddress & 0xFF)), firstScanline, "");
		}
	}
}
