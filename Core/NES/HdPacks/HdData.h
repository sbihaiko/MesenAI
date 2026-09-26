#pragma once
#include "pch.h"
#include "NES/NesConstants.h"
//ADR-0236 (F14.11): HdBackgroundInfo carries a capture's per-cell key record,
//and HdCellKeyOf below is the one bridge between the run time's tile struct and
//the guard's own key. The two are split that way so HdCaptureCellGuard.h stays
//free of this header (and of everything it drags in) and the unit tests can
//reach the record, the tag grammar and the predicate with no emulator at all.
#include "NES/HdPacks/HdCaptureCellGuard.h"
#include "Shared/MessageManager.h"
#include "Utilities/PNGHelper.h"
#include "Utilities/HexUtilities.h"
#include "Utilities/SimpleLock.h"
#include "Utilities/Timer.h"
//F12.1: HdPackData::LoadAsync reports its own cost (see the log line below).
#include <chrono>
//F12.3 (ADR-0212): a reload re-stats the file each image was decoded from.
#include <filesystem>
#include <fstream>
#include <set>

class BaseHdNesPack;

struct HdTileKey
{
	static constexpr int32_t NoTile = -1;

	//Code depends on these 2 fields being one after the other
	uint32_t PaletteColors = 0;
	uint8_t TileData[16] = {};

	int32_t TileIndex = 0;
	bool IsChrRamTile = false;

	HdTileKey GetKey(bool defaultKey) const
	{
		if(defaultKey) {
			HdTileKey copy = *this;
			copy.PaletteColors = 0xFFFFFFFF;
			return copy;
		} else {
			return *this;
		}
	}

	uint32_t GetHashCode() const
	{
		if(IsChrRamTile) {
			return CalculateHash((uint8_t*)&PaletteColors, sizeof(PaletteColors) + sizeof(TileData));
		} else {
			return (uint32_t)TileIndex ^ PaletteColors;
		}
	}

	size_t operator()(const HdTileKey& tile) const
	{
		return tile.GetHashCode();
	}

	bool operator==(const HdTileKey& other) const
	{
		if(IsChrRamTile) {
			return memcmp((uint8_t*)&PaletteColors, (uint8_t*)&other.PaletteColors, sizeof(PaletteColors) + sizeof(TileData)) == 0;
		} else {
			return TileIndex == other.TileIndex && PaletteColors == other.PaletteColors;
		}
	}

	uint32_t CalculateHash(const uint8_t* key, size_t len) const
	{
		uint32_t result = 0;
		for(size_t i = 0; i < len; i += 4) {
			uint32_t chunk;
			memcpy(&chunk, key, sizeof(uint32_t));

			result += chunk;
			result = (result << 2) | (result >> 30);
			key += 4;
		}
		return result;
	}

	bool IsSpriteTile()
	{
		return (PaletteColors & 0xFF000000) == 0xFF000000;
	}
};

namespace std
{
	template<> struct hash<HdTileKey>
	{
		size_t operator()(const HdTileKey& x) const
		{
			return x.GetHashCode();
		}
	};
}

struct HdPpuTileInfo : public HdTileKey
{
	uint8_t OffsetX = 0;
	uint8_t OffsetY = 0;
	bool HorizontalMirroring = false;
	bool VerticalMirroring = false;
	bool BackgroundPriority = false;

	uint8_t BgColorIndex = 0;
	uint8_t SpriteColorIndex = 0;
	uint8_t BgColor = 0;
	uint8_t SpriteColor = 0;
	uint8_t PpuBackgroundColor = 0;
	uint8_t PaletteOffset = 0;
};

static_assert(HdCellKeyRecord::NoTileIndex == HdTileKey::NoTile, "the guard's no-tile value must be the run time's");

