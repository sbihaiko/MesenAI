#pragma once
//ADR-0230 (F14.9): a sheet cell reaches every palette its shape was drawn in.
//
//The recorder interns a shape palette-wildcarded, so every sheet renders one
//palette per shape - the first one seen - and before this the other palettes
//the recording drew (up to MaxPaletteVariantsPerTile of them, each its own
//hires.txt key) had no place on any organised sheet. For each such drawn
//(shape, palette) this header decides one of two things:
//
// - an *exact fold* (Decision item 2, refined on the #448 review): the palette
//   is the same picture as the cell's at another brightness, and the cell's
//   crop scaled by that one Brightness reproduces what the recording drew for
//   the key pixel for pixel. The cell's sidecar entry lists it under `folds`,
//   and mep_build.py emits one exact defaultTile=N rule per fold;
// - a *variant cell* (item 1) otherwise: a colourway (a painted entry changes
//   hue, or the residual fails the 25 degree gate), or a fold whose
//   least-squares Brightness leaves a residual (a screen fade on the NES
//   ramps). The variant is rendered in its own palette from the recorded art
//   and sits in a row inserted directly beneath its base cell, in the base
//   cell's column, so a grid's columns - a cycle's phases - keep their order.
//
//Everything here is host-free and inline: HdPackBuilder.cpp is not in the
//unit-test link set and sits at its ADR-0137 line ceiling, so it only collects
//the drawn palettes and calls PlanPaletteCells / ApplyPaletteCells.
//
//The fold predicate is a faithful port of scripts/palette_folds.py
//(artist_chr_kit's painted_indices, fade_related, fold_measure and its gate),
//decided off the 2C02 reference table below exactly as the kit does, so both
//surfaces agree on what a fold is. docs/specs/golden/sheets/
//palette-relation-cases.txt is checked by scripts/core_unit_tests and by
//scripts/test_palette_folds.py so the two cannot drift. Exactness, on the other
//hand, is judged on the palette the sheet is actually rendered with, because
//it is a claim about pixels.
#include "NES/HdPacks/SheetRender.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <map>
#include <cstring>
#include <set>
#include <string>
#include <tuple>
#include <vector>

namespace MesenSheets
{
	//The 2C02 table of NesDefaultVideoFilter.cpp, ARGB - the same numbers as
	//palette_folds.DEFAULT_PALETTE_ARGB.
	constexpr uint32_t kFoldReferencePalette[64] = {
		0xFF666666, 0xFF002A88, 0xFF1412A7, 0xFF3B00A4, 0xFF5C007E, 0xFF6E0040, 0xFF6C0600, 0xFF561D00,
		0xFF333500, 0xFF0B4800, 0xFF005200, 0xFF004F08, 0xFF00404D, 0xFF000000, 0xFF000000, 0xFF000000,
		0xFFADADAD, 0xFF155FD9, 0xFF4240FF, 0xFF7527FE, 0xFFA01ACC, 0xFFB71E7B, 0xFFB53120, 0xFF994E00,
		0xFF6B6D00, 0xFF388700, 0xFF0C9300, 0xFF008F32, 0xFF007C8D, 0xFF000000, 0xFF000000, 0xFF000000,
		0xFFFFFEFF, 0xFF64B0FF, 0xFF9290FF, 0xFFC676FF, 0xFFF36AFF, 0xFFFE6ECC, 0xFFFE8170, 0xFFEA9E22,
		0xFFBCBE00, 0xFF88D800, 0xFF5CE430, 0xFF45E082, 0xFF48CDDE, 0xFF4F4F4F, 0xFF000000, 0xFF000000,
		0xFFFFFEFF, 0xFFC0DFFF, 0xFFD3D2FF, 0xFFE8C8FF, 0xFFFBC2FF, 0xFFFEC4EA, 0xFFFECCC5, 0xFFF7D8A5,
		0xFFE4E594, 0xFFCFEF96, 0xFFBDF4AB, 0xFFB3F3CC, 0xFFB5EBF2, 0xFFB8B8B8, 0xFF000000, 0xFF000000,
	};
	//palette_folds.HUE_DRIFT_GATE_DEG.
	constexpr double kHueDriftGateDegrees = 25.0;

	//Palette entry k (0 = colour 0) of a hires.txt palette word, 6 bits.
	inline uint8_t FoldEntry(uint32_t palette, int k) { return (uint8_t)((palette >> ((3 - k) * 8)) & 0x3F); }

