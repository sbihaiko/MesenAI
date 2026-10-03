#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"
#include <cmath>
#include <functional>

//"Estilizar o HUD do Core" (user's decision, 2026-10-03): the pure half of the
//Player-style system toast - the card W-P3/W-P9 draw over the game
//(docs/media/gui-redesign/, scripts/render_gui_wireframes.py's `hud()`):
//a rounded dark card in the bottom-right corner, a status glyph, and the
//message alone in white, with no "[title]" prefix and no black outline.
//Classic keeps SystemHud's original look; PreferencesConfig::ToastStyle picks.
//
//Everything here is geometry, colour and text rules - no DebugHud, no font
//table - so scripts/core_unit_tests.cpp asserts it without linking the HUD.
//SystemHud measures the text with DrawStringCommand and draws what this
//header lays out.
//
//Font: the Core keeps its 5x7 bitmap font. A TTF (Inter) would need a font
//rasteriser in the Core and a per-HUD-size glyph cache, for a toast that the
//HUD already draws at the HUD's own pixel scale; what is Inter-like here is
//the treatment (white, no outline, no title, sentence copy), not the glyphs.
namespace HudToastLayout
{
	//Pixel metrics, in HUD pixels. The render's card is 44 px tall with a
	//radius of 12 and its text starts 40 px in, after a 14 px glyph; these are
	//the same proportions rounded onto the Core's 7-row font.
	constexpr int PadLeft = 6;
	constexpr int IconSize = 7;
	constexpr int IconGap = 4;
	//DrawStringCommand's advance includes one blank column after each glyph,
	//so the visible right padding is one more than this.
	constexpr int PadRight = 7;
	constexpr int PadTop = 5;
	constexpr int PadBottom = 4;
	constexpr int Radius = 4;
	constexpr int MarginRight = 8;
	constexpr int MarginLeft = 8;
	constexpr int MarginBottom = 10;
	constexpr int StackGap = 4;
	//DrawStringCommand's line pitch; a line's glyphs occupy rows 0-7 of it
	//(row 7 is the descender of g/j/p/q/y).
	constexpr int LineHeight = 9;
	constexpr int GlyphRows = 8;

	//Colours, RGB only. The card is PlayerTheme.axaml's PlayerHudColor
	//(30,30,32 at alpha 225); the glyphs are the Player palette's Share green
	//(the check W-P3 draws), orange (W-P9's failure pill) and Play blue, the
	//Player accent, for every other toast.
	constexpr uint32_t CardRgb = 0x1E1E20;
	constexpr uint8_t CardAlpha = 225;
	constexpr uint32_t TextRgb = 0xFFFFFF;
	constexpr uint32_t CheckRgb = 0x34C759;
	constexpr uint32_t WarningRgb = 0xFF9F0A;
	constexpr uint32_t AccentRgb = 0x007AFF;

	enum class Icon
	{
		Dot,
		Check,
		Warning
	};

	//Which glyph a toast gets. Title and message arrive already localised
	//(MessageManager::DisplayMessage), so the rule reads the English
	//resources: the "Error" title and the failure openings are warnings, a
	//pack that was applied ("Applied %1", MepPackApplied) is a success, and
	//everything else is neutral information.
	inline bool StartsWith(const string& text, const char* prefix)
	{
		return text.rfind(prefix, 0) == 0;
	}

	inline Icon Classify(const string& title, const string& message)
	{
		if(title == "Error" || StartsWith(message, "Could not") || StartsWith(message, "Couldn't") ||
			StartsWith(message, "Couldn\xE2\x80\x99t") || StartsWith(message, "Failed")) {
			return Icon::Warning;
		}
		if(StartsWith(message, "Applied ")) {
			return Icon::Check;
		}
		return Icon::Dot;
	}

	//7x7 glyphs, one byte per row, bit 7 = leftmost column.
	inline const uint8_t* IconRows(Icon icon)
	{
		static constexpr uint8_t check[IconSize] = { 0x00, 0x02, 0x06, 0x8C, 0xD8, 0x70, 0x20 };
		//The "!" is a hole in the triangle, so the card shows through it.
		static constexpr uint8_t warning[IconSize] = { 0x10, 0x38, 0x28, 0x6C, 0x7C, 0xEE, 0xFE };
		static constexpr uint8_t dot[IconSize] = { 0x00, 0x38, 0x7C, 0x7C, 0x7C, 0x38, 0x00 };
		switch(icon) {
			case Icon::Check: return check;
			case Icon::Warning: return warning;
			default: return dot;
		}
	}

	inline uint32_t IconRgb(Icon icon)
	{
		switch(icon) {
			case Icon::Check: return CheckRgb;
			case Icon::Warning: return WarningRgb;
			default: return AccentRgb;
		}
	}

