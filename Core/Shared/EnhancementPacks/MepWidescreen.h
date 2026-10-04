#pragma once
#include "pch.h"

//ADR-0253 §3 (PRD slice W.3): the `<widescreen>` section of an enhancement
//pack (MEP-v1 §5.5). A pack whose game has no real content beside the 256-px
//picture - on NES, a single-screen or horizontally mirrored game - may ship
//art for the extra columns instead. Each side image is drawn 1:1 into that
//side's run, so its size is decided by the console: exactly the console's
//extra-columns count wide and its frame height tall (64x240 on NES, W.1).
//
//The section lives at `widescreen/widescreen.json` (or the pack's
//`sections.widescreen.path`), and the manifest names the images:
//
//  {
//    "version": 1,
//    "left":  "left.png",
//    "right": "right.png",
//    "screens": [
//      { "id": 3, "left": "screens/3-left.png", "right": "screens/3-right.png" }
//    ]
//  }
//
//The default pair covers every frame; a `screens[]` entry overrides one side
//for one of the pack's `<background>` screens (the id the recorded art uses).
//
//Pure and I/O-free (ADR-0127 spirit): MepWidescreen parses and resolves, and
//never opens the images - the caller decodes them. Deliberately host-free so
//`make core-unit-tests` links it without the Emulator.

struct MepWidescreenScreen
{
	//The pack's `<background>` screen id this override belongs to
	int32_t Id = 0;
	//Relative to the section folder, '/' separators, no leading "./"; empty
	//when this screen does not override that side
	string Left;
	string Right;
};

class MepWidescreen
{
public:
	//The screen id the default pair answers to; no pack screen uses it
	static constexpr int32_t DefaultScreenId = -1;

	//Default pair, used for any screen without an override (MEP-v1 §5.5)
	string Left;
	string Right;
	//Per-screen overrides, in manifest order, ids unique
	vector<MepWidescreenScreen> Screens;

	//Parses/validates `widescreen.json` text. Returns false with a
	//human-readable reason when a MUST rule of MEP-v1 §5.5 is violated or the
	//JSON is malformed. Unknown fields are ignored (MEP-v1 §3.2).
	static bool Parse(const string& json, MepWidescreen& out, string& error);

	//True when the manifest names at least one image on either side
	bool HasAnyArt() const { return !Left.empty() || !Right.empty() || !Screens.empty(); }

	//The override for `screenId`, or nullptr when that screen uses the default
	//pair. DefaultScreenId always returns nullptr.
	const MepWidescreenScreen* FindScreen(int32_t screenId) const;

	//The image for one side of one screen: the screen's override when it has
	//one for that side, else the default pair's. Empty when neither does.
	string GetSidePath(int32_t screenId, bool left) const;

	//True when a path is a safe, section-relative PNG reference (the shape
	//Parse enforces on every image field).
	static bool IsValidImagePath(const string& path);
};