	inline void FoldRgb(uint8_t colour, int rgb[3])
	{
		uint32_t v = kFoldReferencePalette[colour & 0x3F];
		rgb[0] = (int)((v >> 16) & 0xFF);
		rgb[1] = (int)((v >> 8) & 0xFF);
		rgb[2] = (int)(v & 0xFF);
	}

	inline bool FoldIsBlack(uint8_t colour) { return (kFoldReferencePalette[colour & 0x3F] & 0xFFFFFF) == 0; }
	//0 for black, else the palette row + 1 - the rung of the hue's ramp.
	inline int FoldLevel(uint8_t colour) { return FoldIsBlack(colour) ? 0 : (colour >> 4) + 1; }

	//Bit k set when the 8x8 pattern paints colour index k (painted_indices).
	inline uint8_t PaintedIndexMask(const uint8_t* tileData)
	{
		uint8_t used = 0;
		for(int row = 0; row < 8; row++) {
			for(int bit = 0; bit < 8; bit++) {
				used |= (uint8_t)(1 << (((tileData[row] >> bit) & 1) | (((tileData[row + 8] >> bit) & 1) << 1)));
			}
		}
		return used == 0 ? 1 : used;
	}

	//`other` is `base` further down the same ramps: no painted entry changed
	//hue, none got brighter, and at least one moved (fade_related).
	inline bool FadeRelated(uint32_t base, uint32_t other, uint8_t used)
	{
		bool moved = false;
		for(int k = 0; k < 4; k++) {
			if(!(used & (1 << k))) {
				continue;
			}
			uint8_t a = FoldEntry(base, k);
			uint8_t b = FoldEntry(other, k);
			if(FoldIsBlack(a) && FoldIsBlack(b)) {
				continue;
			}
			if(FoldIsBlack(a) || FoldIsBlack(b)) {
				moved = true;
				continue;
			}
			if((a & 0x0F) != (b & 0x0F) || FoldLevel(b) > FoldLevel(a)) {
				return false;
			}
			if(FoldLevel(b) < FoldLevel(a)) {
				moved = true;
			}
		}
		return moved;
	}

	//fold_measure: the least-squares single multiplier taking `base` to
	//`other` (clamped to 0..4), and the largest hue angle between a painted
	//entry's two RGB vectors, in degrees.
	inline void FoldMeasure(uint32_t base, uint32_t other, uint8_t used, double& brightness, double& drift)
	{
		int64_t num = 0;
		int64_t den = 0;
		drift = 0.0;
		for(int k = 0; k < 4; k++) {
			if(!(used & (1 << k))) {
				continue;
			}
			int a[3], b[3];
			FoldRgb(FoldEntry(base, k), a);
			FoldRgb(FoldEntry(other, k), b);
			int64_t dot = 0, na2 = 0, nb2 = 0;
			for(int i = 0; i < 3; i++) {
				num += (int64_t)a[i] * b[i];
				den += (int64_t)a[i] * a[i];
				dot += (int64_t)a[i] * b[i];
				na2 += (int64_t)a[i] * a[i];
				nb2 += (int64_t)b[i] * b[i];
			}
			double na = std::sqrt((double)na2);
			double nb = std::sqrt((double)nb2);
			if(na == 0 || nb == 0) {
				continue;
			}
			double c = std::max(-1.0, std::min(1.0, (double)dot / (na * nb)));
			drift = std::max(drift, std::acos(c) * 180.0 / 3.14159265358979323846);
		}
		double b = den == 0 ? 0.0 : (double)num / (double)den;
		brightness = std::max(0.0, std::min(4.0, b));
	}

	enum class PaletteRelation : uint8_t
	{
		Inert = 0,
		Fade = 1,
		Brighter = 2,
		Colourway = 3
	};

	struct PaletteRelationResult
	{
		PaletteRelation Relation = PaletteRelation::Colourway;
		double Brightness = 0.0; //from the cell's palette to the other
		double Drift = 0.0;
	};

