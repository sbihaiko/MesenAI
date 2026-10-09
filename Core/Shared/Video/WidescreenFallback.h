#pragma once
#include <cstdint>
#include <cstring>
#include <vector>

//ADR-0253 §3 (PRD slice W.3): the content-aware fallback chain. While
//widescreen is active, a side column the console cannot fill with real content
//on a given frame is drawn by the first source that applies:
//
//  1. the pack's widescreen art - the `<widescreen>` section (MEP-v1 §5.5);
//  2. the pack's border layer (ADR-0149);
//  3. black.
//
//The border and black are per-frame fill-ins: on their own they never make a
//game "supported", so they never keep the WideScrn switch enabled (§3, last
//paragraph). Only a console that can reveal real content beside the picture,
//or a pack that ships widescreen art, does.
//
//Host-free and header-only: `scripts/core_unit_tests.cpp` drives the policy
//directly, and VideoRenderer owns the decoded assets and calls in here for the
//decision. Nothing here reads VRAM, settings or the filesystem.
namespace WidescreenFallback
{
	enum class Source : uint8_t
	{
		Reveal = 0,
		PackArt = 1,
		Border = 2,
		Black = 3
	};

	//What is available to fill one side of one row.
	struct Inputs
	{
		//The console's own background map had real content there (§2/§3)
		bool RevealFilled = false;
		//The pack ships a `<widescreen>` image for this side (MEP-v1 §5.5)
		bool PackArtAvailable = false;
		//The pack ships a border layer (ADR-0149)
		bool BorderAvailable = false;
	};

	//The first source that applies, in ADR-0253 §3's order.
	inline Source Resolve(const Inputs& in)
	{
		if(in.RevealFilled) {
			return Source::Reveal;
		}
		if(in.PackArtAvailable) {
			return Source::PackArt;
		}
		if(in.BorderAvailable) {
			return Source::Border;
		}
		return Source::Black;
	}

	//ADR-0253 §3: the border and black alone never make a game supported.
	inline bool SupportsWidescreen(bool revealHasContent, bool packArtAvailable)
	{
		return revealHasContent || packArtAvailable;
	}

	//ADR-0253 §3, the render thread's chain in one host-free place. The order is
	//the contract, so it lives here rather than in two adjacent lines of
	//VideoRenderer::UpdateFrame, where a swap would only show up on screen.
	//
	//`artStage` is the first link: it fills the side rows Reveal left empty and
	//marks them. `borderStage` then runs on the frame that resulted - it fills
	//whatever the map still has empty (the border link), and returns the picture
	//the caller hands to the screen. Inverted, the art would be drawn into a
	//frame the composite has already replaced, so the sides would show the
	//backdrop or black instead of the pack's art.
	//
	//`frameHasSidesToFill` is false for a standard frame: there is no side run to
	//draw on, so the art stage is skipped. Black needs no stage of its own - a
	//row with no reveal, no art and no border is simply a row of the extended
	//frame the console has already drawn black (W.1).
	template<typename ArtStage, typename BorderStage>
	inline auto ApplyChain(bool frameHasSidesToFill, ArtStage&& artStage, BorderStage&& borderStage)
	{
		if(frameHasSidesToFill) {
			artStage();
		}
		return borderStage();
	}

	//The per-row fill map an extended frame carries: one byte per row, bit 0 =
	//the left side was filled by the game, bit 1 = the right side.
	constexpr uint8_t LeftBit = 0x01;
	constexpr uint8_t RightBit = 0x02;

	inline uint8_t SideBit(bool left) { return left ? LeftBit : RightBit; }

