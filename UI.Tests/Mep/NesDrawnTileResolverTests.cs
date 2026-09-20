using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Mep;

//ADR-0215, decided 2026-09-19 (issue #341). Every case here is built from one of
//the ADR's own four measurements, and every one of them FAILS under the old
//behaviour, which was `_chrPages` as the paused emulator held it - the number
//the old path produced is asserted against explicitly, so a regression to it
//cannot pass.
//
//The traces are synthetic on purpose: `_scanlineVideoRamAddr` and
//`_scanlineChrBankOffsets` are two plain arrays of uint32, so the decision they
//feed is testable without a ROM, a save state or a native core. The in-process
//reproduction against the real save states is
//UI.HeadlessTests/ChrBankDiagnosticTests.cs.
public class NesDrawnTileResolverTests
{
	private const int Pages = NesDrawnTileResolver.PpuPageCount;
	private const int Scanlines = NesDrawnTileResolver.VisibleScanlines;

	//A frame with no scroll at all: scanline N draws nametable 0's row N/8,
	//starting at column 0. That is the shape of every one of the ADR's four save
	//states.
	private static uint[] UnscrolledScrollTrace()
	{
		uint[] trace = new uint[Scanlines];
		for(int scanline = 0; scanline < Scanlines; scanline++) {
			int coarseY = scanline / 8;
			int fineY = scanline % 8;
			trace[scanline] = (uint)((coarseY << 5) | (fineY << 12));
		}
		return trace;
	}

	//A CHR trace where every visible scanline maps the two 4 KB pattern-table
	//windows ($0000 and $1000 of PPU space) onto two CHR ROM banks - the shape
	//an MMC1/MMC3 mapping has. `bankAt(scanline)` gives the background window's
	//bank, which is the one every measurement in ADR-0215 turns on.
	private static uint[] PatternTableTrace(uint spriteBank, Func<int, uint> backgroundBankAt)
	{
		uint[] trace = new uint[Scanlines * Pages];
		for(int scanline = 0; scanline < Scanlines; scanline++) {
			uint background = backgroundBankAt(scanline);
			for(int page = 0; page < Pages; page++) {
				trace[scanline * Pages + page] = page < 0x10
					? spriteBank + (uint)(page * 0x100)
					: background + (uint)((page - 0x10) * 0x100);
			}
		}
		return trace;
	}

	private static uint[] PatternTableTrace(uint spriteBank, uint backgroundBank)
	{
		return PatternTableTrace(spriteBank, _ => backgroundBank);
	}