//The tile's own three fields, verbatim and nothing else - what
//`HdPackTileAtPositionCondition` has always compared, and what it keeps
//comparing now that the comparison is shared (ADR-0236 §2). It does not read
//`NoTile` as `Kind::None`: the gate compared the fields at such a pixel too.
inline HdCellKey HdCellKeyFieldsOf(const HdPpuTileInfo& tile)
{
	HdCellKey key;
	key.KeyKind = tile.IsChrRamTile ? HdCellKey::Kind::ChrData : HdCellKey::Kind::ChrIndex;
	key.TileIndex = tile.TileIndex;
	key.PaletteColors = tile.PaletteColors;
	memcpy(key.TileData, tile.TileData, sizeof(key.TileData));
	return key;
}

//ADR-0236 (F14.11): the run time's tile as the guard reads it. `NoTile` - what
//HdNesPpu stores where the ROM's background is off for that pixel, or where its
//leftmost 8 pixels are clipped - is `Kind::None`, the same value the recorder
//writes for such a cell, so a clipped column still matches itself.
inline HdCellKey HdCellKeyOf(const HdPpuTileInfo& tile)
{
	if(tile.TileIndex == HdPpuTileInfo::NoTile) {
		return HdCellKey();
	}
	return HdCellKeyFieldsOf(tile);
}

//#474: the recorder's shape identity (HdPackBuilder::ShapeIdFor), and the
//hasher for its map. HdTileKey is the run time's key: on a CHR ROM game it
//compares the index only, because the run time mirrors the replacement art
//itself. A shape is a drawing, though - HdBuilderPpu bakes the OAM flips into
//TileData so a figure's mirrored halves are distinct shapes (ADR-0178) - so one
//index drawn both ways must be two shapes, or every OAM entry of the second
//orientation names the first one's art. The drawn data joins the index here,
//which makes CHR ROM behave as CHR RAM (keyed by that data) always has. The
//hash stays HdTileKey's: the two orientations of an index share a bucket.
struct HdShapeKey : public HdTileKey
{
	HdShapeKey() = default;
	explicit HdShapeKey(const HdTileKey& key) : HdTileKey(key) {}

	bool operator==(const HdShapeKey& other) const
	{
		return HdTileKey::operator==(other) && (IsChrRamTile || memcmp(TileData, other.TileData, sizeof(TileData)) == 0);
	}
};

struct HdPpuPixelInfo
{
	HdPpuTileInfo Tile = {};
	HdPpuTileInfo Sprite[4] = {};

	uint16_t TmpVideoRamAddr = 0;
	uint8_t XScroll = 0;
	uint8_t EmphasisBits = 0;
	bool Grayscale = false;
	uint8_t SpriteCount = 0;
};

struct HdScreenInfo
{
	HdPpuPixelInfo* ScreenTiles;
	unordered_map<uint32_t, uint8_t> WatchedAddressValues;
	uint32_t FrameNumber = 0;

	HdScreenInfo(const HdScreenInfo& that) = delete;

	HdScreenInfo(bool isChrRamGame)
	{
		ScreenTiles = new HdPpuPixelInfo[NesConstants::ScreenPixelCount];

		for(int i = 0; i < NesConstants::ScreenPixelCount; i++) {
			ScreenTiles[i].Tile.BackgroundPriority = false;
			ScreenTiles[i].Tile.IsChrRamTile = isChrRamGame;
			ScreenTiles[i].Tile.HorizontalMirroring = false;
			ScreenTiles[i].Tile.VerticalMirroring = false;

			for(int j = 0; j < 4; j++) {
				ScreenTiles[i].Sprite[j].IsChrRamTile = isChrRamGame;
			}
		}
	}

	~HdScreenInfo()
	{
		delete[] ScreenTiles;
	}
};

enum class HdPackConditionType
{
	HMirror,
	VMirror,
	BgPriority,
	FrameRange,
	MemoryCheck,
	MemoryCheckConstant,
	TileNearby,
	TileAtPos,
	SpriteAtPos,
	SpriteNearby,
	SpritePalette,
	PositionCheckX,
	PositionCheckY,
	OriginPositionCheckX,
	OriginPositionCheckY,
};

struct HdPackCondition
{
protected:
	HdScreenInfo* _screenInfo = nullptr;
	BaseHdNesPack* _hdPack = nullptr;

public:
	string Name;

