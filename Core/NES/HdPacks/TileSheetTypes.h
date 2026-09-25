#pragma once
//ADR-0153 (Phase 9): shared vocabulary for the artist-legible sheet pipeline.
//Host-free on purpose - no pch.h, no Emulator, no console type - so
//MetatileVocabulary / ScreenStitcher / SheetGrouping / SheetRender all link
//into `make core-unit-tests` (same rule as Core/Shared/Video/BorderLayout.h,
//ADR-0127). HdPackBuilder is the only stateful partner: it records the grid
//stream and writes the bytes these modules return.
#include <cstdint>
#include <cstring>
#include <array>
#include <map>
#include <ostream>
#include <string>
#include <vector>

namespace MesenSheets
{
	//---- tuning constants (ADR-0153 §1, §2, §5) -----------------------------

	//A metatile pair joins an object only when both directions predict each
	//other: n >= MinPairCount and n/out(A) >= MinPairProb and n/in(B) >= MinPairProb.
	constexpr uint32_t kSheetMinPairCount = 3;
	constexpr double kSheetMinPairProb = 0.80;
	//Beyond this a "component" is a contiguous background region, not a figure.
	constexpr uint32_t kSheetMaxObjectCells = 32;
	//Diagnostic only since the §1 amendment: self-consistency saturates near 1
	//on every game, so it no longer gates the grid unit.
	constexpr double kGridConsistencyThreshold = 0.60;
	//A real grid has one parity with a markedly smaller vocabulary. Measured
	//0.20-0.34 (Zelda, attribute-aligned) vs 0.03-0.11 (Excitebike, no grid);
	//0.15 clears both ends whichever screen set feeds the vocabulary.
	constexpr double kGridPhaseAdvantage = 0.15;
	//A status-bar row keeps most of its columns across screens (labels) while a
	//few change (score, timer, lives), and carries real content - a stretch of
	//sky matches everywhere too, but draws nothing.
	constexpr double kHudRowFrozenRatio = 0.50;
	constexpr double kHudRowDrawnRatio = 0.25;
	//A recording is not one screen family: title cards, menus and game-over
	//screens share no row with the playfield. A column counts as frozen when its
	//most common shape covers this share of the screens, so a minority of
	//unrelated screens cannot hide a status bar.
	constexpr double kHudRowScreenAgreement = 0.60;
	//Below this the playfield is essentially text/menus: fall back to 8x8.
	constexpr uint32_t kMinMetatilePlacements = 64;
	//A 2x2 tuple counts towards consistency once it has been seen this often.
	constexpr uint32_t kGridConsistencyMinCount = 3;
	//F9.5: two OAM entries further apart than this are never joined. 32 px is a
	//4x4 block of 8x8 cells - half the kSheetMaxObjectCells budget - and past it
	//a constant offset says "both were on screen", not "these move together".
	constexpr int32_t kSpriteMaxOffset = 32;
	//---- F9.17 (ADR-0164): sheets/adjacency.json -------------------------
	//
	//The sidecar keeps the top N within-cap offsets of a sprite pair and the
	//top N bottom-edge bands of a sprite shape, and drops a pair whose
	//co-sightings never reach this floor - one co-sighting is noise and would
	//make the file scale with frames instead of with the vocabulary.
	constexpr uint32_t kAdjacencyMaxOffsets = 8;
	constexpr uint32_t kAdjacencyMaxFloors = 8;
	constexpr uint32_t kAdjacencyMinPairCount = 2;
	//ADR-0173: a sprite shape is screen-fixed when it kept returning to pixels
	//it had already been drawn at - on average, every position it ever occupied
	//was occupied again in kScreenFixedRevisits distinct frames. A shape seen in
	//fewer than kScreenFixedMinFrames frames is never classified: a handful of
	//sightings in one place is no evidence of being pinned there.
	constexpr uint32_t kScreenFixedMinFrames = 64;
	constexpr uint32_t kScreenFixedRevisits = 32;
	//---- F9.19 (ADR-0170): sheets/poses.json -----------------------------
	//
	//Two OAM entries belong to the same silhouette when their 8x8 boxes are
	//within this on both axes - 8 px is "touching", the smallest gap that
	//still reads as one figure, and the value S10.a measured its poses with.
	constexpr int32_t kPoseMaxGap = 8;
	//A silhouette seen in fewer retained frames than this, or holding fewer
	//tiles, is transient garbage (one frame of an explosion mid-redraw, a lone
	//projectile) and must not reach a file an artist reads.
	constexpr uint32_t kPoseMinFrames = 3;
	constexpr uint32_t kPoseMinTiles = 4;
	//Kept poses, by frames descending. Mirrors kMaxSheetFrames so a long
	//session cannot grow the file without bound.
	constexpr uint32_t kMaxPoses = 4096;
	//---- ADR-0179 (F9.20): succession, cycles, sequences, variants --------
	//
	//A kept cluster in frame i continues as the kept cluster in frame i+1
	//whose top-left is nearest, within this Manhattan distance in pixels. A
	//running actor moves a few px per frame; a teleport ends the track.
	constexpr int32_t kPoseTrackMaxMove = 16;
	//ADR-0226: a kept cluster with no partner in frame i+1 stays a pending
	//track end for this many retained frames, so a figure drawn on every
	//other frame (flicker, OAM sharing) is one track, not one per sighting.
	//The skipped frame must carry RepeatCount <= kPoseTrackGapMaxRepeats: a
	//figure gone across a long static frame has really left.
	constexpr uint32_t kPoseTrackMaxGap = 1;
	constexpr uint32_t kPoseTrackGapMaxRepeats = 2;
	//A cycle is a period that repeats at least this many consecutive times
	//on one track; the period is searched up to kPoseCycleMaxPeriod poses.
	constexpr uint32_t kPoseCycleMinRepeats = 2;
	constexpr uint32_t kPoseCycleMaxPeriod = 16;
	//A sequence (non-looping animation) is a run of at least this many
	//distinct poses, outside every cycle, seen identically at least
	//kPoseCycleMinRepeats times; windows longer than the max are not tried.
	constexpr uint32_t kPoseSequenceMinLength = 3;
	constexpr uint32_t kPoseSequenceMaxLength = 32;
	//---- ADR-0181 §3 (F9.23): `driver` by interruption ----------------------
	//
	//A window is one occurrence of a cycle on a track (>= kPoseCycleMinRepeats
	//turns); it stops at its last phase advance plus that phase's median hold
	//- not at the end of the last run, which swallows the idle a figure spends
	//parked on a cycle pose (Link stands on a walk frame). A port drives the
	//cycle when at least kDriverMinWindows windows exist and at least
	//kDriverStopShareNum/Den of them stop within kDriverStopLag frames after
	//a release of some button on that port, and the other port does not pass
	//the same test. Set from the 2026-09-13 probe measurement (Zelda's walks
	//10/10, Mega Man 3's run 12/13, Contra's run 3/3 stop within 12 f; the
	//enemy cycles 0-2/13, Excitebike's wheels 3/18).
	constexpr uint32_t kDriverMinWindows = 4;
	constexpr uint32_t kDriverStopLag = 12;
	constexpr uint32_t kDriverStopShareNum = 2;
	constexpr uint32_t kDriverStopShareDen = 3;
	//---- ADR-0174 (issue #174): the sheet -> pose cross-reference ---------
	//
	//A group sheet names the poses its cells belong to, most-covered first. A
	//node shared by many silhouettes (Contra's legs sit under two torsos) can
	//be cited by dozens of them, and a pack with kMaxPoses poses would then
	//grow the sidecar with the *stream* instead of with the sheet. The list is
	//a front door to poses.json, not a second copy of it, so the tail past
	//this - the weakest joins, by the §2 order - is dropped.
	constexpr uint32_t kSheetMaxPoseRefs = 32;
	//Retained per-frame grids (de-duplicated); ~2.8 KB each since the ADR-0159
	//amendment added the palette plane (1920 B of shape ids + 960 B of palette
	//ids), i.e. ~11.5 MB with the stream full.
	constexpr uint32_t kMaxSheetFrames = 4096;
	//F12.6b (ADR-0197 §3, option (b)): the internal RAM window the recorder
	//keeps beside every retained grid frame, so a `memoryCheckConstant` an
	//artist writes after the fact can still be checked against the run. It is
	//the same range ADR-0184 bounds for RAM cheats (AAAA < 0x0800). Fixed, and
	//independent of any loaded pack's WatchedMemoryAddresses - option (a),
	//retaining only what a pack already watches, was rejected because a new
	//address would need the pack loaded and the run re-recorded first.
	//2 KB x kMaxSheetFrames = 8 MB with the stream full.
	constexpr uint32_t kRetainedRamSize = 0x800;
	//1-cell gutter, transparent, between every sheet cell.
	constexpr uint32_t kSheetGutter = 1;
	//Largest stitched-map canvas rendered at 1x, in pixels (ADR-0153 §6: a map
	//is a paint surface written once at save time). A screen-mode map is
	//(screens wide x 256) x (screens tall x 240) and a continuous strip is
	//(world columns x 8) x 240; 64 Mpx is ~1000 screens - far past any
	//recording - and bounds the RGBA buffer at 256 MB before the pack-scale
	//upscale multiplies it again. A map past this is skipped and logged, not
	//truncated.
	constexpr uint64_t kMaxMapPixels = 64ull * 1024 * 1024;
	//A map region narrower than this does not justify the continuous stitcher.
	constexpr uint32_t kContinuousMinWidth = 512;
	//One subject reaches the vocabulary under several keys: a mapper that swaps
	//CHR banks per animation frame (MMC2 in Punch-Out!!, MMC3 elsewhere)
	//delivers the same drawing under a different tile index, and CHR-RAM
	//re-uploads earn a fresh shape id for identical bytes. Measured by
	//scripts/spike_sheet_dedup.py over the 30-ROM library: 6906 vocabulary
	//cells collapse to 3733 subjects, and much of that is *exact* pixel
	//duplication (Ninja Gaiden 226 -> 67, Gauntlet 361 -> 105).
	//
	//The tolerance is the share of channel bytes allowed to differ before two
	//cells count as one subject, measured against the *ink* of the richer of
	//the two cells (pixels that are not its most common colour), never against
	//the cell area. Against the area it is not a tolerance at all: every
	//mostly-background metatile is within 10% of every other one, and the
	//first near-empty cell becomes an attractor - on a real Ninja Gaiden
	//recording 465 entries fell to 37 cells with a single cell holding 335 of
	//them. 0.10 was the value the spike measured with; it collapses bank
	//duplicates and near-identical animation frames without merging cells an
	//artist would paint differently. It is a measured starting point, not a
	//tuned optimum - the spike reports the curve.
	constexpr double kSheetAliasTolerance = 0.10;