	//palette_folds.palette_relation: how `other` relates to the palette a
	//sheet cell carries, for this pattern. Inert, Fade and Brighter are folds;
	//Colourway is another picture (a hue change, or a residual over the gate).
	inline PaletteRelationResult ClassifyPaletteRelation(const uint8_t* tileData, uint32_t cellPalette, uint32_t other)
	{
		PaletteRelationResult r;
		uint8_t used = PaintedIndexMask(tileData);
		bool inert = true;
		for(int k = 0; k < 4 && inert; k++) {
			if(used & (1 << k)) {
				int a[3], b[3];
				FoldRgb(FoldEntry(cellPalette, k), a);
				FoldRgb(FoldEntry(other, k), b);
				inert = a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
			}
		}
		if(inert) {
			r.Relation = PaletteRelation::Inert;
			r.Brightness = 1.0;
			return r;
		}
		FoldMeasure(cellPalette, other, used, r.Brightness, r.Drift);
		bool withinGate = r.Drift <= kHueDriftGateDegrees;
		if(FadeRelated(cellPalette, other, used)) {
			r.Relation = withinGate ? PaletteRelation::Fade : PaletteRelation::Colourway;
		} else if(FadeRelated(other, cellPalette, used)) {
			r.Relation = withinGate ? PaletteRelation::Brighter : PaletteRelation::Colourway;
		}
		return r;
	}

	//The Brightness column as mep_build.py writes it: four decimals at most,
	//trailing zeroes dropped ("1", "0", "0.75").
	inline std::string FoldBrightnessText(double brightness)
	{
		char buf[32];
		snprintf(buf, sizeof(buf), "%.4f", brightness);
		std::string text = buf;
		while(!text.empty() && text.back() == '0') {
			text.pop_back();
		}
		if(!text.empty() && text.back() == '.') {
			text.pop_back();
		}
		return text.empty() ? "0" : text;
	}

	//HdPackLoader (version >= 105): `(int)(std::stof(column) * 255)`.
	inline int LoaderBrightness(const std::string& text) { return (int)(std::stof(text) * 255); }

	//HdNesPack::AdjustBrightness, one channel.
	inline int AdjustBrightnessChannel(int channel, int brightness) { return std::min(255, (brightness * (channel + 1)) >> 8); }

	//Decision item 2 as refined on the #448 review: the fold is admitted only
	//when the cell's pixels scaled by the loader's Brightness equal, on every
	//colour index the pattern paints, the pixels the recording drew for the
	//fold's palette. `palette` is the table the sheet is rendered with
	//(0x00RRGGBB, indexed like RenderTile).
	inline bool FoldIsExact(const uint8_t* tileData, uint32_t cellPalette, uint32_t other, int loaderBrightness, NesPalette palette)
	{
		uint8_t used = PaintedIndexMask(tileData);
		for(int k = 0; k < 4; k++) {
			if(!(used & (1 << k))) {
				continue;
			}
			uint32_t a = palette[FoldEntry(cellPalette, k)];
			uint32_t b = palette[FoldEntry(other, k)];
			for(int shift = 0; shift <= 16; shift += 8) {
				if(AdjustBrightnessChannel((int)((a >> shift) & 0xFF), loaderBrightness) != (int)((b >> shift) & 0xFF)) {
					return false;
				}
			}
		}
		return true;
	}

	//mep_build.py's _SHEET_RANK: which sheet a key's variant cell joins when the
	//shape sits on several. The most specific surface wins.
	inline int SheetKindRank(const std::string& kind)
	{
		if(kind == "hud" || kind == "font") {
			return 5;
		}
		if(kind == "object" || kind == "sprite") {
			return 4;
		}
		if(kind == "map") {
			return 3;
		}
		if(kind == "misc") {
			return 2;
		}
		if(kind == "metatiles" || kind == "sprites") {
			return 1;
		}
		return 0;
	}

	//The palettes hires.txt will carry for each shape id: every recorded
	//variant of the shape's key that still owns a CHR page slot. SaveHdPack
	//writes <tile> lines from the pages only, and AddTile evicts a variant
	//whose slot another one took, so a variant off the pages was never
	//drawn as far as the pack is concerned. `banks` is the recorder's
	//bank -> palette -> page-of-tile-pointers map, `variants` its key ->
	//tile-pointers map, `keyOf` a shape id's key in it. Templated so the
	//recorder's types never have to be linked into the unit tests.
	template<typename Banks, typename Variants, typename KeyOf>
	std::vector<std::vector<uint32_t>> WrittenPalettesByShape(size_t shapeCount, const Banks& banks, const Variants& variants, KeyOf keyOf)
	{
		std::set<const void*> written;
		for(const auto& bank : banks) {
			for(const auto& page : bank.second) {
				written.insert(page.second.begin(), page.second.end());
			}
		}
		std::vector<std::vector<uint32_t>> out(shapeCount);
		for(size_t s = 0; s < shapeCount; s++) {
			auto found = variants.find(keyOf((ShapeId)s));
			for(size_t v = 0; found != variants.end() && v < found->second.size(); v++) {
				if(found->second[v] && written.count(found->second[v])) {
					out[s].push_back(found->second[v]->PaletteColors);
				}
			}
		}
		return out;
	}

