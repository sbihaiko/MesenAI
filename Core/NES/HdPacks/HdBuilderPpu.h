#pragma once
#include "pch.h"
#include "NES/HdPacks/HdNesPpu.h"
#include "NES/HdPacks/HdNesPack.h"
#include "NES/HdPacks/HdPackBuilder.h"
#include "NES/HdPacks/OamFetchLatch.h"
#include "NES/HdPacks/ChrBankHashes.h"
#include "Shared/Video/VideoDecoder.h"
#include "Shared/RewindManager.h"
#include "Utilities/BitUtilities.h"
#include "Utilities/Serializer.h"

class HdBuilderPpu final : public NesPpu<HdBuilderPpu>
{
private:
	HdPackBuilder* _hdPackBuilder = nullptr;
	//#450: the frame's sprite halves, each decoded when the PPU fetched it
	//(see OnBeforeSendFrame and OamFetchLatch).
	OamFetchLatch _oamLatch;
	//PR #468 review: whether the row being drawn showed sprites at any pixel,
	//and the sprite palettes it drew with, handed to _oamLatch at cycle 257.
	bool _spriteRowShown = false;
	uint32_t _spriteRowPalettes[4] = {};
	uint32_t _chrRamIndexMask = 0;
	//ADR-0232: the CHR RAM bank id of each recorded tile, rehashed only when a
	//tile is about to be recorded after a CHR write or a state load.
	MesenSheets::ChrBankHashes _bankHashes;

	NesSpriteInfoEx _exSpriteInfo[64] = {};
	NesTileInfoEx _previousTileEx = {};
	NesTileInfoEx _currentTileEx = {};
	NesTileInfoEx _nextTileEx = {};

public:
	__forceinline bool RemoveSpriteLimit() { return _console->GetNesConfig().RemoveSpriteLimit; }
	__forceinline bool UseAdaptiveSpriteLimit() { return _console->GetNesConfig().AdaptiveSpriteLimit; }

	//F9.5 (ADR-0153 §2): one OAM snapshot per frame for the sprite sheets. Read
	//from OAM rather than from DrawPixel because the sheet wants the figure the
	//game *placed*, not the pixels that survived the 8-sprite limit and the
	//background priority bit. Handed over once a frame, before NesConsole
	//closes the frame on the builder; RecordSprite is a no-op unless screen
	//capture is on.
	//Each half is latched at its sprite fetch, not read here at frame end
	//(#450): a half is recorded only if one of its rows was fetched with
	//rendering on and then drawn with sprites showing, under the PPUCTRL and
	//CHR mapping of that fetch and the palette of that row. That keeps #183's rule - power-on, with OAM all
	//zero, palette RAM at its boot values and rendering still disabled,
	//records nothing - and adds the mid-frame case #183's per-pixel gate let
	//through: Castlevania switching PPUCTRL to 8x8 and turning rendering off
	//at line 0 of a screen cut, after which a frame-end decode named 8x8
	//halves the PPU never drew.
	void* OnBeforeSendFrame()
	{
		//#458: each half is named by the bank its topmost row was read from;
		//rows read from another bank (after an MMC2/MMC4 latch tile) give
		//their key a shape too, without placing the sprite a second time.
		BaseMapper* mapper = _console->GetMapper();
		_oamLatch.ForEachLatched(
			[this](uint8_t x, uint8_t y, HdPpuTileInfo& sprite) { _hdPackBuilder->RecordSprite(x, y, sprite); },
			[mapper](int32_t absoluteTileAddr, HdPpuTileInfo& sprite) {
				if(!sprite.IsChrRamTile) {
					sprite.TileIndex = absoluteTileAddr / 16;
				}
				mapper->CopyChrTile((uint32_t)absoluteTileAddr & 0xFFFFFFF0, sprite.TileData);
				ApplyFlips(sprite.TileData, sprite.HorizontalMirroring, sprite.VerticalMirroring);
			},
			[this](const HdPpuTileInfo& sprite) { _hdPackBuilder->RecordSpriteBank(sprite); },
			//#520: a blank half names no shape and reaches no sheet, but it is
			//a half the PPU placed and the pose pass reads the placement.
			[this](uint8_t x, uint8_t y) { _hdPackBuilder->RecordSpritePlacement(x, y); });
		_oamLatch.Clear();
		return nullptr;
	}

	__forceinline void StoreSpriteInformation(bool horizontalMirror, bool verticalMirror, uint16_t tileAddr, uint8_t lineOffset, NesSpriteInfo& sprite)
	{
		NesSpriteInfoEx& info = _exSpriteInfo[_spriteIndex];
		info.TileAddr = tileAddr;
		info.AbsoluteTileAddr = _mapper->GetPpuAbsoluteAddress(info.TileAddr).Address;
		//#458: the bank this row really came from, for OamFetchLatch to name
		//the half by (the <tile> rule is keyed by the same address).
		_oamLatch.OnRowFetch(_scanline, sprite.SpriteX, tileAddr, verticalMirror, info.AbsoluteTileAddr);
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
		_nextTileEx.TileAddr = tileAddr;
		_nextTileEx.AbsoluteTileAddr = _mapper->GetPpuAbsoluteAddress(tileAddr).Address;
	}