	//---- F9.8: adjacency evidence before two screens share a map -----------
	//
	//A screen is appended to a map only when something measurable says it is a
	//neighbour. The evidence is a shared border band read through the scroll:
	//when a transition frame matches the already-placed screen at shift
	//(dx, dy), the cells that shift exposes at the leading edge lie *outside*
	//the placed screen, so they can only be the screen that was scrolling in -
	//and they must be the candidate's opposite edge. Without that, the screen
	//starts its own map instead of being concatenated at an arbitrary offset.
	//
	//Not measured on a fresh recording: re-recording needs the capture tool,
	//which this slice may not relink. The values are read off the only
	//stitched sequences on record, the 2026-09-04 spike (runs/spike-sheets):
	//Zelda's four accepted links score a whole-frame match of 0.73-0.77 at
	//shifts of 5-8 cells, i.e. the 16-25 % of the frame that did *not* match
	//the anchor is exactly the exposed band. On a real neighbour that band is
	//therefore almost entirely the candidate's content; 0.60 leaves room for
	//sprite overlap and a mid-transition sliver while still rejecting a band
	//that merely happens to carry the same tiles.
	constexpr double kStitchBandMatch = 0.60;
	//...and the band has to say something the anchor did not already say. A
	//screen that is mostly one backdrop tile - a Punch-Out!! profile card, a
	//text screen - agrees with every other screen's edge, so a bare band ratio
	//would rubber-stamp exactly the collage this slice exists to stop. The band
	//must therefore beat, by this margin, the share of the same cells the
	//anchor already carried at the same positions. Read off the spike again:
	//Zelda's dx=8 link scores 0.75 over the whole frame, i.e. 24 of 32 columns
	//are anchor-explained and the remaining 8 - the band - contributed nothing,
	//so a real neighbour's lead there is ~1.0. 0.25 is a wide safety margin
	//under that, and a backdrop-only band leads by 0.
	constexpr double kStitchBandLead = 0.25;
	//A band of fewer drawn cells than this is not a measurement - a strip of
	//sky agrees with anything. 24 cells is six unit-16 metatiles; Zelda's
	//narrowest accepted band (5 rows x 32 cols) is 160.
	constexpr uint32_t kStitchMinBandCells = 24;
	//A one-cell band cannot even carry one metatile of the candidate at grid
	//unit 16, so a shift that small is not evidence of a neighbour.
	constexpr int32_t kStitchMinShiftCells = 2;
	//Transition frames probed between two stable screens. The de-duplicated
	//stream (ADR-0153 §5) can put the whole transition in one entry or spread
	//it over many, so one fixed fraction of the gap is a lucky guess; three
	//evenly spread probes cover both shapes, and every probe still has to
	//clear the full evidence bar on its own.
	constexpr uint32_t kStitchTransitionProbes = 3;
	//---- F9.12: a continuous region ends when the world is replaced ---------
	//
	//Continuous mode had no notion of "this is a different place". Its only cut
	//was a whole-frame match below kMinMatch (0.5), and 0.5 is unreachable for a
	//screen swap that keeps the terrain: Super Mario Bros.' title screen is
	//drawn on top of the very start of world 1-1 - same hill, same bushes, same
	//ground - with a logo panel and a menu stamped into the sky. So the
	//title-to-level step reports dx == 0 ("the camera did not move") at a high
	//score, no cut fires, and PaintFrame's first-writer-wins bakes the logo,
	//"ONE PLUMBER / TWO PLUMBERS" and "TOP- 000000" into the level map's sky.
	//There is no seam in map-000.png because there is no offset: the two
	//screens are superimposed on the same world columns.
	//
	//The rule: a step that does *not* claim the camera moved is a claim that
	//both frames show the same place, so the whole playfield has to agree.
	//Below this share it did not - the content was replaced, not continued -
	//and the region ends. A step that does claim a shift is judged exactly as
	//before (kMinMatch), so a genuine scroller is not touched by this rule at
	//all: that is the Excitebike guard, by construction rather than by tuning.
	//
	//Measured, not swept, and not on a fresh recording - this slice may not
	//relink the capture tool. The numbers come off the 21 stable screens the
	//Super Mario Bros. recording installed under
	//`auto/textures/backgrounds/screen*.orig.png`, compared cell by cell (8x8,
	//pixel-exact) at the shift the stitcher accepts:
	//  - title screen vs the level's first screen: 0.700 agreement at dx == 0,
	//    i.e. 30 % of the playfield is title art the level does not have. It
	//    scores 0.699-0.700 against every one of the 19 level screens.
	//  - level screen vs the next level screen (camera still): 0.996-0.999.
	//  - Excitebike's screen001 vs screen002 (two unrelated non-scrolling
	//    screens): 0.746.
	//0.85 is the midpoint of that gap (0.848), so it sits ~0.15 above the
	//overlay case and ~0.15 below the tightest genuine same-place step. The
	//pixel-exact comparison is a proxy for the recorder's palette-agnostic
	//shape ids, and it can only *under*-count agreement (two cells that differ
	//only in palette read as different here and as equal in the stitcher), so
	//the genuine-step figures are a lower bound.
	constexpr double kStitchWorldAgree = 0.85;
	//Continuous mode: accept a non-zero x shift only when it beats "the camera
	//did not move" by this much. On near-uniform content every offset scores
	//alike and the argmax is arbitrary - that is how a still game grows a
	//kilopixel-wide strip of nothing. The margin is deliberately small: a real
	//scroll beats a still by tenths, not by hundredths, so 0.02 costs a genuine
	//scroller nothing. Unmeasured, for the same reason as kStitchBandMatch.
	constexpr double kStitchStillMargin = 0.02;

