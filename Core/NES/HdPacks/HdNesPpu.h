#pragma once
#include <type_traits>
#include "pch.h"
#include "NES/NesPpu.h"
#include "NES/NesConsole.h"
#include "NES/NesWidescreenPpu.h"
#include "NES/BaseMapper.h"
#include "NES/HdPacks/HdData.h"
#include "NES/HdPacks/HdWidescreenColumns.h"
#include "Shared/EmuSettings.h"
#include "Shared/RenderedFrame.h"

struct NesSpriteInfoEx
{
	uint32_t AbsoluteTileAddr;
	uint16_t TileAddr;
	bool HorizontalMirror;
	bool VerticalMirror;
	uint8_t OffsetY;
	uint8_t LowByte;
	uint8_t HighByte;
};

struct NesTileInfoEx
{
	uint32_t AbsoluteTileAddr;
	uint16_t TileAddr;
	uint8_t OffsetY;
};

class HdNesPpu final : public NesPpu<HdNesPpu>
{
	HdScreenInfo* _screenInfo[2] = {};
	HdScreenInfo* _info = nullptr;
	uint32_t _version = 0;
	bool _isChrRam = false;
	bool _forceRemoveSpriteLimit = false;
	HdPackData* _hdData = nullptr;
	NesSpriteInfoEx _exSpriteInfo[64] = {};
	NesTileInfoEx _previousTileEx = {};
	NesTileInfoEx _currentTileEx = {};
	NesTileInfoEx _nextTileEx = {};

	//ADR-0253 slices W.1/W.3/W.4/W.5: the widened frame itself, the per-game
	//support measurement and the frame publication - the same state the default
	//PPU keeps (NesWidescreenPpu.h), latched at the same point, so both paths
	//answer alike. The HD renderer draws the sides at the pack's scale from
	//HdScreenInfo::SideTiles; this buffer is what every other consumer of the
	//frame sees (and what the border layer crops the centre out of).
	NesWidescreenPpu::State _widescreen;

public:
	HdNesPpu(NesConsole* console, HdPackData* hdData);
	virtual ~HdNesPpu();

	void* OnBeforeSendFrame();

	//ADR-0253: the extra columns of `row`, read from the basis NesPpu has just
	//captured for it and stored as HdSideTile's for the HD renderer, which draws
	//them through the pack's own per-pixel pipeline at the pack's scale. Same
	//capture point as DefaultNesPpu::OnRowBasisCaptured, same side-effect-free
	//reads, and nothing at all when the Reveal is off - the standard frame and
	//its ScreenTiles are untouched either way.
	void OnRowBasisCaptured(int16_t row);

	//ADR-0253: an extended frame replaces the standard one on its way to the video
	//decoder; _currentOutputBuffer stays the 256-px picture, and the pack's
	//ScreenTiles keep the picture's own coordinates. The per-row fill map travels
	//with it (NesWidescreenPpu::State::PublishFrame, §3/W.3), so the renderer's
	//fallback chain knows which side columns still need the pack's art, the
	//border layer or black - without it the chain never runs with a pack loaded.
	void OnFrameBuilt(RenderedFrame& frame)
	{
		_widescreen.PublishFrame(frame, _currentOutputBuffer);
	}

	//ADR-0253 §4 (W.5): what the core measured for the running game - the HD
	//path measures it exactly as the default path does.
	NesWidescreenSupport::Verdict GetWidescreenSupportVerdict() const override { return _widescreen.GetVerdict(); }

	//ADR-0253 §3: whether the pack loaded for this ROM ships widescreen art -
	//the same question DefaultNesPpu asks, so a game the measurement settled as
	//unsupported is widened on this path too when the art exists.
	bool PackHasWidescreenArt();

	__forceinline bool RemoveSpriteLimit() { return _forceRemoveSpriteLimit || _console->GetNesConfig().RemoveSpriteLimit; }
	__forceinline bool UseAdaptiveSpriteLimit() { return _forceRemoveSpriteLimit || _console->GetNesConfig().AdaptiveSpriteLimit; }

	__forceinline void StoreSpriteInformation(bool horizontalMirror, bool verticalMirror, uint16_t tileAddr, uint8_t lineOffset, NesSpriteInfo& sprite)
	{
		NesSpriteInfoEx& info = _exSpriteInfo[_spriteIndex];
		info.TileAddr = tileAddr;
		info.AbsoluteTileAddr = _mapper->GetPpuAbsoluteAddress(info.TileAddr).Address;
		info.HorizontalMirror = horizontalMirror;
		info.VerticalMirror = verticalMirror;
		info.OffsetY = lineOffset;
		info.LowByte = sprite.LowByte;
		info.HighByte = sprite.HighByte;
	}