	//Fills one side's run of an extended frame with the pack's `<widescreen>`
	//art, row by row, for every row the game did not fill, and marks those rows
	//as filled so the border composite below leaves them alone.
	//
	//The per-row decision goes through Resolve, so §3's precedence - the
	//console's own content first, the art only where there is none - is spelled
	//out in exactly one place. This function *is* the art link, so the art is
	//available by definition and the border is not a candidate yet: the
	//composite is the next link and only sees the frame afterwards.
	//
	//The image is drawn 1:1: MEP-v1 §5.5 requires a side image to be exactly
	//`extendedColumns` px wide and `frameHeight` px tall, so no scaling is
	//involved and a mismatch is refused (a pack that ships the wrong size gets
	//the next source in the chain instead of a stretched or tiled side).
	//`frame` is the extended frame (ARGB, `frameWidth` px per row); `sideFill`
	//is its per-row map, updated in place.
	inline void FillSideFromArt(uint32_t* frame, uint32_t frameWidth, uint32_t frameHeight, uint32_t extendedColumns,
		bool left, const uint32_t* art, uint32_t artWidth, uint32_t artHeight, uint8_t* sideFill)
	{
		if(!frame || !art || !sideFill || extendedColumns == 0 || frameWidth < 2 * extendedColumns) {
			return;
		}
		if(artWidth != extendedColumns || artHeight != frameHeight) {
			return;
		}
		uint32_t x0 = left ? 0 : (frameWidth - extendedColumns);
		uint8_t bit = SideBit(left);
		for(uint32_t y = 0; y < frameHeight; y++) {
			Inputs in;
			in.RevealFilled = (sideFill[y] & bit) != 0;
			in.PackArtAvailable = true;
			if(Resolve(in) != Source::PackArt) {
				continue;
			}
			memcpy(frame + (size_t)y * frameWidth + x0, art + (size_t)y * artWidth, (size_t)artWidth * sizeof(uint32_t));
			sideFill[y] |= bit;
		}
	}

	//The art's canvas is fixed per console (MEP-v1 §5.5: 64 x 240 on the NES - the
	//Reveal's own extra columns by the frame height) because that is the picture a
	//pack author draws beside. An HD pack draws that same Reveal frame at the
	//pack's own scale, so its frame's side runs are the art's canvas times a whole
	//number, and the 1:1 copy above - which refuses any other size - would leave
	//the sides to the border on a pack that conforms everywhere else.
	//
	//This is where that is reconciled, and only here: the art is scaled up to the
	//frame's own side run, nearest-neighbor by the integer factor the two have in
	//common (the pack's scale). The copy that follows is still 1:1 against the
	//scaled art - no stretching, no cropping, no interpolation - and a frame that
	//is not a whole-number multiple of the art on *both* axes is not this art's
	//frame: nothing is scaled and the 1:1 rule refuses it as before.
	//
	//False, with `out` untouched, whenever the two canvases do not relate that way.
	inline bool ScaleSideArt(const uint32_t* art, uint32_t artWidth, uint32_t artHeight,
		uint32_t targetWidth, uint32_t targetHeight, std::vector<uint32_t>& out)
	{
		if(!art || artWidth == 0 || artHeight == 0 || targetWidth == 0 || targetHeight == 0) {
			return false;
		}
		if(targetWidth % artWidth != 0 || targetHeight % artHeight != 0) {
			return false;
		}
		uint32_t scale = targetWidth / artWidth;
		if(targetHeight / artHeight != scale) {
			return false;
		}
		out.resize((size_t)targetWidth * targetHeight);
		for(uint32_t y = 0; y < targetHeight; y++) {
			const uint32_t* src = art + (size_t)(y / scale) * artWidth;
			uint32_t* dst = out.data() + (size_t)y * targetWidth;
			for(uint32_t x = 0; x < targetWidth; x++) {
				dst[x] = src[x / scale];
			}
		}
		return true;
	}

	//The art link of §3's chain for one side of one frame: exactly
	//FillSideFromArt, plus the one thing an HD frame needs - the pack's art scaled
	//to that frame first (ScaleSideArt). Art already on the frame's own canvas is
	//handed over as it is, which is every non-HD frame and costs nothing there.
	//
	//`scratch` holds the scaled copy and is the caller's: one buffer is enough for
	//both sides, since each call copies out of it before returning, and owning it
	//here would mean allocating per frame.
	inline void FillSideFromArtForFrame(uint32_t* frame, uint32_t frameWidth, uint32_t frameHeight, uint32_t extendedColumns,
		bool left, const uint32_t* art, uint32_t artWidth, uint32_t artHeight, uint8_t* sideFill, std::vector<uint32_t>& scratch)
	{
		if(art && (artWidth != extendedColumns || artHeight != frameHeight)
			&& ScaleSideArt(art, artWidth, artHeight, extendedColumns, frameHeight, scratch)) {
			art = scratch.data();
			artWidth = extendedColumns;
			artHeight = frameHeight;
		}
		FillSideFromArt(frame, frameWidth, frameHeight, extendedColumns, left, art, artWidth, artHeight, sideFill);
	}
}