	//---- issue #164: anchors a variant of the screen does not break ---------
	//
	//ADR-0050 gates every backgrounds/screenNNN.png on three tileAtPosition
	//conditions, picked as the rarest non-flat tiles on the frame. Rarity is
	//what makes a false match on some *other* screen unlikely - and it is also
	//the property of a score digit, a timer, a blinking prompt. When one of the
	//three changes, the whole <background> stops drawing, and since ADR-0156
	//the cells routed off metatiles.png onto that screen then render vanilla.
	//
	//Measured on the 30-pack library by scripts/spike_anchor_stability.py
	//(pixel-exact, against the captured screens on disk): 1558 of 3487 shipped
	//anchors (44.7 %) sit on a cell some variant of their own screen changes,
	//and 21424 of 29218 variant pairs (73.3 %) therefore fail to draw.
	//
	//The fix is *not* "prefer stable cells", though: the same measurement says
	//a stability-first pick alone drives false matches on unrelated screens
	//from 3563/80902 (4.4 %) to 16237/80902 (20.1 %), because what stays put
	//across variants is the shared frame every other screen also has - and a
	//false match draws the wrong screen whole, which is worse than a gap.
	//Stability is therefore a *filter* and discrimination stays the objective:
	//among the cells no variant touches, take the ones that tell this screen
	//apart from the rest of the recording, and fall back to the volatile ones
	//only when the stable region cannot. That pick measures 3911/29218 (13.4 %)
	//misses and 709/80902 (0.88 %) false matches - both better than shipped.

	//Two frames are variants of one screen when they agree on this share of the
	//960 cells. 0.90 is the midpoint of the range the spike was run over; the
	//result is not sensitive to it (0.85 -> 30.4 % misses after vs 77.1 %
	//before, 0.95 -> 9.5 % vs 72.1 %, both with false matches down 5x).
	constexpr double kAnchorVariantAgree = 0.90;
	//How deep into the rarity ranking the discrimination search looks. The
	//search is O(cap x picks x frames) per screen, once, at save time.
	constexpr uint32_t kAnchorCandidateCap = 40;
	//ADR-0050: three conditions, at least 64 px apart (Manhattan).
	constexpr uint32_t kAnchorCount = 3;
	constexpr uint32_t kAnchorMinSpread = 64;

	constexpr uint32_t kGridCols = 32;
	constexpr uint32_t kGridRows = 30;

	//---- recorded data -----------------------------------------------------

	//An 8x8 background tile exactly as hires.txt keys it: the 16 CHR bytes plus
	//the 4-colour NES palette word ([31:24] = colour 0 ... [7:0] = colour 3).
	//ADR-0178: the OAM flip transform, shared by the recorder that bakes it into
	//a sprite's recorded shape and by the code that un-bakes it to recover the
	//tile data hires.txt keys by. Per axis it is an involution - applying the
	//same flags twice restores the original bytes - so one function serves both
	//directions and neither can drift from the other.
	inline void ApplyTileFlips(uint8_t* tileData, bool horizontalMirror, bool verticalMirror)
	{
		if(verticalMirror) {
			for(int plane = 0; plane < 16; plane += 8) {
				for(int row = 0; row < 4; row++) {
					uint8_t tmp = tileData[plane + row];
					tileData[plane + row] = tileData[plane + 7 - row];
					tileData[plane + 7 - row] = tmp;
				}
			}
		}
		if(horizontalMirror) {
			for(int i = 0; i < 16; i++) {
				uint8_t b = tileData[i];
				b = (uint8_t)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
				b = (uint8_t)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
				b = (uint8_t)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
				tileData[i] = b;
			}
		}
	}

	struct SheetTileKey
	{
		uint8_t TileData[16] = {};
		uint32_t PaletteColors = 0;
		//ADR-0172: the absolute CHR index hires.txt keys this tile by on a CHR
		//ROM game (`AbsoluteTileAddr / 16`), -1 on a CHR RAM game or when the
		//recorder never saw one. Deliberately outside the comparisons below -
		//identity stays TileData + PaletteColors, so the vocabulary, the dedup
		//and every grouping decision are unchanged by carrying it.
		int32_t TileIndex = -1;

		//ADR-0178: the tile data as the PPU fetched it, before the OAM flip bits
		//were baked into TileData above, and which of the two axes were baked.
		//On a CHR RAM game hires.txt keys by the data, and the run time looks up
		//the unflipped form - it mirrors the replacement art itself - so this is
		//the key a rebuilt pack must emit. Mirrors bit 0 = horizontal, bit 1 =
		//vertical; 0 means the shape was never flipped and SourceTileData equals
		//TileData. Deliberately outside the comparisons below, like TileIndex.
		uint8_t SourceTileData[16] = {};
		uint8_t Mirrors = 0;

		bool operator==(const SheetTileKey& o) const
		{
			return PaletteColors == o.PaletteColors && memcmp(TileData, o.TileData, 16) == 0;
		}
		bool operator<(const SheetTileKey& o) const
		{
			int c = memcmp(TileData, o.TileData, 16);
			return c != 0 ? c < 0 : PaletteColors < o.PaletteColors;
		}
	};

	//Palette-agnostic shape id handed out by the recorder in first-sight order.
	using ShapeId = uint16_t;
	constexpr ShapeId kEmptyCell = 0xFFFF;

	//ADR-0221 (option B, F12.13): what "empty" means for the variant kind test.
	//The recorder hands *every* drawn tile a shape id (ShapeIdFor), so a cell
	//the game fills with a single flat colour is not kEmptyCell in the grid -
	//it is a shape whose 16 CHR bytes resolve every pixel to one colour index
	//(each plane's eight row bytes all 0x00 or all 0xFF). That is the same
	//"single flat colour per 8x8 cell" scripts/measure_capture_overdraw.py
	//scores, chosen so the rule and its acceptance tool agree on the word. A
	//kEmptyCell (nothing drawn there) is empty too. Palette-agnostic on
	//purpose: two colour indexes that happen to map to one NES colour under
	//some palette would read as "detail" here and "flat" in the tool, which
	//errs toward the rival side - the safe one for #339.
	inline bool IsFlatTileData(const uint8_t* tileData)
	{
		uint8_t plane0 = tileData[0];
		uint8_t plane1 = tileData[8];
		if((plane0 != 0x00 && plane0 != 0xFF) || (plane1 != 0x00 && plane1 != 0xFF)) {
			return false;
		}
		for(int row = 1; row < 8; row++) {
			if(tileData[row] != plane0 || tileData[8 + row] != plane1) {
				return false;
			}
		}
		return true;
	}

	//Which 4-colour NES palette a cell was drawn with, interned by the recorder
	//in first-sight order (ADR-0159 amendment, 2026-09-05). A whole
	//PaletteColors word per cell would triple the retained stream; an id only
	//has to answer "the same colours as last time?", which is the only question
	//the anchor rule asks of it.
	using PaletteId = uint8_t;
	//"No palette evidence": no cell was drawn here, the caller carries none at
	//all, or the recording used more palettes than the id space holds. The
	//anchor rule reads it as "the colours may well be the same" throughout, so
	//a stream without palettes behaves exactly as it did before the amendment
	//instead of anchoring on nothing.
	constexpr PaletteId kUnknownPalette = 0xFF;