	//One sheet the recorder has built but not yet written: WriteSheetFiles
	//collects them so the variant pass sees every sheet before any is written.
	struct PendingSheet
	{
		std::string Folder;
		std::string BaseName;
		SheetImage Image;
		SheetJsonDoc Doc;
	};

	struct PaletteCellPlan
	{
		//A variant cell to insert beneath cell BaseCell of sheet Sheet.
		struct Cell
		{
			size_t Sheet = 0;
			size_t BaseCell = 0;
			uint32_t Palette = 0;
			MetatileKey Key;
			bool Colourway = false; //false: every key in it is a residual fold
		};

		size_t ShapeCount = 0;
		std::vector<SheetTileKey> VariantTiles; //shape id ShapeCount + i
		ShapeFolds Folds;
		std::vector<Cell> Cells;
		uint32_t ExactFoldKeys = 0;
		uint32_t ResidualFoldKeys = 0;
		uint32_t ColourwayKeys = 0;
		uint32_t UnplacedKeys = 0; //no organised sheet holds the shape, or the id space ran out

		const SheetTileKey* Variant(ShapeId id) const
		{
			return id >= ShapeCount && id - ShapeCount < VariantTiles.size() ? &VariantTiles[id - ShapeCount] : nullptr;
		}
	};

	//The hires.txt identity of a shape's key, palette aside: the CHR index on a
	//CHR ROM game, else the unflipped data (ADR-0172, ADR-0178).
	inline std::string ShapeSourceKey(const SheetTileKey& tile)
	{
		if(tile.TileIndex >= 0) {
			return "#" + std::to_string(tile.TileIndex);
		}
		return std::string((const char*)tile.SourceTileData, 16);
	}