	virtual HdPackConditionType GetConditionType() = 0;
	virtual string GetConditionName() = 0;
	virtual bool IsExcludedFromFile() { return Name.size() > 0 && Name[0] == '!'; }
	virtual string ToString() = 0;

	virtual ~HdPackCondition() {}

	void Initialize(HdScreenInfo* screenInfo, BaseHdNesPack* hdPack)
	{
		_screenInfo = screenInfo;
		_hdPack = hdPack;
		_resultCache = -1;
	}

	bool CheckCondition(int x, int y, HdPpuTileInfo* tile)
	{
		if(_resultCache >= 0) {
			return (bool)_resultCache;
		}

		bool result = InternalCheckCondition(x, y, tile);
		if(Name[0] == '!') {
			result = !result;
		}

		if(_useCache) {
			_resultCache = result ? 1 : 0;
		}
		return result;
	}

protected:
	int8_t _resultCache = -1;
	bool _useCache = false;

	virtual bool InternalCheckCondition(int x, int y, HdPpuTileInfo* tile) = 0;
};

//F12.3 (ADR-0212 §1/§2/§4): what one reload attempt did to one image. The
//caller logs from this; nothing here decides policy.
enum class HdPackImageReload
{
	NotWatched,     //zip-backed pack, or the image never got a source path
	Unchanged,      //the (size, mtime) pair on disk still matches
	Needed,         //ClassifyReload only: the file moved and a decode is due
	Reloaded,       //re-decoded in place, same dimensions
	RefusedResize,  //the repaint changed the canvas; old pixels kept (ADR-0212 §4)
	ReadFailed      //the file went away, or the PNG no longer decodes
};

//F12.3 (ADR-0212): what one reload sweep did, for the single log line the
//console writes and for the interop caller that wants to know.
struct HdPackReloadResult
{
	uint32_t Scanned = 0;
	uint32_t Reloaded = 0;
	uint32_t Refused = 0;
	uint32_t Failed = 0;
	uint32_t NotWatched = 0;
	uint32_t TilesInvalidated = 0;
	double Milliseconds = 0;
};

struct HdPackBitmapInfo
{
private:
	bool _initDone = false;
	SimpleLock _lock;

public:
	string PngName;
	vector<uint8_t> FileData;
	vector<uint32_t> PixelData;
	uint32_t Width;
	uint32_t Height;

	//F12.3 (ADR-0212 §2): the absolute file this image was decoded from, plus
	//the (size, mtime) pair it had then. SourcePath is empty for a zip-backed
	//pack - there is nothing to stat, so it is never reloaded (ADR-0212 §5).
	string SourcePath;
	uint64_t SourceSize = 0;
	int64_t SourceTime = 0;

	//Free of PNG decoding on purpose: the change rule is the part worth unit
	//testing, and Utilities/PNGHelper.cpp is not in the core-unit-tests link
	//set. Returns false when the path cannot be stat'ed at all.
	static bool StatSource(const string& path, uint64_t& size, int64_t& time)
	{
		if(path.empty()) {
			return false;
		}
		std::error_code ec;
		std::filesystem::path fsPath = std::filesystem::u8path(path);
		uintmax_t fileSize = std::filesystem::file_size(fsPath, ec);
		if(ec) {
			return false;
		}
		std::filesystem::file_time_type writeTime = std::filesystem::last_write_time(fsPath, ec);
		if(ec) {
			return false;
		}
		size = (uint64_t)fileSize;
		time = (int64_t)writeTime.time_since_epoch().count();
		return true;
	}

	void RecordSourceFingerprint()
	{
		if(!StatSource(SourcePath, SourceSize, SourceTime)) {
			SourceSize = 0;
			SourceTime = 0;
		}
	}

	//True when the file on disk differs from what was recorded. A path that
	//cannot be stat'ed reads as unchanged: a reload must not throw away good
	//pixels because the file is momentarily unreadable (a paint program
	//writing through a temporary file is the normal case).
	bool SourceChanged() const
	{
		uint64_t size = 0;
		int64_t time = 0;
		if(!StatSource(SourcePath, size, time)) {
			return false;
		}
		return size != SourceSize || time != SourceTime;
	}