	//One recorded background frame: which shape sat at every 8x8 cell origin.
	//FineX is the sub-tile x scroll the cells were aligned to (0..7), so two
	//frames of the same screen at different scroll offsets compare equal.
	struct GridFrame
	{
		ShapeId Cells[kGridRows][kGridCols];
		//Palette id per cell, same indexing as Cells (ADR-0159 amendment). The
		//shape ids above are palette-agnostic on purpose - the vocabulary must
		//see a bank-swapped or recoloured tile as one subject - so a recolour is
		//invisible in Cells, while the tileAtPosition condition an anchor becomes
		//compares PaletteColors and fails on it. This plane is the evidence for
		//that one case: +960 B on a 1920 B frame, ~2.8 KB retained per frame.
		PaletteId Palettes[kGridRows][kGridCols];
		uint8_t FineX = 0;
		uint32_t FrameNumber = 0;
		//How many consecutive recorded frames were identical to this one. The
		//recorder collapses duplicates, so "the screen held still for N frames"
		//reads as RepeatCount >= N instead of a run length in the stream.
		uint32_t RepeatCount = 1;
		//F9.9 (ADR-0156): this frame was written out as
		//backgrounds/screenNNN.png, so a `<background>` layer covers it at
		//render time. Set by HdPackBuilder when CaptureScreen succeeds; the
		//inference reads it to decide which cells the screen surface owns.
		bool Captured = false;

		GridFrame() { Clear(); }
		void Clear()
		{
			for(uint32_t r = 0; r < kGridRows; r++) {
				for(uint32_t c = 0; c < kGridCols; c++) {
					Cells[r][c] = kEmptyCell;
					Palettes[r][c] = kUnknownPalette;
				}
			}
		}
		uint32_t DrawnCells() const
		{
			uint32_t n = 0;
			for(uint32_t r = 0; r < kGridRows; r++) {
				for(uint32_t c = 0; c < kGridCols; c++) {
					n += Cells[r][c] != kEmptyCell ? 1 : 0;
				}
			}
			return n;
		}
		bool SameCells(const GridFrame& o) const { return memcmp(Cells, o.Cells, sizeof(Cells)) == 0; }
		//Same drawing *and* same colours. The recorder de-duplicates on this, so
		//a frame that only recolours the screen earns its own entry instead of
		//collapsing into RepeatCount - otherwise the very evidence the anchor
		//rule needs is the evidence the stream throws away. The vocabulary keeps
		//using SameCells: for it a recoloured tile is the same subject.
		bool SamePalettedCells(const GridFrame& o) const
		{
			return SameCells(o) && memcmp(Palettes, o.Palettes, sizeof(Palettes)) == 0;
		}
	};

	//HdPackBuilder::RecordGridFrame's layout of one frame's background runs
	//onto `frame` (a run is HdPackBuilder::ScreenRun: X, Y and the HdPpuTileInfo
	//Tile drawn from X to the next run's X on scanline Y). Ported from the
	//spike's frame_grid (scripts/spike_tile_sheets.py): run starts sit on tile
	//boundaries, so the most common (x % 8) among non-zero run starts is the
	//frame's fine x scroll, and cells are laid out relative to it - two frames
	//of the same screen at different sub-tile offsets then compare equal. A
	//cell is filled from the scanline at its origin (y % 8 == 0).
	//`shapeFor(tile)` interns a tile's shape (kEmptyCell when it cannot) and
	//`paletteFor(paletteColors)` its palette, in the order they are met. Host-free and inline
	//because HdPackBuilder.cpp is not in the unit-test link set.
	template<typename Run, typename ShapeFor, typename PaletteFor>
	void LayOutGridRuns(const std::vector<Run>& runs, GridFrame& frame, ShapeFor&& shapeFor, PaletteFor&& paletteFor)
	{
		uint32_t fineCounts[8] = {};
		for(const Run& run : runs) {
			if(run.X != 0) {
				fineCounts[run.X & 7]++;
			}
		}
		uint8_t fine = 0;
		for(uint8_t i = 1; i < 8; i++) {
			if(fineCounts[i] > fineCounts[fine]) {
				fine = i;
			}
		}
		frame.FineX = fine;
		for(size_t i = 0; i < runs.size(); i++) {
			const Run& run = runs[i];
			if((run.Y & 7) != 0) {
				continue;
			}
			uint32_t row = (uint32_t)run.Y >> 3;
			if(row >= kGridRows) {
				continue;
			}
			//The run ends where the next run on the same scanline starts
			uint32_t xEnd = (i + 1 < runs.size() && runs[i + 1].Y == run.Y) ? runs[i + 1].X : 256;
			int32_t offset = ((int32_t)run.X - (int32_t)fine) % 8;
			if(offset < 0) {
				offset += 8;
			}
			uint32_t cx = offset == 0 ? run.X : run.X + (8 - offset);
			ShapeId shape = shapeFor(run.Tile);
			if(shape == kEmptyCell) {
				continue;
			}
			PaletteId palette = paletteFor(run.Tile.PaletteColors);
			for(; cx + 8 <= 256 && cx < xEnd; cx += 8) {
				int32_t col = ((int32_t)cx - (int32_t)fine) / 8;
				if(col >= 0 && col < (int32_t)kGridCols) {
					frame.Cells[row][col] = shape;
					frame.Palettes[row][col] = palette;
				}
			}
		}
		//#471: every other scanline's tiles are interned too, after the origin
		//scanlines so their shapes keep the ids they had. DrawPixel writes a
		//<tile> rule for every scanline; a tile drawn only off a cell's origin
		//(Punch-Out!!'s 00FD, a one-line raster effect on y % 8 == 7) had rules
		//and no sheet cell. It takes no grid cell here (the cell belongs to
		//its origin's tile); the unsorted sheet gives its shape one.
		for(const Run& run : runs) {
			if((run.Y & 7) != 0) {
				shapeFor(run.Tile);
			}
		}
	}

	//F12.6b (ADR-0197 §3): the body of a grid dump's `M` line - the retained
	//RAM window as upper-case hex with no separators, `kRetainedRamSize * 2`
	//characters, always the full width so a reader can index a byte by
	//multiplying its address by two. A short or absent buffer pads with zeroes
	//rather than shortening the line, because a ragged line would read as a
	//different address space.
	//
	//It lives here, host-free and inline, on purpose: `HdPackBuilder.cpp` is
	//not in the unit-test link set, so the encoding the emulator writes would
	//otherwise be untestable and could drift from the one `mep_conditions.py`
	//reads.
	inline std::string RamDumpLine(const uint8_t* ram, size_t size)
	{
		static const char* digits = "0123456789ABCDEF";
		std::string out((size_t)kRetainedRamSize * 2, '0');
		size_t n = ram == nullptr ? 0 : (size < kRetainedRamSize ? size : kRetainedRamSize);
		for(size_t i = 0; i < n; i++) {
			out[i * 2] = digits[ram[i] >> 4];
			out[i * 2 + 1] = digits[ram[i] & 0x0F];
		}
		return out;
	}

	//---- OAM (F9.5) --------------------------------------------------------