	__forceinline void PushTileInformation()
	{
		_previousTileEx = _currentTileEx;
		_currentTileEx = _nextTileEx;
	}

	__forceinline void StoreTileInformation()
	{
		uint8_t tileIndex = _mapper->DebugReadVram(GetNameTableAddr());
		uint16_t tileAddr = (tileIndex << 4) | (_videoRamAddr >> 12) | _control.BackgroundPatternAddr;

		_nextTileEx.OffsetY = _videoRamAddr >> 12;
		_nextTileEx.AbsoluteTileAddr = _mapper->GetPpuAbsoluteAddress(tileAddr).Address;
	}

	__forceinline void ProcessScanline()
	{
		ProcessScanlineImpl();
	}

	void DrawPixel()
	{
		uint16_t bufferOffset = (_scanline << 8) + _cycle - 1;
		uint16_t& pixel = _currentOutputBuffer[bufferOffset];
		_lastSprite = nullptr;

		if(IsRenderingEnabled() || ((_videoRamAddr & 0x3F00) != 0x3F00)) {
			uint32_t color = GetPixelColor();
			pixel = (_paletteRam[color & 0x03 ? color : 0] & _paletteRamMask) | _intensifyColorBits;

			bool usePrev = (_xScroll + ((_cycle - 1) & 0x07) < 8);
			uint8_t tilePalette = usePrev ? _previousTilePalette : _currentTilePalette;
			NesTileInfoEx& lastTileEx = usePrev ? _previousTileEx : _currentTileEx;
			uint32_t backgroundColor = 0;
			if(_mask.BackgroundEnabled && _cycle > _minimumDrawBgCycle) {
				backgroundColor = (((_lowBitShift << _xScroll) & 0x8000) >> 15) | (((_highBitShift << _xScroll) & 0x8000) >> 14);
			}

			HdPpuPixelInfo& tileInfo = _info->ScreenTiles[bufferOffset];

			tileInfo.Grayscale = _paletteRamMask == 0x30;
			tileInfo.EmphasisBits = _intensifyColorBits >> 6;
			tileInfo.Tile.PpuBackgroundColor = ReadPaletteRam(0);
			tileInfo.Tile.BgColorIndex = backgroundColor;
			tileInfo.Tile.PaletteOffset = tileInfo.Tile.PaletteOffset;
			if(backgroundColor == 0) {
				tileInfo.Tile.BgColor = tileInfo.Tile.PpuBackgroundColor;
			} else {
				tileInfo.Tile.BgColor = ReadPaletteRam(tilePalette + backgroundColor);
			}

			tileInfo.XScroll = _xScroll;
			tileInfo.TmpVideoRamAddr = _tmpVideoRamAddr;

			if(_lastSprite && _mask.SpritesEnabled) {
				int j = 0;
				for(uint8_t i = 0; i < _spriteCount; i++) {
					int32_t shift = (int32_t)_cycle - _spriteTiles[i].SpriteX - 1;
					NesSpriteInfo& sprite = _spriteTiles[i];
					NesSpriteInfoEx& spriteEx = _exSpriteInfo[i];
					if(shift >= 0 && shift < 8) {
						tileInfo.Sprite[j].TileIndex = spriteEx.AbsoluteTileAddr / 16;
						if(_isChrRam) {
							_console->GetMapper()->CopyChrTile(spriteEx.AbsoluteTileAddr & 0xFFFFFFF0, tileInfo.Sprite[j].TileData);
						}
						if(_version >= 100) {
							tileInfo.Sprite[j].PaletteColors = 0xFF000000 | _paletteRam[sprite.PaletteOffset + 3] | (_paletteRam[sprite.PaletteOffset + 2] << 8) | (_paletteRam[sprite.PaletteOffset + 1] << 16);
						} else {
							tileInfo.Sprite[j].PaletteColors = _paletteRam[sprite.PaletteOffset + 3] | (_paletteRam[sprite.PaletteOffset + 2] << 8) | (_paletteRam[sprite.PaletteOffset + 1] << 16);
						}
						if(spriteEx.OffsetY >= 8) {
							tileInfo.Sprite[j].OffsetY = spriteEx.OffsetY - 8;
						} else {
							tileInfo.Sprite[j].OffsetY = spriteEx.OffsetY;
						}

						tileInfo.Sprite[j].OffsetX = shift;
						tileInfo.Sprite[j].HorizontalMirroring = spriteEx.HorizontalMirror;
						tileInfo.Sprite[j].VerticalMirroring = spriteEx.VerticalMirror;
						tileInfo.Sprite[j].BackgroundPriority = sprite.BackgroundPriority;
						tileInfo.Sprite[j].PaletteOffset = sprite.PaletteOffset;

						tileInfo.Sprite[j].SpriteColorIndex = ((spriteEx.LowByte << shift) & 0x80) >> 7 | ((spriteEx.HighByte << shift) & 0x80) >> 6;

						if(tileInfo.Sprite[j].SpriteColorIndex == 0) {
							tileInfo.Sprite[j].SpriteColor = ReadPaletteRam(0);
						} else {
							tileInfo.Sprite[j].SpriteColor = ReadPaletteRam(sprite.PaletteOffset + tileInfo.Sprite[j].SpriteColorIndex);
						}

						tileInfo.Sprite[j].PpuBackgroundColor = tileInfo.Tile.PpuBackgroundColor;
						tileInfo.Sprite[j].BgColorIndex = tileInfo.Tile.BgColorIndex;

						j++;
						if(j >= 4) {
							break;
						}
					}
				}
				tileInfo.SpriteCount = j;
			} else {
				tileInfo.SpriteCount = 0;
			}

			if(_mask.BackgroundEnabled && _cycle > _minimumDrawBgCycle) {
				tileInfo.Tile.TileIndex = lastTileEx.AbsoluteTileAddr / 16;
				if(_isChrRam) {
					_console->GetMapper()->CopyChrTile(lastTileEx.AbsoluteTileAddr & 0xFFFFFFF0, tileInfo.Tile.TileData);
				}
				if(_version >= 100) {
					tileInfo.Tile.PaletteColors = _paletteRam[tilePalette + 3] | (_paletteRam[tilePalette + 2] << 8) | (_paletteRam[tilePalette + 1] << 16) | (_paletteRam[0] << 24);
				} else {
					tileInfo.Tile.PaletteColors = _paletteRam[tilePalette + 3] | (_paletteRam[tilePalette + 2] << 8) | (_paletteRam[tilePalette + 1] << 16);
				}
				tileInfo.Tile.OffsetY = lastTileEx.OffsetY;
				tileInfo.Tile.OffsetX = (_xScroll + ((_cycle - 1) & 0x07)) & 0x07;
			} else {
				tileInfo.Tile.TileIndex = HdPpuTileInfo::NoTile;
			}
		} else {
			//"If the current VRAM address points in the range $3F00-$3FFF during forced blanking, the color indicated by this palette location will be shown on screen instead of the backdrop color."
			pixel = ReadPaletteRam(_videoRamAddr) | _intensifyColorBits;
			_info->ScreenTiles[bufferOffset].Tile.TileIndex = HdPpuTileInfo::NoTile;
			_info->ScreenTiles[bufferOffset].SpriteCount = 0;
		}
	}
};

