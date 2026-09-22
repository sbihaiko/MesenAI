#pragma once
//ADR-0209 Q1 (b): the default `label` of a sidecar entry, inferred by the Core
//at record time from the grouping it already computed, as a default the artist
//renames. Host-free on purpose - string formatting over structs that already
//exist - so scripts/core_unit_tests.cpp links it without HdPackBuilder.
//
//What a label may say (ADR-0183 §3/§5): only what the recording measured.
//The scheme names the recorder's own classification (`context`, the sheet
//`kind`), the vocabulary index, the extent in cells and the counts. It never
//claims semantics it cannot know - no "player", no "enemy", no "tree" - and
//every label written from here is marked `"labelSource": "inferred"` so a
//reader can tell it from a human's name and let the human's win (ADR-0183 §5:
//a names.json caption beats it in every reader).
//
//Deterministic and stable: the inputs are the serialised fields themselves,
//so two saves of one recording produce the same labels, and a label never
//depends on anything the sidecar does not also state.
//
//The scheme, exactly (ASCII only, never a double quote, so it needs no JSON
//escaping):
//
//  cell   "<ctx> #<metatile> x<count>"        a vocabulary-backed cell
//         "<ctx> cell <index> x<count>"       a cell with no vocabulary index
//         ""                                  no grouping data at all (count 0
//                                             and no vocabulary index): no
//                                             label, no labelSource
//         <ctx> is "sprite" on a sprite/sprites sheet, else the cell's own
//         `context` (scene | hud | font | misc).
//  sheet  "<kind> group <cols>x<rows>, <n> cells[, <p> poses], x<max count>"
//         sprite/object group sheets only - a vocabulary sheet is not a
//         figure and gets none.
//  pose   "figure <w>x<h>, <t> tiles, <f> frames[, fusion][, variant of poseNNN]"
//  run    "<loop|sequence> of <n> phases, <w>x<h>, x<repeats>[, driver portN]"
//         <w>x<h> is the largest phase extent, the box an artist paints in.
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>
#include "NES/HdPacks/TileSheetTypes.h"

namespace MesenSheets
{
	constexpr const char* kLabelSourceInferred = "inferred";

	inline std::string LabelNumber(uint64_t value)
	{
		char buf[32];
		snprintf(buf, sizeof(buf), "%llu", (unsigned long long)value);
		return buf;
	}

	//The word the cell's classification reads as. The sheet kind wins for a
	//sprite: RenderGroup leaves a sprite cell's Context at its default (Scene),
	//which is a statement about the background vocabulary, not about OAM.
	inline const char* LabelContext(const SheetCell& cell, const std::string& sheetKind)
	{
		if(sheetKind == "sprite" || sheetKind == "sprites") {
			return "sprite";
		}
		return ContextName(cell.Context);
	}

	inline std::string InferCellLabel(const SheetCell& cell, const std::string& sheetKind)
	{
		if(cell.Count == 0 && cell.Metatile < 0) {
			return "";
		}
		std::string label = LabelContext(cell, sheetKind);
		if(cell.Metatile >= 0) {
			label += " #" + LabelNumber((uint64_t)cell.Metatile);
		} else {
			label += " cell " + LabelNumber(cell.Index);
		}
		label += " x" + LabelNumber(cell.Count);
		return label;
	}

	//`kind`, `columns`, the cells and the blank slots are what SheetJsonDoc
	//carries; the caller hands them over so this header does not depend on
	//SheetRender.h (which is what includes it).
	inline std::string InferSheetLabel(const std::string& kind, uint32_t columns, const std::vector<SheetCell>& cells, size_t emptySlots, size_t poses)
	{
		if(kind != "sprite" && kind != "object") {
			return "";
		}
		if(cells.empty()) {
			return "";
		}
		uint32_t cols = columns == 0 ? 1 : columns;
		size_t slots = cells.size() + emptySlots;
		uint32_t rows = (uint32_t)((slots + cols - 1) / cols);
		uint32_t maxCount = 0;
		for(const SheetCell& cell : cells) {
			if(cell.Count > maxCount) {
				maxCount = cell.Count;
			}
		}
		std::string label = kind + " group " + LabelNumber(cols) + "x" + LabelNumber(rows) + ", " + LabelNumber(cells.size()) + " cells";
		if(poses > 0) {
			label += ", " + LabelNumber(poses) + (poses == 1 ? " pose" : " poses");
		}
		label += ", x" + LabelNumber(maxCount);
		return label;
	}

	inline std::string InferPoseLabel(const PoseEntry& pose)
	{
		if(pose.Tiles.empty()) {
			return "";
		}
		std::string label = "figure " + LabelNumber(pose.Width) + "x" + LabelNumber(pose.Height)
			+ ", " + LabelNumber(pose.Tiles.size()) + (pose.Tiles.size() == 1 ? " tile" : " tiles")
			+ ", " + LabelNumber(pose.Frames) + (pose.Frames == 1 ? " frame" : " frames");
		if(!pose.FusionOf.empty()) {
			label += ", fusion";
		}
		if(pose.VariantOf >= 0) {
			char base[16];
			snprintf(base, sizeof(base), "pose%03u", (uint32_t)pose.VariantOf);
			label += ", variant of ";
			label += base;
		}
		return label;
	}

	inline std::string InferRunLabel(const PoseRun& run, const std::vector<PoseEntry>& poses, bool cyclic)
	{
		if(run.Poses.empty()) {
			return "";
		}
		uint32_t width = 0;
		uint32_t height = 0;
		for(uint32_t index : run.Poses) {
			if(index < poses.size()) {
				if(poses[index].Width > width) { width = poses[index].Width; }
				if(poses[index].Height > height) { height = poses[index].Height; }
			}
		}
		std::string label = std::string(cyclic ? "loop" : "sequence") + " of " + LabelNumber(run.Poses.size())
			+ (run.Poses.size() == 1 ? " phase" : " phases")
			+ ", " + LabelNumber(width) + "x" + LabelNumber(height)
			+ ", x" + LabelNumber(run.Repeats);
		if(run.Driver == 1 || run.Driver == 2) {
			label += ", driver port" + LabelNumber(run.Driver);
		}
		return label;
	}

	//The two fields as the sidecars write them, or nothing when there is no
	//label - a reader that sees `label` always sees `labelSource` beside it.
	inline std::string LabelFields(const std::string& label)
	{
		if(label.empty()) {
			return "";
		}
		return ", \"label\": \"" + label + "\", \"labelSource\": \"" + kLabelSourceInferred + "\"";
	}
}