	//One sprite as the recorder saw it: the 8x8 shape id - taken *after* the OAM
	//flip bits are applied, so the mirrored half of a figure is its own shape and
	//can sit beside its twin on a sheet - plus its screen origin in pixels. An
	//8x16 sprite is recorded as its two 8x8 halves, which keeps the whole slice
	//on one unit and lets the grouping recover the tall figure by itself.
	//
	//ADR-0222 (F12.14): Palette is the interned id of the sprite's palette word
	//(the OAM attribute's two palette bits resolved through palette RAM), from
	//the same first-sight table the grid stream's "P" lines spell
	//(HdPackBuilder::PaletteIdFor). The shape id wildcards the palette on
	//purpose, so without it the OAM stream could settle the shape half of a
	//spriteNearby condition and never its colour half. It is part of entry
	//identity: two frames that differ only in sprite colour are two frames.
	//One sprite half the PPU placed: the shape it was drawn from, its screen
	//origin in pixels, and the palette its row was drawn in.
	//
	//#520: Shape == kEmptyCell is an *artless placement* - a half whose 16 bytes
	//are all zero, which #470 (IsFullyTransparent) keeps out of the shape
	//registry because the loader never draws one and no `<tile>` rule can exist
	//for it. The cell is still part of the figure the game drew, which is what
	//ADR-0170 §1 segments, so the OAM stream carries it with no shape rather
	//than dropping it. Every shape-keyed consumer already skips kEmptyCell:
	//BuildSpriteVocabulary, Accumulate, SpriteNearbyPalettes and the stream dump.
	struct OamEntry
	{
		ShapeId Shape = kEmptyCell;
		uint8_t X = 0;
		uint8_t Y = 0;
		PaletteId Palette = kUnknownPalette;

		bool operator==(const OamEntry& o) const { return Shape == o.Shape && X == o.X && Y == o.Y && Palette == o.Palette; }
	};

	//#520: the OAM entry an *artless placement* is - a sprite half the PPU
	//placed whose 16 bytes are all zero (#470's IsFullyTransparent). It names no
	//shape, so it reaches no sheet cell, no `<tile>` rule and no vocabulary node;
	//it is a cell of the figure the game drew, which is what ADR-0170 §1
	//segments. `HdPackBuilder::RecordSpritePlacement` is the only production
	//caller and this is the only thing it builds, so a reader can pin the entry
	//host-free, without HdPackBuilder.cpp in the unit-test link set (ADR-0127).
	inline OamEntry PlacementEntry(uint8_t x, uint8_t y)
	{
		OamEntry entry;
		entry.Shape = kEmptyCell;
		entry.X = x;
		entry.Y = y;
		//No art was drawn, so no palette was observed on the half. The pose pass
		//reads positions; the palette-keyed passes skip kUnknownPalette.
		entry.Palette = kUnknownPalette;
		return entry;
	}

	//One frame's OAM, in OAM order. Consecutive identical frames collapse into
	//RepeatCount and the stream is capped at kMaxSheetFrames, exactly like
	//GridFrame - a paused screen must not manufacture evidence.
	struct OamFrame
	{
		std::vector<OamEntry> Entries;
		uint32_t FrameNumber = 0;
		uint32_t RepeatCount = 1;
		//ADR-0181 §1: the packed button byte of ports 1 and 2 at frame end, in
		//NesController::ToByte order (A, B, Select, Start, Up, Down, Left,
		//Right; bit 0 = A). Not part of frame identity: a repeated frame
		//keeps the buttons of its first occurrence, so a held button that
		//changes nothing on screen is under-counted (a lower bound on
		//attention, not a duty cycle). 0 for a port with no controller.
		uint8_t Buttons[2] = {};

		//Entry identity includes the palette id (ADR-0222), so a frame that only
		//recolours a sprite is retained as its own frame, as the grid stream
		//already does for a recoloured cell (ADR-0159 amendment, 2026-09-05).
		bool SameEntries(const OamFrame& o) const { return Entries == o.Entries; }
	};

	//ADR-0222 option A (F12.14): the MESEN_OAM_STREAM_DUMP writer, host-free so
	//scripts/core_unit_tests.cpp can pin the format `mep_conditions.py` reads.
	//Self-describing like WriteGridDump: "K <id> <32 hex tile data> <8 hex
	//palette>" interns a shape on first sight, "P <id> <8 hex palette>" interns
	//a palette word on first sight, and each frame is one line,
	//"<frame> <repeat> <port1> <port2>" (ADR-0181: the two button bytes) followed
	//by "<shape>,<x>,<y>,<pal>" per sprite, where <shape> is the ShapeId - the
	//same id space the grid stream's "K" lines use, since ShapeIdFor interns
	//sprites and background cells into one table. The "K" tile data is the
	//shape's drawable art (TileData, with the OAM flips baked in - ADR-0178),
	//exactly what the grid dump writes for the same id. A palette id with no
	//entry in `paletteColors` (kUnknownPalette: the id space ran out) gets no
	//"P" line and a reader falls back to the shape's own first-seen palette.
	inline void WriteOamStreamDump(std::ostream& out, const std::vector<OamFrame>& frames, const std::vector<SheetTileKey>& shapeTiles, const std::vector<uint32_t>& paletteColors)
	{
		static const char* digits = "0123456789ABCDEF";
		auto hex8 = [&](uint8_t v) { out << digits[v >> 4] << digits[v & 0x0F]; };
		auto hex32 = [&](uint32_t v) { hex8((uint8_t)(v >> 24)); hex8((uint8_t)(v >> 16)); hex8((uint8_t)(v >> 8)); hex8((uint8_t)v); };
		std::vector<bool> shapeEmitted(shapeTiles.size(), false);
		std::vector<bool> paletteEmitted(paletteColors.size(), false);
		for(const OamFrame& frame : frames) {
			for(const OamEntry& entry : frame.Entries) {
				//#520: an artless placement (kEmptyCell, #470's fully
				//transparent half) names no art, so there is nothing for the
				//dump to resolve it to - and ADR-0222's contract is that every
				//entry the dump lists does resolve to tile data and palette.
				//The pose pass reads it; this dump does not carry it.
				if(entry.Shape == kEmptyCell) {
					continue;
				}
				if(entry.Shape < shapeTiles.size() && !shapeEmitted[entry.Shape]) {
					shapeEmitted[entry.Shape] = true;
					out << "K " << entry.Shape << ' ';
					for(int b = 0; b < 16; b++) {
						hex8(shapeTiles[entry.Shape].TileData[b]);
					}
					out << ' ';
					hex32(shapeTiles[entry.Shape].PaletteColors);
					out << '\n';
				}
				if(entry.Palette < paletteColors.size() && !paletteEmitted[entry.Palette]) {
					paletteEmitted[entry.Palette] = true;
					out << "P " << (uint32_t)entry.Palette << ' ';
					hex32(paletteColors[entry.Palette]);
					out << '\n';
				}
			}
			out << frame.FrameNumber << ' ' << frame.RepeatCount << ' ' << (int)frame.Buttons[0] << ' ' << (int)frame.Buttons[1];
			for(const OamEntry& entry : frame.Entries) {
				if(entry.Shape == kEmptyCell) {
					continue; //#520: an artless placement resolves to nothing
				}
				out << ' ' << entry.Shape << ',' << (int)entry.X << ',' << (int)entry.Y << ',' << (uint32_t)entry.Palette;
			}
			out << '\n';
		}
	}

	//---- F9.17 (ADR-0164): adjacency.json statistics ----------------------

	//One bottom-edge band a sprite shape stood on: the OAM entry's bottom edge
	//(Y + 8; an 8x16 sprite's lower half lands on the true bottom) quantised to
	//8 px. Counted over the retained, de-duplicated OamFrame stream, no distance
	//cap - the 32 px offset test drops a pair the moment the actors are further
	//apart, which is exactly the far-field question floors[] is for. The band a
	//composed usrNNN sprite layer records (ADR-0164 §5) is one such bottom.
	struct SpriteFloorBand
	{
		uint32_t Bottom = 0;
		uint32_t Count = 0;
	};

	//One within-cap relative offset a sprite pair held, in pixels: B sat at
	//(A + Dx, A + Dy). The lower vocabulary index is the reference, matching
	//SelectSpriteEdges, so P(B at d | A) recomputed from the file equals the
	//value the grouping test divided by.
	struct SpriteOffsetSample
	{
		int32_t Dx = 0;
		int32_t Dy = 0;
		uint32_t Count = 0;
	};