//ADR-0253, W.3/W.5 wiring guards. Both hooks and the verdict reader are
//non-virtual CRTP defaults in BaseNesPpu (except the verdict, a plain virtual
//that defaults to Undecided), so a declaration dropped from this class does
//NOT fail to compile - the call silently lands on the base and the HD path
//quietly stops publishing an extended frame or measuring the game, which is
//exactly the W.3/W.5 half-port this guards against. Each assert pins the
//member to HdNesPpu itself by its exact type, so an inherited member resolves
//to BaseNesPpu's type and the build stops here.
//
//The host-free suite cannot carry this: including this header drags in
//NesPpu.h, whose __forceinline members (GetNameTableAddr, ProcessScanlineImpl,
//GetPixelColor) are defined in NesPpu.cpp and trip -Wundefined-inline under
//-Werror. `make core` compiles this header everywhere, CI included.
static_assert(std::is_same<decltype(&HdNesPpu::GetWidescreenSupportVerdict), NesWidescreenSupport::Verdict (HdNesPpu::*)() const>::value,
	"ADR-0253 W.5: HdNesPpu must answer the support verdict itself - inherited, the HD path always reads Undecided");
static_assert(std::is_same<decltype(&HdNesPpu::OnFrameBuilt), void (HdNesPpu::*)(RenderedFrame&)>::value,
	"ADR-0253 W.3: HdNesPpu must publish the built frame itself - inherited, the frame never carries its side-fill map");
static_assert(std::is_same<decltype(&HdNesPpu::OnRowBasisCaptured), void (HdNesPpu::*)(int16_t)>::value,
	"ADR-0253 W.3/W.4: HdNesPpu must capture the row basis itself - inherited, the side tiles are never captured");