	//ADR-0212 section 1/section 4: the two rules a reload obeys, split out so
	//they can be unit tested. Utilities/PNGHelper.cpp is not in the
	//core-unit-tests link set (and spng.c is a C file the set has no rule for),
	//so anything that touches the decoder cannot be reached from a test - the
	//decision has to live where the decoder does not.
	HdPackImageReload ClassifyReload() const
	{
		if(SourcePath.empty()) {
			return HdPackImageReload::NotWatched;
		}
		return SourceChanged() ? HdPackImageReload::Needed : HdPackImageReload::Unchanged;
	}

	//A repaint may only replace the pixels in place when it kept the canvas:
	//HdPackTileInfo caches x/y/w/h from the manifest and indexes PixelData with
	//them, so a smaller image would be read out of bounds.
	static bool DimensionsAllowInPlaceSwap(uint32_t oldWidth, uint32_t oldHeight, uint32_t newWidth, uint32_t newHeight)
	{
		return oldWidth == newWidth && oldHeight == newHeight;
	}

	void Init()
	{
		if(_initDone) {
			return;
		}

		auto lock = _lock.AcquireSafe();
		if(_initDone) {
			return;
		}

		//Timer tmr;
		if(PNGHelper::ReadPNG(FileData, PixelData, Width, Height)) {
			//MessageManager::Log("[HDPack] PNG file loaded: " + PngName + " (" + std::to_string(tmr.GetElapsedMS()) + ")");
			PremultiplyAlpha();
		} else {
			MessageManager::Log("[HDPack] PNG file " + PngName + " is invalid.");
		}
		FileData = {};
		_initDone = true;
	}

	//F12.3 (ADR-0212 §1): re-decode this image from its own file, into this
	//same object. Nothing that points here is invalidated - not
	//HdPackTileInfo::Bitmap, not the three raw HdPackData* holders - which is
	//exactly why the reload is shaped as an in-place decode rather than a swap.
	//Runs on the emulation thread with VideoDecoder's decode thread drained
	//(ADR-0212 §3); the lock here only keeps it off LoadAsync's toes.
	HdPackImageReload ReloadFromDisk(uint32_t* outNewWidth = nullptr, uint32_t* outNewHeight = nullptr)
	{
		HdPackImageReload need = ClassifyReload();
		if(need != HdPackImageReload::Needed) {
			return need;
		}

		vector<uint8_t> bytes;
		{
			std::ifstream file(SourcePath, std::ios::in | std::ios::binary);
			if(!file.good()) {
				//Left un-fingerprinted on purpose: an explicit reload should
				//retry a file it could not read, not swallow it once.
				return HdPackImageReload::ReadFailed;
			}
			file.seekg(0, std::ios::end);
			std::streamoff size = file.tellg();
			file.seekg(0, std::ios::beg);
			bytes.resize((size_t)(size > 0 ? size : 0));
			if(!bytes.empty()) {
				file.read((char*)bytes.data(), (std::streamsize)bytes.size());
			}
		}

		auto lock = _lock.AcquireSafe();
		if(!_initDone) {
			//LoadAsync has not reached this image yet: hand it the new bytes
			//and let the decode it is already going to do read the new file.
			FileData = std::move(bytes);
			RecordSourceFingerprint();
			return HdPackImageReload::Reloaded;
		}

		vector<uint32_t> pixels;
		uint32_t width = 0;
		uint32_t height = 0;
		if(!PNGHelper::ReadPNG(bytes, pixels, width, height)) {
			return HdPackImageReload::ReadFailed;
		}
		if(outNewWidth) { *outNewWidth = width; }
		if(outNewHeight) { *outNewHeight = height; }
		if(!DimensionsAllowInPlaceSwap(Width, Height, width, height)) {
			//ADR-0212 §4: HdPackTileInfo caches x/y/w/h from the manifest, so a
			//resized canvas would be read out of bounds. Keep the old pixels.
			RecordSourceFingerprint();
			return HdPackImageReload::RefusedResize;
		}

		PixelData = std::move(pixels);
		PremultiplyAlpha();
		RecordSourceFingerprint();
		return HdPackImageReload::Reloaded;
	}