	//One unordered sprite pair's persisted statistics (A < B). Only offsets
	//within kSpriteMaxOffset on both axes are counted at all; the histogram
	//keeps the top kAdjacencyMaxOffsets by count and folds the rest into Other.
	struct SpritePairStat
	{
		uint32_t A = 0;
		uint32_t B = 0;
		//Frames both shapes were on screen at all, any distance. Written for
		//every pair with CoFrames >= kAdjacencyMinPairCount, so a pair may exist
		//with Count 0 and empty Offsets - actors that share scenes but never
		//come within 32 px of each other (a boss and a first-level enemy).
		uint32_t CoFrames = 0;
		uint32_t Count = 0; //within-cap directed sightings: sum(Offsets) + Other
		std::vector<SpriteOffsetSample> Offsets;
		uint32_t Other = 0;
	};

	//Everything the adjacency serializer needs from the OAM stream, beyond the
	//sprite vocabulary itself (a node's appearances are its vocabulary Count).
	struct SpriteAdjacencyStats
	{
		//Per sprite-vocabulary index: bottom-edge bands, most-seen first.
		std::vector<std::vector<SpriteFloorBand>> Floors;
		//Per sprite-vocabulary index: how many distinct (X, Y) screen positions
		//the shape was ever drawn at, and how many retained frames it appeared
		//in at all (once per frame, however many instances). Together they say
		//how often the shape came back to a place it had already been, which is
		//what tells furniture from an actor - see ScreenFixed below. Both are
		//written to the file, so a reader can second-guess the classification.
		std::vector<uint32_t> Positions;
		std::vector<uint32_t> NodeFrames;
		//Per sprite-vocabulary index: the shape never moved during the capture,
		//so its Floors are a coincidence of where it is painted on the screen
		//and not evidence of a ground it stands on (ADR-0173). Floors are kept
		//either way - this is a label on the evidence, not a deletion of it.
		std::vector<uint8_t> ScreenFixed;
		//Every unordered pair with CoFrames >= kAdjacencyMinPairCount, sorted
		//by (A, B).
		std::vector<SpritePairStat> Pairs;
		//Retained, de-duplicated frames the counts above live in. The file
		//states its sampling per block (distinctScreens for the background)
		//rather than once in the header, so neither read as a fraction of the
		//wrong universe.
		uint32_t OamFrames = 0;
	};

	//---- F9.19 (ADR-0170): sheets/poses.json -------------------------------

	//One tile of a pose: a sprite-vocabulary node (the same index space as
	//adjacency.json sprites.nodes[]) at an 8 px cell offset from the pose's
	//own top-left. Offsets come from SpriteGrouping's round-to-nearest-cell
	//rule, so a metasprite sitting a few pixels off the grid still lands on
	//the cell an artist would draw it in.
	struct PoseTile
	{
		uint32_t Node = 0;
		int32_t Dx = 0;
		int32_t Dy = 0;
		//ADR-0225 §1: the tile's top-left in native pixels from the pose origin
		//(Dx == ToCells(Px)), and its front-to-back OAM rank (0 = frontmost) -
		//written as `z` only when the pose's tiles overlap. Deliberately NOT
		//part of ==/<: identity stays on the rounded (Node, Dx, Dy) set.
		int32_t Px = 0;
		int32_t Py = 0;
		int32_t Z = 0;

		bool operator==(const PoseTile& o) const { return Node == o.Node && Dx == o.Dx && Dy == o.Dy; }
		bool operator<(const PoseTile& o) const
		{
			if(Dy != o.Dy) { return Dy < o.Dy; }
			if(Dx != o.Dx) { return Dx < o.Dx; }
			return Node < o.Node;
		}
	};

	//One distinct silhouette: a set of (node, dx, dy), normalised to its own
	//top-left. Two clusters are the same pose when the sets are equal, and
	//identical sets merge, summing the frames they were seen in. Tiles are
	//kept sorted (Dy, Dx, Node) so the set comparison and the file are
	//deterministic for a given stream.
	//ADR-0179 §2: one first-order succession edge, pose -> Pose, seen Count
	//times on the retained stream.
	struct PoseLink
	{
		uint32_t Pose = 0;
		uint32_t Count = 0;
	};

	struct PoseEntry
	{
		std::vector<PoseTile> Tiles;
		//ADR-0225 §1: some pair of Tiles overlaps at pixel precision, so the
		//sidecar writes each tile's Z.
		bool Overlaps = false;
		//Retained frames this silhouette was seen in, RepeatCount included -
		//unlike the pair statistics, which ignore RepeatCount on purpose. A
		//pose is a still, so a paused screen showing one really is evidence
		//that the pose exists; it cannot manufacture a *second* pose.
		uint32_t Frames = 0;
		//Extent in cells, for a consumer that lays poses out without walking
		//Tiles[].
		uint32_t Width = 0;
		uint32_t Height = 0;
		//ADR-0177: the two poses this one's tiles split into, as positions in
		//PoseStats::Poses, when it is a fusion - two figures that touched and
		//so were clustered as one. Empty means *not classified as a fusion*,
		//never *proved not to be one*. Both parts are kept poses themselves,
		//and they may be the same pose twice (two copies side by side).
		//ADR-0228: a single position when the entry is a kept pose plus a
		//remainder of kPoseMinTiles or more tiles that never stood alone.
		std::vector<uint32_t> FusionOf;
		//ADR-0179 §2: retained-frame transitions on which this pose was linked
		//to itself (the figure held still or moved without changing shape).
		uint32_t Hold = 0;
		//ADR-0179 §2: the poses this one was linked to, most-linked first.
		//Raw first-order evidence - see the fork in §Context; the animation
		//itself is PoseStats::Cycles / Sequences.
		std::vector<PoseLink> Next;
		//ADR-0179 §4: the kept pose this one is a strict superset of, when the
		//remainder is below kPoseMinTiles (a figure plus its projectile). -1
		//means *not classified as a variant*, never *proved not to be one*.
		int32_t VariantOf = -1;
	};

	//ADR-0179 §3: one closed loop (a cycle) or one non-looping run (a
	//sequence) of kept poses found by repetition on the tracks. Poses are
	//indexes into PoseStats::Poses; Hold is the median held frames per
	//position, in RepeatCount-weighted frames, so a reader can play it at
	//the game's own cadence. A pose may occur more than once in a cycle -
	//Contra's player run is period 6 with one silhouette at two phases.
	struct PoseRun
	{
		std::vector<uint32_t> Poses;
		std::vector<uint32_t> Hold;
		//Cycle: consecutive repetitions of the period on tracks, summed over
		//tracks. Sequence: identical occurrences.
		uint32_t Repeats = 0;
		//ADR-0181 §3 (cycles only): windows the rule judged, how many stopped
		//within kDriverStopLag of a release on each port, and the verdict -
		//0 = no driver written, 1 = "port1", 2 = "port2". A sequence keeps 0.
		uint32_t Windows = 0;
		uint32_t Stops[2] = {};
		uint8_t Driver = 0;
	};

	//What BuildPoses found, with the counts a reader needs to judge
	//truncation (ADR-0170 §2): the file states its own sampling.
	//ADR-0181 §2: the controller evidence of a recording, summed over the
	//retained stream. Buttons are indexed in OamFrame::Buttons bit order
	//(kButtonNames); a button counts as held in a frame when either port held
	//it, so a two-player run reads as one report and `Ports` says how many
	//ports ever pressed anything. Counts are RepeatCount-weighted, the same
	//universe PoseStats::Frames is - and, as OamFrame::Buttons says, a lower
	//bound. `Pairs` is the directional+action matrix (Up/Down/Left/Right x
	//A/B) the `never` list is read off: a pair the run never held at once is a
	//move the sidecar cannot have seen.
	constexpr uint32_t kButtonCount = 8;
	constexpr const char* kButtonNames[kButtonCount] = { "A", "B", "Select", "Start", "Up", "Down", "Left", "Right" };
	//One run of a pose on a track (ADR-0179 §1): the retained frame index it
	//began on, the kept pose, and the RepeatCount-weighted frames it held.
	struct PoseTrackRun
	{
		uint32_t Frame = 0;
		uint32_t Pose = 0;
		uint32_t Held = 0;
	};