	//Decides, for every drawn (shape, palette) no sheet cell carries, whether it
	//is an exact fold of a cell's palette (listed on that shape) or a variant
	//cell, and where the variant goes: beside the shape's cell on the
	//highest-ranked non-map sheet, first sheet and first cell on ties.
	//`drawnPalettes[s]` is every palette the recording drew shape s in (the
	//hires.txt defaultTile=N keys of its data); every shape's own palette is
	//what the sheets already carry, since the unsorted sheet claims every shape
	//no other sheet did.
	inline PaletteCellPlan PlanPaletteCells(const std::vector<PendingSheet>& sheets, const std::vector<SheetTileKey>& shapeTiles, const std::vector<std::vector<uint32_t>>& drawnPalettes, NesPalette palette)
	{
		PaletteCellPlan plan;
		plan.ShapeCount = shapeTiles.size();
		std::map<std::string, std::vector<ShapeId>> bySource;
		std::set<std::pair<std::string, uint32_t>> carried;
		for(size_t s = 0; s < shapeTiles.size() && s < kEmptyCell; s++) {
			std::string src = ShapeSourceKey(shapeTiles[s]);
			bySource[src].push_back((ShapeId)s);
			carried.insert({ src, shapeTiles[s].PaletteColors });
		}

		//Where each shape sits on an organised sheet: (rank, -sheet, -cell) is
		//maximised, so the first sheet and first cell win a tie.
		struct Site
		{
			int Rank = -1;
			size_t Sheet = 0;
			size_t Cell = 0;
			uint32_t Tile = 0;
		};
		std::vector<Site> site(shapeTiles.size());
		for(size_t d = 0; d < sheets.size(); d++) {
			const SheetJsonDoc& doc = sheets[d].Doc;
			if(doc.IsMap) {
				continue;
			}
			int rank = SheetKindRank(doc.Kind);
			uint32_t tiles = doc.Grid.Unit >= 16 ? 4 : 1;
			for(size_t c = 0; c < doc.Cells.size(); c++) {
				for(uint32_t i = 0; i < tiles; i++) {
					ShapeId s = doc.Cells[c].Key.Tiles[i];
					if(s < site.size() && rank > site[s].Rank) {
						site[s] = { rank, d, c, i };
					}
				}
			}
		}

		std::set<std::pair<std::string, uint32_t>> decided;
		std::map<std::tuple<size_t, size_t, uint32_t>, size_t> cellAt;
		for(size_t s = 0; s < shapeTiles.size() && s < drawnPalettes.size() && s < kEmptyCell; s++) {
			std::string src = ShapeSourceKey(shapeTiles[s]);
			for(uint32_t p : drawnPalettes[s]) {
				std::pair<std::string, uint32_t> key = { src, p };
				if(carried.count(key) || !decided.insert(key).second) {
					continue;
				}
				const std::vector<ShapeId>& candidates = bySource[src];
				bool anyFold = false;
				bool folded = false;
				for(ShapeId c : candidates) {
					PaletteRelationResult rel = ClassifyPaletteRelation(shapeTiles[c].SourceTileData, shapeTiles[c].PaletteColors, p);
					if(rel.Relation == PaletteRelation::Colourway) {
						continue;
					}
					anyFold = true;
					std::string text = FoldBrightnessText(rel.Brightness);
					if(FoldIsExact(shapeTiles[c].SourceTileData, shapeTiles[c].PaletteColors, p, LoaderBrightness(text), palette)) {
						plan.Folds[c].push_back({ p, text });
						folded = true;
						break;
					}
				}
				if(folded) {
					plan.ExactFoldKeys++;
					continue;
				}
				(anyFold ? plan.ResidualFoldKeys : plan.ColourwayKeys)++;

				ShapeId owner = kEmptyCell;
				for(ShapeId c : candidates) {
					if(site[c].Rank < 0) {
						continue;
					}
					if(owner == kEmptyCell || site[c].Rank > site[owner].Rank ||
						(site[c].Rank == site[owner].Rank && (site[c].Sheet < site[owner].Sheet || (site[c].Sheet == site[owner].Sheet && site[c].Cell < site[owner].Cell)))) {
						owner = c;
					}
				}
				if(owner == kEmptyCell || plan.ShapeCount + plan.VariantTiles.size() >= kEmptyCell) {
					plan.UnplacedKeys++;
					continue;
				}
				SheetTileKey variant = shapeTiles[owner];
				variant.PaletteColors = p;
				ShapeId variantId = (ShapeId)(plan.ShapeCount + plan.VariantTiles.size());
				plan.VariantTiles.push_back(variant);

				const Site& at = site[owner];
				std::tuple<size_t, size_t, uint32_t> where = std::make_tuple(at.Sheet, at.Cell, p);
				auto found = cellAt.find(where);
				if(found == cellAt.end()) {
					PaletteCellPlan::Cell cell;
					cell.Sheet = at.Sheet;
					cell.BaseCell = at.Cell;
					cell.Palette = p;
					for(ShapeId& t : cell.Key.Tiles) {
						t = kEmptyCell;
					}
					found = cellAt.emplace(where, plan.Cells.size()).first;
					plan.Cells.push_back(cell);
				}
				plan.Cells[found->second].Key.Tiles[at.Tile] = variantId;
				plan.Cells[found->second].Colourway |= !anyFold;
			}
		}
		//Sheet, then base cell, then palette: the order the rows are laid out in.
		std::stable_sort(plan.Cells.begin(), plan.Cells.end(), [](const PaletteCellPlan::Cell& a, const PaletteCellPlan::Cell& b) {
			return std::make_tuple(a.Sheet, a.BaseCell, a.Palette) < std::make_tuple(b.Sheet, b.BaseCell, b.Palette);
		});
		return plan;
	}