	void PremultiplyAlpha()
	{
		for(size_t i = 0; i < PixelData.size(); i++) {
			if(PixelData[i] < 0xFF000000) {
				uint8_t* output = (uint8_t*)(PixelData.data() + i);
				uint8_t alpha = output[3] + 1;
				output[0] = (uint8_t)((alpha * output[0]) >> 8);
				output[1] = (uint8_t)((alpha * output[1]) >> 8);
				output[2] = (uint8_t)((alpha * output[2]) >> 8);
			}
		}
	}
};

struct HdPackTileInfo : public HdTileKey
{
private:
	bool _needInit = true;

public:
	uint32_t X;
	uint32_t Y;
	uint32_t Width;
	uint32_t Height;
	uint32_t BitmapIndex;
	HdPackBitmapInfo* Bitmap;
	int Brightness;
	bool DefaultTile;
	bool Blank;
	bool HasTransparentPixels;
	bool TransparencyRequired;
	bool IsFullyTransparent;
	//-1 unknown, 0 no, 1 yes: a defaultTile whose pixels are all gray (the
	//bootstrap's neutral ramp) is recolored with the real palette when drawn
	int8_t NeutralRamp = -1;
	vector<uint32_t> HdTileData;
	uint32_t ChrBankId;

	vector<HdPackCondition*> Conditions;
	bool ForceDisableCache;

	bool MatchesCondition(int x, int y, HdPpuTileInfo* tile)
	{
		for(HdPackCondition* condition : Conditions) {
			if(!condition->CheckCondition(x, y, tile)) {
				return false;
			}
		}
		return true;
	}

	vector<uint32_t> ToRgb(uint32_t* palette)
	{
		vector<uint32_t> rgbBuffer;
		for(uint8_t i = 0; i < 8; i++) {
			uint8_t lowByte = TileData[i];
			uint8_t highByte = TileData[i + 8];
			for(uint8_t j = 0; j < 8; j++) {
				uint8_t color = ((lowByte >> (7 - j)) & 0x01) | (((highByte >> (7 - j)) & 0x01) << 1);
				uint32_t rgbColor;
				if(IsSpriteTile() || TransparencyRequired) {
					rgbColor = color == 0 ? 0x00FFFFFF : palette[(PaletteColors >> ((3 - color) * 8)) & 0x3F];
				} else {
					rgbColor = palette[(PaletteColors >> ((3 - color) * 8)) & 0x3F];
				}
				rgbBuffer.push_back(rgbColor);
			}
		}

		return rgbBuffer;
	}

	void UpdateFlags()
	{
		Blank = true;
		HasTransparentPixels = false;
		IsFullyTransparent = true;
		for(size_t i = 0; i < HdTileData.size(); i++) {
			if(HdTileData[i] != HdTileData[0]) {
				Blank = false;
			}
			if((HdTileData[i] & 0xFF000000) != 0xFF000000) {
				HasTransparentPixels = true;
			}
			if(HdTileData[i] & 0xFF000000) {
				IsFullyTransparent = false;
			}
		}
	}

	//F12.3 (ADR-0212): Init() copies the crop out of Bitmap->PixelData into
	//HdTileData, so re-decoding the bitmap alone is invisible - this second
	//cache layer has to be dropped too, or a repaint renders the old pixels.
	//Called on the emulation thread with the decode thread drained, which is
	//the same thread discipline the Init() below relies on.
	void InvalidateCachedPixels()
	{
		_needInit = true;
	}

	__forceinline bool NeedInit()
	{
		return _needInit;
	}