	struct InputStats
	{
		uint32_t Frames = 0;
		uint32_t Ports = 0;
		uint32_t Held[kButtonCount] = {};
		uint32_t Pairs[4][2] = {};

		bool Any() const { return Ports > 0; }
	};

	struct PoseStats
	{
		//Kept poses, by Frames descending then by Tiles, capped at kMaxPoses.
		std::vector<PoseEntry> Poses;
		//Frames the counts live in: retained OamFrames weighted by
		//RepeatCount, the same universe PoseEntry::Frames is counted in.
		uint32_t Frames = 0;
		//Retained, de-duplicated OamFrames - adjacency.json's "oamFrames".
		uint32_t RetainedFrames = 0;
		//Distinct silhouettes before the kPoseMinFrames threshold, and after
		//it but before the kMaxPoses cap.
		uint32_t PosesFound = 0;
		uint32_t PosesKept = 0;
		//ADR-0179: tracks the linker followed, and what repetition found on
		//them. Both vectors are ordered by Repeats descending, then by Poses,
		//so "cycleNNN"/"seqNNN" is the array position.
		uint32_t Tracks = 0;
		std::vector<PoseRun> Cycles;
		std::vector<PoseRun> Sequences;
		//ADR-0181 §2: what the recording exercised on the controller.
		InputStats Input;
		//The tracks themselves, for the save-time debug dump and the ADR-0181
		//§3 measurement (which cycle answers which button). Not a pack file.
		std::vector<std::vector<PoseTrackRun>> TrackRuns;
	};

	//---- F9.18 (ADR-0166): the owning screen of a resident node -------------

	//One captured frame showing a screen-resident node, and the node's grid
	//position on it. Row/Col are the 8 px tile coordinates of the node's
	//top-left tile (the same key CollectScreen places cells with), so the art
	//origin in 1x NES pixels is (Col * 8, Row * 8) and a `unit`-pixel crop
	//equals the node's tiles. `Frame` is the index into the retained GridFrame
	//stream, which the builder maps to the screenNNN file that froze it.
	struct ScreenSight
	{
		uint32_t Frame = 0;
		uint32_t Row = 0;
		uint32_t Col = 0;
	};

	//One owned-screen site the adjacency serializer persists on a resident
	//background node: the file stem (matches textures/backgrounds/ and the
	//hires.txt <background> the capture became) plus the 8 px grid position.
	struct ScreenSite
	{
		std::string Screen; //"screenNNN"
		uint32_t X = 0;     //8 px tile column of the node's top-left tile
		uint32_t Y = 0;     //8 px tile row
	};

	//---- vocabulary --------------------------------------------------------

	//A building block: 4 shapes (row-major) at grid unit 16, 1 shape + three
	//kEmptyCell at grid unit 8.
	struct MetatileKey
	{
		std::array<ShapeId, 4> Tiles = { kEmptyCell, kEmptyCell, kEmptyCell, kEmptyCell };

		bool operator==(const MetatileKey& o) const { return Tiles == o.Tiles; }
		bool operator<(const MetatileKey& o) const { return Tiles < o.Tiles; }
	};

	//Which sheet a cell belongs on. Split by context so a rupee counter never
	//sits between two trees (ADR-0153 §3).
	enum class SheetContext
	{
		Scene = 0,
		Hud = 1,
		Font = 2,
		Misc = 3
	};

	const char* ContextName(SheetContext context);

	//Automatic grid detection result; both the winner and the loser are
	//reported so a bad pick can be argued with (ADR-0153 §1).
	struct GridDetection
	{
		uint32_t Unit = 8;          //16 or 8
		uint8_t PhaseX = 0;         //cell parity the 16x16 grid is aligned to
		uint8_t PhaseY = 0;
		double ChosenConsistency = 0;
		double Alt8x8 = 0;
		double PhaseScores[4] = {}; //(0,0) (1,0) (0,1) (1,1)
		double PhaseAdvantage = 0;  //1 - distinct(best) / mean(distinct(others))
		bool HasGrid = false;       //false = the 2x2 cut is arbitrary, not the game's grid
	};

	struct MetatileEntry
	{
		MetatileKey Key;
		uint32_t Count = 0;
		SheetContext Context = SheetContext::Scene;
		bool Aligned = true; //false when only ever seen off the chosen phase
		//F9.9 (ADR-0156): every sighting of this cell, in the whole recorded
		//stream, sat where a captured screen already shows it, under the same
		//fine scroll. A `<background>` therefore covers it wherever it appears,
		//so its tile art is never the pixel that reaches the screen and a cell
		//spent on it in metatiles.png is dead paint. Kept in the vocabulary
		//(indexes are addresses - maps, objects and sprites cite them), left
		//off the contact sheet.
		bool ScreenResident = false;
	};

	//Directed adjacency counts between vocabulary entries, keyed (A, B).
	using AdjacencyMap = std::map<std::pair<uint32_t, uint32_t>, uint32_t>;

	//---- F9.9 routing floor (ADR-0156) -------------------------------------
	//
	//"Every sighting is explained" is trivially true of a recording that never
	//left one screen: the screen is captured, nothing else is ever seen, and
	//the whole scene vocabulary routes off metatiles.png. The pack that ships
	//is then worse than the one before the rule - an almost empty contact
	//sheet, and no way to tell it from a game that really is one screen.
	//
	//So routing is withheld unless the recording looks like gameplay. These are
	//two of the four clauses of scripts/gameplay_probe.py, chosen because they
	//need only what the builder already has at save time; the thresholds are
	//that script's, calibrated over 86 packs from three runs of a 30-ROM
	//library (17 of 20 hand-labelled menu-only recordings caught, no false
	//alarms).
	//
	//  - tile structure: how deterministically the frames reuse the same 2x2
	//    tuples. A tiled playfield saturates near 1.0; a run that only drew
	//    one-off compositions - a logo, a menu, a portrait - never accumulates
	//    repeats. Menu-only 0.76-0.87, gameplay >= 0.89.
	//  - misc share: cells off the detected grid with no adjacency support. A
	//    menu is drawn at text granularity, not on the game's grid. Gameplay
	//    <= 0.262; a Gauntlet run stuck in its menu, 0.635.
	//
	//Deliberately conservative in one direction only: a false "this is not
	//gameplay" costs the artist a fatter sheet, which is what they had before
	//F9.9. A false "this is gameplay" costs them the sheet.
	constexpr double kGameplayTileStructure = 0.86;
	constexpr double kGameplayMiscShare = 0.32;

	//A second floor, for the failure the two clauses above cannot see. Residency
	//is proved against the *retained* frames, which are a sample (kMaxSheetFrames);
	//for a game whose screen space is combinatorial - Tetris, where every board
	//state is another screen - that sample says "every sighting is covered" when
	//what it means is "the recording did not last long enough to see otherwise".
	//The pack then ships with no metatiles.png at all and the artist has nothing
	//to paint but the handful of screens that happened to be captured; on a
	//variant whose anchors miss (issue #164), the result is vanilla.
	//
	//Measured over the 30-ROM library re-recorded on 2026-09-05, as a share of
	//the *scene* vocabulary: six games sit at 0.974-1.000 and leave a sheet of
	//1 to 10 cells (Mario Bros. 106/106 and no sheet at all, Tennis 206/207,
	//Tetris 648/651, Donkey Kong 171/172, Golf 184/186, Tetris 2 376/386). The
	//next game down is Zelda 1 at 0.879, which still leaves 28 usable cells, and
	//below it the field is continuous (Lifeforce 0.770, Mega Man 2 0.754,
	//Punch-Out!! 0.637, Super Mario Bros. 0.106). 0.93 is the midpoint of that
	//gap. Above it, the evidence is treated as an artefact of sampling and
	//nothing is routed - the same asymmetry as the clauses above.
	constexpr double kMaxRoutedSceneShare = 0.93;