	__forceinline void ProcessScanline()
	{
		ProcessScanlineImpl();
		if(_cycle == 257 && _scanline >= 0) {
			LatchFetchedSprites();
		}
	}

	void DrawPixel()
	{
		if(!_spriteRowShown && _prevRenderingEnabled && _mask.SpritesEnabled) {
			_spriteRowShown = true;
			for(uint8_t i = 0; i < 4; i++) {
				uint8_t offset = 0x10 | (i << 2);
				_spriteRowPalettes[i] = ReadPaletteRam(offset + 3) | (ReadPaletteRam(offset + 2) << 8) | (ReadPaletteRam(offset + 1) << 16) | 0xFF000000;
			}
		}
		if(IsRenderingEnabled() || ((_videoRamAddr & 0x3F00) != 0x3F00)) {
			BaseMapper* mapper = _console->GetMapper();
			bool isChrRam = !mapper->HasChrRom();

			_lastSprite = nullptr;
			uint32_t color = GetPixelColor();
			_currentOutputBuffer[(_scanline << 8) + _cycle - 1] = _paletteRam[color & 0x03 ? color : 0];
			uint32_t backgroundColor = 0;
			if(_mask.BackgroundEnabled && _cycle > _minimumDrawBgCycle) {
				backgroundColor = (((_lowBitShift << _xScroll) & 0x8000) >> 15) | (((_highBitShift << _xScroll) & 0x8000) >> 14);
			}

			//A CHR ROM tile's bank is its CHR ROM bank number, which
			//HdPackBuilder derives from the address, so it never reads this.
			bool writePending = _ppuMemoryDataWriteStateMachine > 0;
			auto bankIdOf = [this, mapper, isChrRam, writePending](uint16_t tileAddr) -> uint32_t {
				return isChrRam ? _bankHashes.BankIdOf(tileAddr, [mapper](uint16_t a) { return mapper->DebugReadVram(a); }, writePending) : 0;
			};

			bool hasBgSprite = false;
			if(_lastSprite && _mask.SpritesEnabled) {
				uint8_t spriteIndex = (uint8_t)(_lastSprite - _spriteTiles);
				NesSpriteInfoEx& spriteInfoEx = _exSpriteInfo[spriteIndex];

				if(backgroundColor == 0) {
					for(uint8_t i = 0; i < _spriteCount; i++) {
						if(_spriteTiles[i].BackgroundPriority) {
							hasBgSprite = true;
							break;
						}
					}
				}

				//#479: row 0's sprites are pre-render leftovers no OAM entry
				//placed, so they make no rule (OamFetchLatch::SpriteRowIsPlaced).
				if(spriteInfoEx.AbsoluteTileAddr >= 0 && OamFetchLatch::SpriteRowIsPlaced(_scanline)) {
					HdPpuTileInfo sprite = {};
					sprite.TileIndex = (isChrRam ? (spriteInfoEx.TileAddr & _chrRamIndexMask) : spriteInfoEx.AbsoluteTileAddr) / 16;
					sprite.PaletteColors = ReadPaletteRam(_lastSprite->PaletteOffset + 3) | (ReadPaletteRam(_lastSprite->PaletteOffset + 2) << 8) | (ReadPaletteRam(_lastSprite->PaletteOffset + 1) << 16) | 0xFF000000;
					sprite.IsChrRamTile = isChrRam;
					mapper->CopyChrTile(spriteInfoEx.AbsoluteTileAddr & 0xFFFFFFF0, sprite.TileData);

					//#470: a fully transparent sprite tile makes no rule, just as
					//OamFetchLatch gives it no sheet key.
					if(!OamFetchLatch::IsFullyTransparent(sprite)) {
						_hdPackBuilder->ProcessTile(_cycle - 1, _scanline, spriteInfoEx.AbsoluteTileAddr, sprite, mapper, false, bankIdOf(spriteInfoEx.TileAddr), false);
					}
				}
			}

			if(_mask.BackgroundEnabled) {
				bool usePrev = (_xScroll + ((_cycle - 1) & 0x07) < 8);
				uint8_t tilePalette = usePrev ? _previousTilePalette : _currentTilePalette;
				NesTileInfoEx& lastTileEx = usePrev ? _previousTileEx : _currentTileEx;
				//TileInfo* lastTile = &((_xScroll + ((_cycle - 1) & 0x07) < 8) ? _previousTile : _currentTile);
				if(lastTileEx.AbsoluteTileAddr >= 0) {
					HdPpuTileInfo tile = {};
					tile.TileIndex = (isChrRam ? (lastTileEx.TileAddr & _chrRamIndexMask) : lastTileEx.AbsoluteTileAddr) / 16;
					tile.PaletteColors = ReadPaletteRam(tilePalette + 3) | (ReadPaletteRam(tilePalette + 2) << 8) | (ReadPaletteRam(tilePalette + 1) << 16) | (ReadPaletteRam(0) << 24);
					tile.IsChrRamTile = isChrRam;
					mapper->CopyChrTile(lastTileEx.AbsoluteTileAddr & 0xFFFFFFF0, tile.TileData);

					_hdPackBuilder->ProcessTile(_cycle - 1, _scanline, lastTileEx.AbsoluteTileAddr, tile, mapper, false, bankIdOf(lastTileEx.TileAddr), hasBgSprite);
					_hdPackBuilder->ProcessBgPixel(_cycle - 1, _scanline, tile, (uint8_t)backgroundColor);
				}
			}
		} else {
			//"If the current VRAM address points in the range $3F00-$3FFF during forced blanking, the color indicated by this palette location will be shown on screen instead of the backdrop color."
			_currentOutputBuffer[(_scanline << 8) + _cycle - 1] = _paletteRam[_videoRamAddr & 0x1F];
		}
	}

private:
	//OAM flips are attribute bits, not tile data, so a mirrored half of a figure
	//shares its CHR with its twin. Baking the flip into the recorded shape is
	//what lets the two sit side by side on a sheet instead of collapsing into
	//one cell (a group cannot place the same vocabulary entry twice). ADR-0178:
	//the flags travel on the tile as well, so the sheet sidecar can still name
	//the unflipped data hires.txt keys by on a CHR RAM game.
	static void ApplyFlips(uint8_t* tileData, bool horizontalMirror, bool verticalMirror)
	{
		MesenSheets::ApplyTileFlips(tileData, horizontalMirror, verticalMirror);
	}