	__noinline void Init()
	{
		_needInit = false;
		Bitmap->Init();

		uint32_t bitmapOffset = Y * Bitmap->Width + X;
		uint32_t* pngData = Bitmap->PixelData.data();

		HdTileData.resize(Width * Height);
		if(Bitmap->PixelData.size() >= bitmapOffset + ((Height - 1) * Bitmap->Width) + Width) {
			for(uint32_t y = 0; y < Height; y++) {
				memcpy(HdTileData.data() + (y * Width), pngData + bitmapOffset, Width * sizeof(uint32_t));
				bitmapOffset += Bitmap->Width;
			}
		}

		UpdateFlags();
	}

	string ToString(int pngIndex)
	{
		stringstream out;

		if(Conditions.size() > 0) {
			out << "[";
			for(size_t i = 0; i < Conditions.size(); i++) {
				if(i > 0) {
					out << "&";
				}
				out << Conditions[i]->Name;
			}
			out << "]";
		}

		if(IsChrRamTile) {
			out << "<tile>" << pngIndex << ",";

			for(int i = 0; i < 16; i++) {
				out << HexUtilities::ToHex(TileData[i]);
			}
			out << "," << HexUtilities::ToHex(PaletteColors, true) << "," << X << "," << Y << "," << (double)Brightness / 255 << "," << (DefaultTile ? "Y" : "N") << "," << ChrBankId << "," << TileIndex;
		} else {
			out << "<tile>" << pngIndex << "," << HexUtilities::ToHex(TileIndex) << "," << HexUtilities::ToHex(PaletteColors, true) << "," << X << "," << Y << "," << (double)Brightness / 255 << "," << (DefaultTile ? "Y" : "N");
		}

		return out.str();
	}
};

enum class HdPackBlendMode
{
	Alpha,
	Add,
	Subtract
};

struct HdBackgroundInfo
{
	HdPackBitmapInfo* Data;
	int Brightness;
	vector<HdPackCondition*> Conditions;
	float HorizontalScrollRatio;
	float VerticalScrollRatio;
	uint8_t Priority;

	uint32_t Left;
	uint32_t Top;

	HdPackBlendMode BlendMode;

	//ADR-0236 (F14.11): the capture's positional cell-key record, present only
	//on a `<background>` the recorder wrote from a frame and only since this
	//slice - IsPresent() is the whole opt-in. Empty for every hand-made pack and
	//for every pack already on disk, which is why the guard costs those packs
	//nothing at all.
	HdCellKeyRecord CellRecord;

	//The record's line, written directly under the `<background>` line it
	//belongs to and read back from there (HdPackLoader::LoadPack), so a pack
	//that is edited, rebuilt or re-recorded keeps the guard or loses it whole.
	void WriteCellRecord(stringstream& out) const
	{
		if(CellRecord.IsPresent()) {
			out << CellRecord.ToString() << std::endl;
		}
	}

	uint32_t* data()
	{
		return Data->PixelData.data();
	}

	string ToString()
	{
		stringstream out;

		if(Conditions.size() > 0) {
			out << "[";
			for(size_t i = 0; i < Conditions.size(); i++) {
				if(i > 0) {
					out << "&";
				}
				out << Conditions[i]->Name;
			}
			out << "]";
		}

		out << "<background>";
		out << Data->PngName << ",";
		out << (Brightness / 255.0);
		if(HorizontalScrollRatio != 0 || VerticalScrollRatio != 0 || Priority != 10 || Left != 0 || Top != 0) {
			//Optional fields (v101+/v102+/v105+): without them a reload would fall back to priority 10
			out << "," << HorizontalScrollRatio << "," << VerticalScrollRatio << "," << (int)Priority;
			if(Left != 0 || Top != 0) {
				out << "," << Left << "," << Top;
			}
		}

		return out.str();
	}
};

struct HdPackAdditionalSpriteInfo
{
	HdTileKey OriginalTile;
	HdTileKey AdditionalTile;

	int32_t OffsetX;
	int32_t OffsetY;
	bool IgnorePalette;
};

struct FallbackTileInfo
{
	int32_t TileIndex;
	int32_t FallbackTileIndex;
};

struct BgmTrackInfo
{
	string Filename;
	uint32_t LoopPosition = 0;
};