	//DebugHud colours carry *transparency* in the top byte (0 = opaque,
	//DrawRectangleCommand/DrawStringCommand invert it). `alpha` is the
	//colour's own opacity and `fade` the toast's fade-in/out (0-255).
	inline uint32_t HudColor(uint32_t rgb, uint8_t alpha, uint8_t fade)
	{
		uint32_t effective = (uint32_t)alpha * fade / 255;
		return ((255 - effective) << 24) | (rgb & 0xFFFFFF);
	}

	//The bitmap font has no glyphs for typographic punctuation: a UTF-8
	//sequence it cannot find is dropped, so "Applied X — textures" would draw
	//as "Applied X  textures". The Player copy uses dashes, middle dots,
	//ellipses and curly quotes (W-P3, ADR-0244, ADR-0251), so they are mapped
	//onto the nearest ASCII the font has.
	inline string ToFontText(const string& text)
	{
		static const std::pair<const char*, const char*> map[] = {
			{ "\xE2\x80\x94", "-" },    //em dash
			{ "\xE2\x80\x93", "-" },    //en dash
			{ "\xC2\xB7", "-" },        //middle dot
			{ "\xE2\x80\xA2", "-" },    //bullet
			{ "\xE2\x80\xA6", "..." },  //ellipsis
			{ "\xE2\x80\x98", "'" },
			{ "\xE2\x80\x99", "'" },
			{ "\xE2\x80\x9C", "\"" },
			{ "\xE2\x80\x9D", "\"" },
		};
		string out;
		out.reserve(text.size());
		for(size_t i = 0; i < text.size();) {
			bool replaced = false;
			for(auto& entry : map) {
				size_t len = strlen(entry.first);
				if(text.compare(i, len, entry.first) == 0) {
					out += entry.second;
					i += len;
					replaced = true;
					break;
				}
			}
			if(!replaced) {
				out += text[i++];
			}
		}
		return out;
	}

	//Word wrap at spaces (the classic draw wraps mid-word). A single word
	//wider than the line is left whole on its own line rather than split -
	//the card then grows past maxWidth, which is the lesser evil for a path.
	inline vector<string> Wrap(const string& text, int maxWidth, const std::function<int(const string&)>& measure)
	{
		vector<string> lines;
		string line;
		size_t start = 0;
		while(start <= text.size()) {
			size_t end = text.find(' ', start);
			string word = text.substr(start, end == string::npos ? string::npos : end - start);
			if(!word.empty()) {
				string candidate = line.empty() ? word : line + " " + word;
				if(!line.empty() && measure(candidate) > maxWidth) {
					lines.push_back(line);
					line = word;
				} else {
					line = candidate;
				}
			}
			if(end == string::npos) {
				break;
			}
			start = end + 1;
		}
		if(!line.empty() || lines.empty()) {
			lines.push_back(line);
		}
		return lines;
	}

	//The widest text a card can hold on a HUD this wide.
	inline int MaxTextWidth(int screenWidth)
	{
		int width = screenWidth - MarginLeft - MarginRight - PadLeft - IconSize - IconGap - PadRight;
		return width > 1 ? width : 1;
	}

	struct Box
	{
		int X = 0;
		int Y = 0;
		int Width = 0;
		int Height = 0;
		int IconX = 0;
		int IconY = 0;
		int TextX = 0;
		int TextY = 0;
	};

	//One card, anchored bottom-right. `stackOffset` is how many pixels the
	//newer cards below it already use; the caller adds Height + StackGap
	//after each card so the newest toast sits lowest, as in Classic.
	inline Box Layout(int screenWidth, int screenHeight, int textWidth, int lineCount, int stackOffset)
	{
		if(lineCount < 1) {
			lineCount = 1;
		}
		Box box;
		box.Width = PadLeft + IconSize + IconGap + textWidth + PadRight;
		box.Height = PadTop + (lineCount - 1) * LineHeight + GlyphRows + PadBottom;
		box.X = screenWidth - MarginRight - box.Width;
		if(box.X < MarginLeft) {
			box.X = MarginLeft;
		}
		box.Y = screenHeight - MarginBottom - stackOffset - box.Height;
		box.TextX = box.X + PadLeft + IconSize + IconGap;
		box.TextY = box.Y + PadTop;
		box.IconX = box.X + PadLeft;
		//The glyph sits on the first line's cap height (rows 0-6).
		box.IconY = box.TextY;
		return box;
	}

	//How many pixels row `row` of a `height`-tall card is cut in on each
	//side, for a corner of `radius`: the quarter circle sampled at the
	//pixel's centre. Rows outside both corners are 0.
	inline int CornerInset(int row, int height, int radius)
	{
		if(radius <= 0 || height <= 0) {
			return 0;
		}
		if(radius * 2 > height) {
			radius = height / 2;
		}
		int fromEdge = row < height - row - 1 ? row : height - row - 1;
		if(fromEdge >= radius) {
			return 0;
		}
		double dy = radius - fromEdge - 0.5;
		double dx = std::sqrt((double)radius * radius - dy * dy);
		return radius - (int)std::floor(dx + 0.5);
	}
}
