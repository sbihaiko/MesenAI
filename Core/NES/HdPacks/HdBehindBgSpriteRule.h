#pragma once
#include "pch.h"
#include "NES/HdPacks/HdData.h"

//ADR-0224 (F12.15). A pack that carries the tag line below asks HdNesPack to
//keep a behind-background sprite visible where the ROM's own background pixel
//is colour 0, even when a layer-2 (priority 20-29) <background> covers that
//pixel - the hardware's rule, which a recorded screen (ADR-0050, rebuilt from
//background tiles alone) otherwise paints over. It is a tag rather than an
//<options> token because HdPackLoader::ProcessOptionTag counts an unknown
//token as a load error while an unknown tag is skipped in silence, so a pack
//recorded here still opens in Mesen 2 and HD Mesen, without the effect.
//Host-free (ADR-0127): the predicate and the two format helpers are what
//scripts/core_unit_tests.cpp covers; the pixel loop that applies them lives in
//HdNesPack::GetPixels.
namespace HdBehindBgSpriteRule
{
	constexpr const char* Tag = "<bgPreservesBehindBgSprites>";

	//True for the tag line exactly as the loader sees it after its own
	//trimming (CR/LF stripped, no leading condition). Nothing follows the tag:
	//it takes no arguments.
	inline bool IsTagLine(const string& lineContent)
	{
		return lineContent == Tag;
	}

	//The renderer's decision for one pixel, taken after the layer-2 pass:
	//re-apply the behind-background sprite pass when the pack opted in, an
	//opaque behind-background sprite drew here (lowestBgSprite is 999 until
	//one with SpriteColorIndex != 0 is seen - the right test, since a
	//transparent sprite pixel must not block the background), the ROM's
	//background pixel is colour 0 (never the HD tile's alpha), and a layer-2
	//background actually changed the pixel. Without the opt-in the answer is
	//always false, which is what keeps every other pack byte-identical.
	inline bool KeepsBehindBgSprite(bool packOptedIn, int lowestBgSprite, uint8_t bgColorIndex, bool layer2Painted)
	{
		return packOptedIn && lowestBgSprite != 999 && bgColorIndex == 0 && layer2Painted;
	}

	//The tail of the hires.txt header the recorder writes: the <options> line
	//(only when some flag is set - see HdPackOptionsToString for why it has no
	//trailing comma) and then the opt-in tag, unconditionally, since a recorded
	//<background> is the case the tag exists for (ADR-0224 3).
	inline void WriteHeaderTail(std::ostream& out, uint32_t optionFlags)
	{
		if(optionFlags != 0) {
			out << "<options>" << HdPackOptionsToString(optionFlags) << std::endl;
		}
		out << Tag << std::endl;
	}
}