struct HdPackData
{
private:
	bool _cancelLoad = false;

public:
	static constexpr int BgLayerCount = 40;

	vector<HdBackgroundInfo> BackgroundsByPriority[HdPackData::BgLayerCount];
	vector<unique_ptr<HdPackBitmapInfo>> BackgroundFileData;
	vector<unique_ptr<HdPackBitmapInfo>> ImageFileData;
	vector<unique_ptr<HdPackTileInfo>> Tiles;
	vector<unique_ptr<HdPackCondition>> Conditions;
	vector<HdPackAdditionalSpriteInfo> AdditionalSprites;
	vector<FallbackTileInfo> FallbackTiles;
	unordered_set<uint32_t> WatchedMemoryAddresses;
	unordered_map<HdTileKey, vector<HdPackTileInfo*>> TileByKey;
	unordered_map<string, string> PatchesByHash;
	unordered_map<int, BgmTrackInfo> BgmFilesById;
	unordered_map<int, string> SfxFilesById;
	vector<uint32_t> Palette;

	bool HasOverscanConfig = false;
	OverscanDimensions Overscan;

	uint32_t Scale = 1;
	uint32_t Version = 0;
	uint32_t OptionFlags = 0;

	//ADR-0224: the pack carried <bgPreservesBehindBgSprites>, so a layer-2
	//<background> must not hide a behind-background sprite over a colour-0
	//background pixel. Deliberately not an HdPackOptions bit: the <options>
	//line is a contract other emulators enforce (see HdBehindBgSpriteRule.h).
	bool PreservesBehindBgSprites = false;

	//ADR-0049's human-vs-auto line, carried along with the data so the renderer
	//can tell a pack somebody painted from the bootstrap's machine layer. Set by
	//NesConsole::LoadHdPack from the existing MepSection::HasHuman signal (and for
	//a legacy loose HdPacks/ pack, which has no auto layer at all) - never
	//re-derived here. A recorded-only pack keeps this false: its <background>
	//lines are the bootstrap's, so a diagnostic addressed to an artist would be
	//addressed to nobody (ADR-0146).
	bool HumanAuthoredTextures = false;

	HdPackData() {}
	~HdPackData() {}

	//False for audio-only data (MEP audio section / fingerprints): the HD PPU
	//is then not needed
	bool HasVideoContent() const
	{
		if(!Tiles.empty() || !AdditionalSprites.empty() || !FallbackTiles.empty() || !Palette.empty() || HasOverscanConfig) {
			return true;
		}
		for(int i = 0; i < BgLayerCount; i++) {
			if(!BackgroundsByPriority[i].empty()) {
				return true;
			}
		}
		return false;
	}

	HdPackData(const HdPackData&) = delete;
	HdPackData& operator=(const HdPackData&) = delete;

	void LoadAsync()
	{
		//F12.1: this runs on the detached thread NesConsole::LoadHdPack starts,
		//so the ROM load returns before the pack's images are ready. A number
		//that only covered the parse would understate what the user waits for,
		//so the decode reports its own cost here.
		std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();
		for(auto& bitmap : BackgroundFileData) {
			bitmap->Init();
			if(_cancelLoad) {
				LogLoadAsyncTime(start, true);
				return;
			}
		}
		for(auto& bitmap : ImageFileData) {
			bitmap->Init();
			if(_cancelLoad) {
				LogLoadAsyncTime(start, true);
				return;
			}
		}
		LogLoadAsyncTime(start, false);
	}

	void LogLoadAsyncTime(std::chrono::steady_clock::time_point start, bool cancelled)
	{
		double ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
		MessageManager::Log("[MEP] LoadAsync (bitmap decode): " + std::to_string((int)(ms + 0.5)) + " ms, " +
			std::to_string(BackgroundFileData.size() + ImageFileData.size()) + " image(s)" + (cancelled ? " (cancelled)" : ""));
	}

	void CancelLoad()
	{
		_cancelLoad = true;
	}