	//Lays one sheet's variant cells out: for every row of the sheet's grid that
	//holds a base cell with variants, as many rows as its most-varied cell needs
	//are inserted directly beneath it, and variant j of a base cell goes in the
	//j-th inserted row, in the base cell's column. Nothing is interleaved inside
	//a row, so the columns - a cycle's phases - keep their order, and the rows
	//below move down whole. `variants` is (base cell position, key) in layout
	//order. A variant cell carries no vocabulary index (a map placement must
	//never resolve to it) and names its base cell in VariantOf. On an object or
	//sprite group sheet the slots an inserted row leaves blank are listed in
	//EmptySlots, as ADR-0175 requires of that grid.
	inline void InsertVariantCells(SheetJsonDoc& doc, SheetImage& image, const std::vector<std::pair<size_t, MetatileKey>>& variants, const TileLookup& lookup, NesPalette palette)
	{
		if(variants.empty() || doc.IsMap || image.Width == 0) {
			return;
		}
		int32_t gutter = (int32_t)doc.Gutter;
		int32_t strideX = (int32_t)doc.CellWidth + gutter;
		int32_t strideY = (int32_t)doc.CellHeight + gutter;
		auto rowOf = [&](int32_t y) { return y < gutter ? 0 : (uint32_t)((y - gutter) / strideY); };

		std::vector<uint32_t> perCell(doc.Cells.size(), 0);
		for(const std::pair<size_t, MetatileKey>& v : variants) {
			if(v.first < perCell.size()) {
				perCell[v.first]++;
			}
		}
		uint32_t rows = rowOf((int32_t)image.Height) + 2;
		std::vector<uint32_t> need(rows, 0);
		for(size_t c = 0; c < doc.Cells.size(); c++) {
			uint32_t r = std::min(rowOf(doc.Cells[c].Y), rows - 1);
			need[r] = std::max(need[r], perCell[c]);
		}
		std::vector<uint32_t> extra(rows + 1, 0); //inserted rows above row r
		for(uint32_t r = 0; r < rows; r++) {
			extra[r + 1] = extra[r] + need[r];
		}
		auto shiftOf = [&](int32_t y) { return (int32_t)extra[std::min(rowOf(y), rows - 1)] * strideY; };

		SheetImage out;
		out.Reset(image.Width, image.Height + extra[rows] * (uint32_t)strideY);
		for(uint32_t y = 0; y < image.Height; y++) {
			memcpy(out.Row(y + (uint32_t)shiftOf((int32_t)y)), image.Row(y), (size_t)image.Width * sizeof(uint32_t));
		}

		bool transparent = doc.Kind == "sprite" || doc.Kind == "sprites";
		uint32_t nextIndex = 0;
		for(const SheetCell& cell : doc.Cells) {
			nextIndex = std::max(nextIndex, cell.Index + 1);
		}
		std::vector<SheetCell> cells;
		std::vector<SheetSlot> filled;
		size_t v = 0;
		std::vector<std::pair<size_t, MetatileKey>> ordered = variants;
		std::stable_sort(ordered.begin(), ordered.end(), [](const std::pair<size_t, MetatileKey>& a, const std::pair<size_t, MetatileKey>& b) { return a.first < b.first; });
		for(size_t c = 0; c < doc.Cells.size(); c++) {
			SheetCell base = doc.Cells[c];
			base.Y += shiftOf(doc.Cells[c].Y);
			cells.push_back(base);
			for(uint32_t j = 0; v < ordered.size() && ordered[v].first == c; v++, j++) {
				SheetCell variant;
				variant.Index = nextIndex++;
				variant.X = base.X;
				variant.Y = base.Y + (int32_t)(j + 1) * strideY;
				variant.Context = base.Context;
				variant.Key = ordered[v].second;
				variant.VariantOf = (int32_t)base.Index;
				RenderMetatile(variant.Key, lookup, palette, doc.Grid.Unit, out, variant.X, variant.Y, transparent);
				cells.push_back(variant);
				filled.push_back({ (uint32_t)(base.X < gutter ? 0 : (base.X - gutter) / strideX), rowOf(variant.Y) });
			}
		}

		for(SheetSlot& slot : doc.EmptySlots) {
			slot.Row += extra[std::min(slot.Row, rows - 1)];
		}
		if(doc.Kind == "object" || doc.Kind == "sprite") {
			for(uint32_t r = 0; r < rows; r++) {
				for(uint32_t j = 0; j < need[r]; j++) {
					uint32_t newRow = r + extra[r] + 1 + j;
					for(uint32_t col = 0; col < doc.Columns; col++) {
						if(std::find(filled.begin(), filled.end(), SheetSlot{ col, newRow }) == filled.end()) {
							doc.EmptySlots.push_back({ col, newRow });
						}
					}
				}
			}
			std::sort(doc.EmptySlots.begin(), doc.EmptySlots.end(), [](const SheetSlot& a, const SheetSlot& b) { return a.Row != b.Row ? a.Row < b.Row : a.Col < b.Col; });
		}
		doc.Cells = cells;
		image = out;
	}

	//Applies a plan to the pending sheets: every sheet with variant cells gets
	//them inserted and rendered. `lookup` must resolve the variant ids too.
	inline void ApplyPaletteCells(std::vector<PendingSheet>& sheets, const PaletteCellPlan& plan, const TileLookup& lookup, NesPalette palette)
	{
		for(size_t d = 0; d < sheets.size(); d++) {
			std::vector<std::pair<size_t, MetatileKey>> variants;
			for(const PaletteCellPlan::Cell& cell : plan.Cells) {
				if(cell.Sheet == d) {
					variants.push_back({ cell.BaseCell, cell.Key });
				}
			}
			InsertVariantCells(sheets[d].Doc, sheets[d].Image, variants, lookup, palette);
		}
	}
}