	//The share above is the wrong axis on its own, and the library said so on
	//the second measurement: the gap it was cut from (0.879 to 0.974 in one run)
	//did not reproduce - the next run put Bomberman at 0.899, Zelda 1 at 0.900
	//and Pac-Man at 0.901, straight through the middle of it. A share moves with
	//the size of the vocabulary and with the luck of the recording.
	//
	//So the second floor is on the artefact the artist actually opens: what is
	//left on the contact sheet. Below this many scene cells it is not a contact
	//sheet, it is a leftover - those three packs kept 13, 21 and 23 cells, which
	//is the very failure kMaxRoutedSceneShare exists to prevent, arriving from
	//underneath. Measured on the 2026-09-05 re-record, where the remaining
	//sheets run 13, 21, 23, then 37, 42, 46, 55 - but the number is deliberately
	//not read off a gap this time: it is a floor on usefulness, which is why it
	//is an absolute count and not a ratio.
	constexpr uint32_t kMinSceneSheetCells = 30;

	enum class RoutingWithhold
	{
		None = 0,
		NotGameplay = 1, //the two gameplay clauses above
		SamplingCap = 2, //kMaxRoutedSceneShare
		SheetTooThin = 3, //kMinSceneSheetCells
	};

	struct Vocabulary
	{
		GridDetection Grid;
		std::vector<MetatileEntry> Entries;
		//Vocabulary index of a metatile key.
		std::map<MetatileKey, uint32_t> Index;
		//Top rows that never change across distinct stable screens (the HUD).
		uint32_t HudRows = 0;
		//Bottom rows that never change (Metroid/SMB3-style status bars).
		uint32_t HudBottomRows = 0;
		AdjacencyMap East;
		AdjacencyMap South;
		uint32_t StableScreens = 0;
		uint32_t DistinctScreens = 0;
		//F9.9: why nothing was routed, when nothing was. "Withheld" and
		//"there was nothing to route" are the same cell count and not the same
		//event - and the two floors are not the same event either: Tetris is
		//gameplay by any reading, and was still capped for routing 648 of its
		//651 scene cells.
		RoutingWithhold Withheld = RoutingWithhold::None;

		int32_t Find(const MetatileKey& key) const
		{
			auto it = Index.find(key);
			return it == Index.end() ? -1 : (int32_t)it->second;
		}
	};

	//---- sheets ------------------------------------------------------------

	//A cell as laid out on a sheet. X/Y are the top-left in sheet pixels.
	struct SheetCell
	{
		uint32_t Index = 0;
		int32_t X = 0;
		int32_t Y = 0;
		uint32_t Count = 0;
		SheetContext Context = SheetContext::Scene;
		MetatileKey Key;
		int32_t Metatile = -1; //vocabulary index, for object/sprite/map cells
		//Vocabulary indexes that render to this same cell (ADR-0153 §3, alias
		//pass). Empty when the cell is the only key for its subject. The artist
		//paints the cell once; mep_build.py fans the crop back out to every
		//alias, so a bank-swapped duplicate can never drift out of sync with
		//the copy that was painted. AliasKeys carries their tile keys in the
		//same order: the sidecar has to be self-contained, or the round-trip
		//would need the vocabulary the artist never receives.
		std::vector<uint32_t> Aliases;
		std::vector<MetatileKey> AliasKeys;
		//ADR-0230 (F14.9): the Index of the cell this one is a palette variant
		//of - the same shapes drawn in another palette the recording saw - or
		//-1 for an ordinary cell. A variant carries no vocabulary index
		//(Metatile stays -1) so a map placement never resolves to it.
		int32_t VariantOf = -1;
	};

	//0xAARRGGBB, alpha 0 outside a cell (gutters and padding stay transparent).
	struct SheetImage
	{
		uint32_t Width = 0;
		uint32_t Height = 0;
		std::vector<uint32_t> Pixels;

		void Reset(uint32_t w, uint32_t h)
		{
			Width = w;
			Height = h;
			Pixels.assign((size_t)w * h, 0);
		}
		uint32_t* Row(uint32_t y) { return Pixels.data() + (size_t)y * Width; }
	};

	//Where one vocabulary cell was painted on a stitched map, in map pixels.
	struct SheetPlacement
	{
		int32_t X = 0;
		int32_t Y = 0;
		uint32_t Cell = 0;
	};

	enum class StitchMode
	{
		Screen = 0,
		Continuous = 1
	};

	//One connected map region. Placements are the slicing contract mep_build.py
	//reads back; the map itself is a paint surface, never a runtime layer.
	struct StitchedMap
	{
		StitchMode Mode = StitchMode::Screen;
		uint32_t Width = 0;  //in pixels
		uint32_t Height = 0;
		uint32_t HudRows = 0;
		std::vector<SheetPlacement> Placements;
		std::vector<std::string> Log; //per-screen decisions, for the run log
	};

	//Evidence for one edge that joined two cells into an object or a sprite.
	//Dx/Dy is where B sits relative to A, in cells: always (1,0)/(0,1) for a
	//metatile edge, any direction for an OAM edge (F9.5), which is why the
	//layout reads the offset instead of re-deriving it from Dir.
	struct GroupEdge
	{
		uint32_t A = 0;
		uint32_t B = 0;
		char Dir = 'E'; //'E', 'S', and for sprites also 'W' / 'N'
		int32_t Dx = 1;
		int32_t Dy = 0;
		uint32_t Count = 0;
		double ProbAB = 0;
		double ProbBA = 0;
	};

	//One `spriteNearby` a group's own edges support: "the shape at Target sits
	//at (Dx, Dy) cells from me". Attached to Node's <tile> lines, it is the
	//machine form of the idiom a human HD pack uses to say *which cell of a
	//metasprite this is* - Contra80s' predcloaked1/2/3 are three tiles of one
	//picture, each gated on the same anchor sprite at a different offset.
	//
	//Count/ProbAB/ProbBA are copied off the GroupEdge that justified it, so the
	//emitted condition carries its own evidence: the two shapes held this exact
	//offset on Count retained frames, and that accounted for ProbAB/ProbBA of
	//the frames each of them appeared in at all. Nothing here is inferred from
	//meaning - it is the observation SelectSpriteEdges already made, re-read as
	//a condition (ADR-0183 §3).
	struct SpriteNearbyPlan
	{
		uint32_t Node = 0;   //sprite-vocabulary index the condition is attached to
		uint32_t Target = 0; //sprite-vocabulary index the condition looks for
		int32_t Dx = 0;      //where Target sits relative to Node, in cells
		int32_t Dy = 0;
		uint32_t Count = 0;
		double ProbAB = 0;
		double ProbBA = 0;
	};

	//ADR-0175 (issue #175): one slot of a group sheet's grid that the layout
	//deliberately left blank. Col/Row are in cells, the same units SheetGroup
	//lays its members out in, so a consumer reaches the sheet pixels with the
	//sidecar's own cell size and gutter.
	struct SheetSlot
	{
		uint32_t Col = 0;
		uint32_t Row = 0;

		bool operator==(const SheetSlot& o) const { return Col == o.Col && Row == o.Row; }
	};

	struct SheetGroup
	{
		std::vector<SheetCell> Cells; //Metatile = vocabulary index, X/Y in cells
		std::vector<GroupEdge> Edges;
		uint32_t Columns = 0;
		uint32_t Rows = 0;
	};
}