	//F12.3 (ADR-0212): re-decode every image whose file changed on disk, in
	//place. An unchanged pack costs one stat per image and decodes nothing -
	//that is the whole point of per-image invalidation (F12.1 finding 1: the
	//pack's load is the 13-16 s decode, not the 0.4 s parse).
	HdPackReloadResult ReloadChangedImages()
	{
		std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();
		HdPackReloadResult result;
		std::set<const HdPackBitmapInfo*> reloaded;
		auto sweep = [&](vector<unique_ptr<HdPackBitmapInfo>>& images) {
			for(auto& bitmap : images) {
				result.Scanned++;
				uint32_t newWidth = 0;
				uint32_t newHeight = 0;
				switch(bitmap->ReloadFromDisk(&newWidth, &newHeight)) {
					case HdPackImageReload::Reloaded:
						result.Reloaded++;
						reloaded.insert(bitmap.get());
						MessageManager::Log("[MEP] reload: " + bitmap->PngName + " re-decoded");
						break;
					case HdPackImageReload::RefusedResize:
						result.Refused++;
						MessageManager::Log("[MEP] reload: " + bitmap->PngName + " changed size (" +
							std::to_string(bitmap->Width) + "x" + std::to_string(bitmap->Height) + " -> " +
							std::to_string(newWidth) + "x" + std::to_string(newHeight) +
							") - reopen the ROM to pick it up");
						break;
					case HdPackImageReload::ReadFailed:
						result.Failed++;
						MessageManager::Log("[MEP] reload: " + bitmap->PngName + " could not be read");
						break;
					case HdPackImageReload::NotWatched:
						result.NotWatched++;
						break;
					case HdPackImageReload::Unchanged:
					case HdPackImageReload::Needed:
						break;
				}
			}
		};
		sweep(BackgroundFileData);
		sweep(ImageFileData);

		//A <background> reads Data->PixelData straight through and needs
		//nothing more. A <tile> does not: HdPackTileInfo::Init() memcpy'd its
		//crop out of the bitmap once and has served it from HdTileData ever
		//since, so every tile cut from a re-decoded image has to be told to cut
		//it again. Linear over Tiles, which is fine for an explicit, rare
		//action - the Metroid pack's 150 199 rules are one pass.
		if(!reloaded.empty()) {
			for(auto& tile : Tiles) {
				if(tile->Bitmap && reloaded.count(tile->Bitmap)) {
					tile->InvalidateCachedPixels();
					result.TilesInvalidated++;
				}
			}
		}
		result.Milliseconds = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
		return result;
	}
};

enum class HdPackOptions
{
	None = 0,
	NoSpriteLimit = 1,
	AlternateRegisterRange = 2,
	DisableCache = 8,
	DontRenderOriginalTiles = 16,
	AutomaticFallbackTiles = 32
};

//The token list of an <options> line, in the order the writer has always
//emitted them. Host-free so it can be unit-tested (ADR-0127): the line is a
//format contract, and a trailing comma is not cosmetic. StringUtilities::Split
//always pushes a final token, so "a,b," yields an empty last entry, which
//HdPackLoader::ProcessOptionTag reports as "Invalid option: " and counts as a
//load error. The hand-written community packs carry no trailing comma for the
//same reason.
inline string HdPackOptionsToString(uint32_t flags)
{
	vector<string> tokens;
	if(flags & (int)HdPackOptions::NoSpriteLimit) {
		tokens.push_back("disableSpriteLimit");
	}
	if(flags & (int)HdPackOptions::AlternateRegisterRange) {
		tokens.push_back("alternateRegisterRange");
	}
	if(flags & (int)HdPackOptions::DisableCache) {
		tokens.push_back("disableCache");
	}
	if(flags & (int)HdPackOptions::DontRenderOriginalTiles) {
		tokens.push_back("disableOriginalTiles");
	}
	if(flags & (int)HdPackOptions::AutomaticFallbackTiles) {
		tokens.push_back("automaticFallbackTiles");
	}

	string out;
	for(size_t i = 0; i < tokens.size(); i++) {
		if(i > 0) {
			out += ',';
		}
		out += tokens[i];
	}
	return out;
}