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

	[Fact]
	public void No_pack_loaded_leaves_the_live_palette_alone()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, null);
		Assert.Equal(NesPackPaletteStatus.Unchecked, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
		Assert.False(verdict.IsRefusal);
	}

	[Fact]
	public void The_live_palette_is_kept_when_the_pack_keys_the_tile_under_it()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { 0x0F0F0F0F, MetroidLive });
		Assert.Equal(NesPackPaletteStatus.LiveMatches, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
	}

	//Metroid: one recorded palette, and it is not the live one. The old behaviour
	//emitted 0F361506 - a key the renderer will never ask for.
	[Fact]
	public void Metroid_names_the_palette_the_pack_keys_the_tile_under()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { MetroidRecorded });
		Assert.Equal(NesPackPaletteStatus.Substituted, verdict.Status);
		Assert.Equal(MetroidRecorded, verdict.Palette);
		Assert.NotEqual(MetroidLive, verdict.Palette);
		Assert.Contains("0F0F0F0F", verdict.Reason);
		Assert.False(verdict.IsRefusal);
	}

	//"If the pack does not hold the tile at all, saying so is a valid answer and
	//better than a key that cannot match" - the user's own wording on #342.
	[Fact]
	public void A_tile_the_pack_does_not_hold_is_refused()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, Array.Empty<uint>());
		Assert.Equal(NesPackPaletteStatus.NoRule, verdict.Status);
		Assert.True(verdict.IsRefusal);
		Assert.Contains("holds no rule", verdict.Reason);
	}

	[Fact]
	public void Several_recorded_palettes_and_no_match_is_refused_with_the_list()
	{
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { 0x0F0F0F0F, 0x0F301612 });
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
		NesPackPaletteVerdict verdict = NesPackTilePalette.Resolve(MetroidLive, new uint[] { MetroidRecorded, NesPackTilePalette.DefaultTileWildcard });
		Assert.Equal(NesPackPaletteStatus.LiveMatches, verdict.Status);
		Assert.Equal(MetroidLive, verdict.Palette);
	}
}