	//Dr. Mario (1990), MMC1, 32 KB CHR: the frame is paused in vblank, where the
	//game has already written the NEXT MMC1 CHR bank, so `_chrPages` answers CHR
	//$01000 for the background pattern table while all 240 visible scanlines drew
	//under CHR $00000. Cell (0,0)'s tile is at PPU $1F C0; the old path named it
	//508, the control experiment proved 252.
	[Fact]
	public void Dr_Mario_names_the_index_the_runtime_drew_not_the_one_the_pause_holds()
	{
		//The paused mapping, which is what GetAbsoluteAddress answered with.
		uint[] paused = PatternTableTrace(0x00000, 0x01000);
		int ppuTileAddress = 0x1FC0;
		int pausedIndex = (int)(paused[0 * Pages + (ppuTileAddress >> 8)] + (uint)(ppuTileAddress & 0xFF)) / 16;
		Assert.Equal(508, pausedIndex);

		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), PatternTableTrace(0x00000, 0x00000), tileMapAddress: 0x2000, ppuTileAddress: ppuTileAddress);

		Assert.Equal(NesDrawnTileStatus.Resolved, drawn.Status);
		Assert.Equal(252, drawn.AbsoluteAddress / 16);
		Assert.NotEqual(508, drawn.AbsoluteAddress / 16);
		Assert.Equal(0, drawn.Scanline);
	}

	//Lemmings (1993), MMC1, 128 KB CHR: scanlines 0-206 drew under CHR $1A000,
	//207-239 under CHR $00000, and the pause holds the lower band's. A cell in the
	//upper band is 0x1A01, not 1.
	[Fact]
	public void Lemmings_upper_band_is_named_from_its_own_bank()
	{
		uint[] chr = PatternTableTrace(0x00000, scanline => scanline <= 206 ? 0x1A000u : 0x00000u);

		//Tile $01 of the background pattern table at $1000, on row 0 - drawn only
		//by scanlines 0-7, all of them in the upper band. The pause holds the lower
		//band's CHR $00000, which is where the copy's old index 1 came from.
		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), chr, tileMapAddress: 0x2000, ppuTileAddress: 0x1010);

		Assert.Equal(NesDrawnTileStatus.Resolved, drawn.Status);
		Assert.Equal(0x1A01, drawn.AbsoluteAddress / 16);
		Assert.NotEqual(1, drawn.AbsoluteAddress / 16);
	}

	//Ninja Gaiden (1989), MMC3, 128 KB CHR: scanlines 0-146 drew under CHR
	//$1E000, 147-239 under CHR $10000, and the pause holds $10000. A cell in the
	//upper band is 0x1EFA, not 4346 ($10FA).
	[Fact]
	public void Ninja_Gaiden_upper_band_is_named_from_its_own_bank()
	{
		uint[] chr = PatternTableTrace(0x00000, scanline => scanline <= 146 ? 0x1E000u : 0x10000u);

		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), chr, tileMapAddress: 0x2000, ppuTileAddress: 0x1FA0);

		Assert.Equal(NesDrawnTileStatus.Resolved, drawn.Status);
		Assert.Equal(0x1EFA, drawn.AbsoluteAddress / 16);
		Assert.NotEqual(4346, drawn.AbsoluteAddress / 16);
	}

	//OPEN 1 (a)'s second half: an empty set of drawing scanlines refuses. A
	//nametable row the frame never put on screen (here nametable 1 under a frame
	//that never scrolled off nametable 0) has no drawing scanline at all.
	[Fact]
	public void A_cell_no_scanline_drew_is_refused()
	{
		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), PatternTableTrace(0x00000, 0x00000), tileMapAddress: 0x2800, ppuTileAddress: 0x1FC0);

		Assert.Equal(NesDrawnTileStatus.NotDrawnThisFrame, drawn.Status);
		Assert.True(drawn.IsRefusal);
		Assert.Equal(-1, drawn.AbsoluteAddress);
		Assert.Contains("no visible scanline", drawn.Reason);
	}

	//OPEN 1 (a)'s third half: members that disagree refuse too. A row whose eight
	//scanlines straddle a mid-frame bank split has no single index.
	[Fact]
	public void A_cell_whose_scanlines_disagree_is_refused()
	{
		uint[] chr = PatternTableTrace(0x00000, scanline => scanline < 4 ? 0x1E000u : 0x10000u);

		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), chr, tileMapAddress: 0x2000, ppuTileAddress: 0x1FA0);

		Assert.Equal(NesDrawnTileStatus.BanksDisagree, drawn.Status);
		Assert.True(drawn.IsRefusal);
		Assert.Contains("different CHR banks", drawn.Reason);
	}

	//The inversion has to survive scroll, which is the whole reason option (b)
	//("use row * 8") was not the one picked. With the screen scrolled 100 pixels
	//down, nametable 0 row 20 is drawn by scanlines 60-67, not 160-167.
	[Fact]
	public void A_scrolled_frame_finds_the_scanlines_that_really_drew_the_row()
	{
		uint[] scroll = new uint[Scanlines];
		for(int scanline = 0; scanline < Scanlines; scanline++) {
			int pixel = scanline + 100;
			int coarseY = (pixel / 8) % 30;
			int nametableY = ((pixel / 8) / 30) & 0x01;
			scroll[scanline] = (uint)((coarseY << 5) | (nametableY << 11) | ((pixel % 8) << 12));
		}

		List<int> scanlines = NesDrawnTileResolver.ScanlinesThatDrew(scroll, nametable: 0, row: 20, column: 5);

		Assert.Equal(new List<int> { 60, 61, 62, 63, 64, 65, 66, 67 }, scanlines);
		//Option (b) would have looked at scanline 160, which drew row 32 % 30 = 2
		//of nametable 2 - a different row entirely.
		Assert.DoesNotContain(160, scanlines);
	}

	//Horizontal scroll: the 33 columns a scanline fetches wrap across the two
	//horizontal nametables, so a cell at the far left of nametable 1 is drawn by a
	//scanline that started near the right edge of nametable 0.
	[Fact]
	public void A_horizontally_scrolled_scanline_covers_the_next_nametable()
	{
		uint[] scroll = new uint[Scanlines];
		for(int scanline = 0; scanline < Scanlines; scanline++) {
			//Coarse X 20 in nametable 0: columns 20..31 of nametable 0 and 0..20 of
			//nametable 1.
			scroll[scanline] = (uint)(((scanline / 8) << 5) | 20);
		}

		Assert.Contains(0, NesDrawnTileResolver.ScanlinesThatDrew(scroll, nametable: 1, row: 0, column: 3));
		Assert.Empty(NesDrawnTileResolver.ScanlinesThatDrew(scroll, nametable: 1, row: 0, column: 25));
		Assert.Empty(NesDrawnTileResolver.ScanlinesThatDrew(scroll, nametable: 0, row: 0, column: 5));
	}

	//OPEN 3, the Sprite Viewer half: a sprite has a Y, so the same rule applies to
	//it. A sprite at Y=10 is fetched for scanlines 11-18.
	[Fact]
	public void A_sprite_is_named_from_the_scanlines_that_fetched_it()
	{
		uint[] chr = PatternTableTrace(0x00000, 0x00000);
		for(int scanline = 0; scanline < 100; scanline++) {
			for(int page = 0; page < 0x10; page++) {
				chr[scanline * Pages + page] = 0x08000u + (uint)(page * 0x100);
			}
		}

		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveSpriteTile(chr, spriteY: 10, spriteHeight: 8, ppuTileAddress: 0x0040);
		Assert.Equal(NesDrawnTileStatus.Resolved, drawn.Status);
		Assert.Equal(0x804, drawn.AbsoluteAddress / 16);
		Assert.Equal(11, drawn.Scanline);

		//A 16px sprite straddling the split has no single index either.
		NesDrawnTileAddress straddling = NesDrawnTileResolver.ResolveSpriteTile(chr, spriteY: 90, spriteHeight: 16, ppuTileAddress: 0x0040);
		Assert.Equal(NesDrawnTileStatus.BanksDisagree, straddling.Status);

		//A sprite parked off the bottom of the screen was never fetched.
		NesDrawnTileAddress offscreen = NesDrawnTileResolver.ResolveSpriteTile(chr, spriteY: 0xF0, spriteHeight: 8, ppuTileAddress: 0x0040);
		Assert.Equal(NesDrawnTileStatus.NotDrawnThisFrame, offscreen.Status);
	}

	//OPEN 3, the Tile Viewer half: no row and no frame context, so it refuses -
	//and the refusal says where the artist can pick the tile instead.
	[Fact]
	public void The_tile_viewer_refuses_because_it_has_no_frame_context()
	{
		NesDrawnTileAddress drawn = NesDrawnTileResolver.NoFrameContext();
		Assert.Equal(NesDrawnTileStatus.NoFrameContext, drawn.Status);
		Assert.True(drawn.IsRefusal);
		Assert.Contains("Tilemap or Sprite Viewer", drawn.Reason);
	}

	//A CHR RAM game's pages are not CHR ROM, so there is no bank in the key and
	//nothing to refuse - the caller falls back to the paused resolution, which for
	//an unbanked CHR RAM tile is the same answer.
	[Fact]
	public void A_chr_ram_page_is_not_a_refusal()
	{
		uint[] chr = new uint[Scanlines * Pages];
		for(int i = 0; i < chr.Length; i++) {
			chr[i] = NesDrawnTileResolver.NotChrRomPage;
		}

		NesDrawnTileAddress drawn = NesDrawnTileResolver.ResolveTilemapTile(
			UnscrolledScrollTrace(), chr, tileMapAddress: 0x2000, ppuTileAddress: 0x0040);

		Assert.Equal(NesDrawnTileStatus.NotChrRom, drawn.Status);
		Assert.False(drawn.IsRefusal);
	}
}
