using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Mep;

//ADR-0215 / issue #342. The Metroid case is the one the F12.2 cold read measured:
//the copy handed out 0F361506, every rule the pack held for those bitmaps was
//keyed 0F0F0F0F, and six verbatim pastes built clean, linted clean and changed no
//pixel. The old behaviour is "emit the live palette whatever the pack holds", and
//each case below asserts against exactly that.
public class NesPackTilePaletteTests
{
	private const uint MetroidLive = 0x0F361506;
	private const uint MetroidRecorded = 0x0F0F0F0F;

	//The background palettes palette RAM holds when the copy runs. #431: a
	//recorded palette is only substituted when it is one of these, so the
	//substitution cases below run on a frame that still holds the recorded
	//palettes (a fade caught mid-way), and the rest on one that does not.
	private static readonly uint[] MetroidFrame = { MetroidLive, 0x0F161A27, 0x0F2A1A0F, 0x0F301606 };
	private static readonly uint[] MetroidFadingFrame = { MetroidLive, MetroidRecorded, 0x0F301612, 0x0F2A1A0F };

	[Fact]
	public void No_pack_loaded_leaves_the_live_palette_alone()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, null, MetroidFrame);
		Assert.Equal(NesPackPaletteStatus.Unchecked, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
		Assert.False(verdict.IsRefusal);
	}

	[Fact]
	public void The_live_palette_is_kept_when_the_pack_keys_the_tile_under_it()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { 0x0F0F0F0F, MetroidLive }, MetroidFrame);
		Assert.Equal(NesPackPaletteStatus.LiveMatches, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
	}

	//Metroid: one recorded palette, and it is not the live one. The old behaviour
	//emitted 0F361506 - a key the renderer will never ask for.
	[Fact]
	public void Metroid_names_the_palette_the_pack_keys_the_tile_under()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { MetroidRecorded }, MetroidFadingFrame);
		Assert.Equal(NesPackPaletteStatus.Substituted, verdict.Status);
		Assert.Equal(MetroidRecorded, verdict.Palette);
		Assert.NotEqual(MetroidLive, verdict.Palette);
		Assert.Contains("0F0F0F0F", verdict.Reason);
		Assert.False(verdict.IsRefusal);
	}

	//ADR-0215, amendment of 2026-09-24 (the user's go-ahead: "aceito sua sugestao.
	//pode aplicar e rodar em paralelo"). #342 refused a tile the loaded pack
	//holds no rule for, on the premise that no key could match. #431's E2E
	//contradicted that premise: a new key carrying the live palette rendered
	//274 432 magenta pixels on Tetris 2, because the live palette is the one the
	//runtime asks for. The old behaviour here was a refusal (Palette 0,
	//IsRefusal true).
	[Fact]
	public void A_tile_the_pack_does_not_hold_copies_with_the_live_palette()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, Array.Empty<uint>(), MetroidFrame);
		Assert.Equal(NesPackPaletteStatus.NoRule, verdict.Status);
		Assert.False(verdict.IsRefusal);
		Assert.Equal(MetroidLive, verdict.Palette);
		//The receipt says the pack has no rule for the tile, names the palette that
		//went out, and says that pasting adds a new key.
		Assert.Contains("holds no rule", verdict.Reason);
		Assert.Contains("0F361506", verdict.Reason);
		Assert.Contains("adds a new key", verdict.Reason);
	}

	//The same for a sprite: the live word leads with FF (transparent colour 0),
	//and that word is the one handed out.
	[Fact]
	public void A_sprite_the_pack_does_not_hold_copies_with_its_live_sprite_palette()
	{
		const uint spriteLive = 0xFF162730;
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(spriteLive, Array.Empty<uint>(),
			new uint[] { 0xFF0F0F0F, spriteLive, 0xFF2A1A0F, 0xFF301606 });
		Assert.Equal(NesPackPaletteStatus.NoRule, verdict.Status);
		Assert.False(verdict.IsRefusal);
		Assert.Equal(spriteLive, verdict.Palette);
		Assert.Contains("FF162730", verdict.Reason);
	}

	[Fact]
	public void Several_recorded_palettes_and_no_match_is_refused_with_the_list()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { 0x0F0F0F0F, 0x0F301612 }, MetroidFadingFrame);
		Assert.Equal(NesPackPaletteStatus.Ambiguous, verdict.Status);
		Assert.True(verdict.IsRefusal);
		Assert.Contains("0F0F0F0F", verdict.Reason);
		Assert.Contains("0F301612", verdict.Reason);
	}

	//A defaultTile rule is keyed with the palette bits masked out
	//(HdTileKey::GetKey(true)), so it matches whatever is live - and the live
	//palette is then the right one to emit, not a substitute.
	[Fact]
	public void A_default_tile_wildcard_matches_whatever_is_live()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { MetroidRecorded, NesPackTilePalette.DefaultTileWildcard }, MetroidFrame);
		Assert.Equal(NesPackPaletteStatus.LiveMatches, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
	}

	//#431, Tetris 2 (1993), F14.2 re-score: the baseline pack keys 474 of its 518
	//<tile> rules under the all-black 0F0F0F0F it recorded while the title faded
	//in, including tile 0x1170. The frame the artist copies from is fully faded
	//in and draws 0F281807; no palette RAM slot holds 0F0F0F0F. The old
	//behaviour substituted 0F0F0F0F anyway - a key the runtime never asks for on
	//this frame (0 magenta pixels; the live key rendered 274 432).
	private const uint Tetris2Live = 0x0F281807;
	private const uint Tetris2Fade = 0x0F0F0F0F;
	private static readonly uint[] Tetris2Frame = { Tetris2Live, 0x0F262320, 0x0F262A12, 0x0F2A1A30 };

	[Fact]
	public void A_recorded_palette_the_frame_does_not_hold_is_not_substituted_431()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(Tetris2Live, new uint[] { Tetris2Fade }, Tetris2Frame);
		Assert.Equal(NesPackPaletteStatus.RecordedNotDrawn, verdict.Status);
		Assert.Equal(Tetris2Live, verdict.Palette);
		Assert.NotEqual(Tetris2Fade, verdict.Palette);
		Assert.False(verdict.IsRefusal);
		//The receipt names both palettes, so the artist can see what was kept and why.
		Assert.Contains("0F0F0F0F", verdict.Reason);
		Assert.Contains("0F281807", verdict.Reason);
	}

	//Several recorded palettes, none of them held by palette RAM: none can match on
	//this frame, so there is nothing to be ambiguous about - keep the live one.
	[Fact]
	public void Several_recorded_palettes_the_frame_does_not_hold_keep_the_live_palette_431()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(Tetris2Live, new uint[] { Tetris2Fade, 0x0F000F0F }, Tetris2Frame);
		Assert.Equal(NesPackPaletteStatus.RecordedNotDrawn, verdict.Status);
		Assert.Equal(Tetris2Live, verdict.Palette);
		Assert.False(verdict.IsRefusal);
	}

	//Only the recorded palettes the frame holds count towards Ambiguous: one held,
	//one not, is a substitution of the held one, not a refusal.
	[Fact]
	public void Only_the_recorded_palettes_the_frame_holds_are_candidates_431()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(Tetris2Live, new uint[] { Tetris2Fade, 0x0F262320 }, Tetris2Frame);
		Assert.Equal(NesPackPaletteStatus.Substituted, verdict.Status);
		Assert.Equal(0x0F262320u, verdict.Palette);
	}

	//The frame's palettes are packed the way HdTileKey packs a key and the way
	//the copy packs the live palette: the universal background colour leads every
	//background palette, and a sprite palette leads with FF (transparent).
	[Fact]
	public void The_frame_palettes_are_read_out_of_palette_ram_per_layer_431()
	{
		uint[] ram = new uint[32];
		for(int i = 0; i < 32; i++) {
			ram[i] = (uint)(0x10 + i);
		}
		ram[0] = 0x0F;
		Assert.Equal(new uint[] { 0x0F111213, 0x0F151617, 0x0F191A1B, 0x0F1D1E1F },
			NesPackTilePalette.FramePalettes(ram, false));
		Assert.Equal(new uint[] { 0xFF212223, 0xFF252627, 0xFF292A2B, 0xFF2D2E2F },
			NesPackTilePalette.FramePalettes(ram, true));
		Assert.Equal(0x0F191A1Bu, NesPackTilePalette.PaletteWord(ram, 2, false));
		Assert.Equal(0xFF292A2Bu, NesPackTilePalette.PaletteWord(ram, 2, true));
	}
}
