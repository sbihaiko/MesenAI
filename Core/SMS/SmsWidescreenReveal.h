#pragma once
#include "pch.h"
#include "Shared/SettingTypes.h"

//ADR-0253 slice W.2, the Game Gear half. The Game Gear's VDP is the Master
//System's: it renders a 256-px line, and the console's 160x144 picture is that
//line with SmsConfig.GameGearOverscan (48 px on each side) cropped away. The 96
//px the crop hides are real map columns the game already draws, so the Reveal
//is not new pixels - it is the crop being dropped while the switch is on.
//
//That is why a Game Gear frame keeps Width = 256 and only sets
//RenderedFrame::ExtendedColumns: the frame-width contract's
//Width - 2 * ExtendedColumns is the standard 160-px picture, and the extra
//columns are the ones hidden inside the VDP's own line. The NES and GB widen
//the frame because their pictures are the whole buffer; the Game Gear does not
//need to.
//
//The crop is a player setting with three presets, so it is not always 48: the
//Reveal is the crop being dropped, which means it reveals exactly what that
//crop hides and does nothing at all when there is no crop to drop.
//
//The Master System is not revealed: its picture already is the whole 256-px
//line (ADR-0253 §2, "SMS/SG-1000: Reveal is offered but has no map columns to
//show").
namespace SmsWidescreenReveal
{
	constexpr uint32_t LineWidth = 256;
	constexpr uint32_t Height = 144;
	//The shipped "Game Gear" overscan preset (UI/Config/SmsConfig.cs), which is
	//what the Reveal reveals on a stock configuration: 48 px on each side. The
	//player may configure a narrower crop, and then the Reveal follows it.
	constexpr uint32_t ExtraColumns = 48;
	constexpr uint32_t StandardPictureWidth = LineWidth - 2 * ExtraColumns; //160

	//How many columns each side of a Game Gear frame the Reveal adds - 0 when
	//there is no Reveal at all. The switch is WideScrn, per ADR-0253 §1, on a
	//console whose picture is narrower than the line its VDP renders.
	//
	//The count is the configured horizontal crop, because dropping that crop is
	//the whole Reveal: a "Full Frame" Game Gear (0 on both sides, a preset the
	//player can pick) already shows the entire line, so it has nothing to
	//reveal, and a crop that differs per side is not something the frame-width
	//contract can describe - it carries one column count for both sides.
	inline uint32_t RevealedColumns(bool isGameGear, VideoAspectRatio setting, uint32_t overscanLeft, uint32_t overscanRight)
	{
		if(!isGameGear || setting != VideoAspectRatio::Widescreen || overscanLeft != overscanRight) {
			return 0;
		}
		return overscanLeft;
	}

	//The horizontal overscan a frame is shown with: a revealing frame has no
	//side crop left, a standard one keeps whatever the user configured.
	inline uint32_t HorizontalOverscan(uint32_t configured, bool revealing)
	{
		return revealing ? 0 : configured;
	}

	//The overscan a Game Gear frame is shown with, from the player's configured
	//crop and the switch alone - EmuSettings::GetOverscan's whole decision for
	//the console, and the one place the crop is dropped.
	//
	//It deliberately does not ask the VideoDecoder about the frame it is
	//holding. A filter or the border layer that cannot take the wide frame
	//(BaseVideoFilter::AcceptsExtendedFrame) makes the decoder keep the standard
	//160-px centre, and that centre is exactly what the crop itself produces:
	//applying the crop again would cut into the picture and read past the end of
	//the row. The Reveal is the crop being dropped, so both are decided here by
	//RevealedColumns - the same call SmsVdp makes when it stamps the frame's
	//ExtendedColumns - and the wide frame and the standard centre both come out
	//with no crop.
	inline OverscanDimensions GameGearOverscan(OverscanDimensions configured, VideoAspectRatio setting)
	{
		bool revealing = RevealedColumns(true, setting, configured.Left, configured.Right) > 0;
		configured.Left = HorizontalOverscan(configured.Left, revealing);
		configured.Right = HorizontalOverscan(configured.Right, revealing);
		return configured;
	}
}