	//#450: the sprite halves the PPU fetches for the next screen row, decoded
	//now - the moment the PPU itself reads PPUCTRL for them - with the CHR
	//this fetch sees. The row just drawn hands over whether it showed sprites
	//and its palettes, which record the halves fetched for it.
	void LatchFetchedSprites()
	{
		BaseMapper* mapper = _console->GetMapper();
		bool isChrRam = !mapper->HasChrRom();
		bool rowShown = _spriteRowShown;
		_spriteRowShown = false;
		_oamLatch.OnSpriteFetch(_scanline, rowShown, _spriteRowPalettes, _prevRenderingEnabled, _spriteRam, _control.LargeSprites, _control.SpritePatternAddr,
			[&](OamFetchLatch::Half& h, HdPpuTileInfo& sprite) {
				int32_t absoluteTileAddr = mapper->GetPpuAbsoluteAddress(h.TileAddr).Address;
				if(absoluteTileAddr < 0) {
					return false;
				}
				h.AbsoluteAddr = absoluteTileAddr & ~0x0F;
				sprite = {};
				sprite.TileIndex = (isChrRam ? (h.TileAddr & _chrRamIndexMask) : (uint32_t)absoluteTileAddr) / 16;
				//PaletteColors comes from the row the half is drawn on.
				sprite.IsChrRamTile = isChrRam;
				mapper->CopyChrTile((uint32_t)absoluteTileAddr & 0xFFFFFFF0, sprite.TileData);
				ApplyFlips(sprite.TileData, h.HorizontalMirror, h.VerticalMirror);
				//ADR-0178: recorded, not just consumed - HdPackBuilder un-bakes
				//them to recover the key the run time actually looks up.
				sprite.HorizontalMirroring = h.HorizontalMirror;
				sprite.VerticalMirroring = h.VerticalMirror;
				return true;
			});
	}

public:
	//ADR-0232 (#467): spelled WriteRAM from the 2022 port until 2026-09, which
	//overrode nothing, so a CHR write never marked the banks stale and every
	//tile of a power-on recording carried the all-zero bank's id, 0. The
	//`override` is what keeps it an override.
	void WriteRam(uint16_t addr, uint8_t value) override
	{
		if(GetRegisterID(addr) == PpuRegisters::VideoMemoryData) {
			_bankHashes.OnVideoMemoryWrite(_videoRamAddr);
		}
		NesPpu::WriteRam(addr, value);
	}

	void Serialize(Serializer& s) override
	{
		NesPpu::Serialize(s);
		if(!s.IsSaving()) {
			_bankHashes.MarkStale();
			//#450: halves latched before a load belong to a frame the loaded
			//timeline never drew.
			_oamLatch.Clear();
			_spriteRowShown = false;
		}
	}

public:
	HdBuilderPpu(NesConsole* console, HdPackBuilder* hdPackBuilder, uint32_t chrRamBankSize) : NesPpu(console), _bankHashes(chrRamBankSize)
	{
		_hdPackBuilder = hdPackBuilder;
		_chrRamIndexMask = chrRamBankSize - 1;
	}
};