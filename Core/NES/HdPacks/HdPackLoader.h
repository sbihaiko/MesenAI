#pragma once
#include "pch.h"
#include "NES/HdPacks/HdData.h"
#include "NES/HdPacks/HdPackErrorDedupe.h"
#include "Utilities/ZipReader.h"
#include "Utilities/VirtualFile.h"

enum class HdPackConditionOperator;

class HdPackLoader
{
public:
	static bool LoadHdNesPack(string definitionFile, HdPackData& outData);
	static bool LoadHdNesPack(VirtualFile& romFile, HdPackData& outData);

	//True when HdPacks/<rom>/hires.txt exists (legacy loose pack)
	static bool HasLoosePack(VirtualFile& romFile);

	//F5 (ADR-0049): appends a lower-precedence layer (the pack's auto/
	//hires.txt) under an already loaded one. Entries whose tile key already
	//exists in "into" are dropped; everything else (tiles, backgrounds,
	//conditions, bitmaps, audio tracks, patches) is moved over. Both layers
	//must share the same <scale>; returns false (and merges nothing) otherwise.
	static bool MergeLowerLayer(HdPackData& into, HdPackData& lower, bool includeBackgrounds = true);

private:
	HdPackData* _data = nullptr;
	bool _loadFromZip = false;
	int _currentLine = 0;
	int _errorCount = 0;
	//Issue #302: first-occurrence-only logging for repeated loader errors.
	HdPackErrorDedupe _errorLog;
	ZipReader _reader;
	string _hdPackDefinitionFile;
	string _hdPackFolder;
	unordered_map<string, HdPackCondition*> _conditionsByName;
	//Lower-cased name -> registered name, for the case-insensitive fallback in
	//ParseConditionString; rebuilt when _conditionsByName grew since.
	unordered_map<string, string> _conditionsByLower;
	size_t _conditionsByLowerCount = 0;
	unordered_map<string, HdPackBitmapInfo*> _backgroundsByName;
	unordered_map<string, string> _packFilesByLower;
	bool _packFilesIndexed = false;
	//ADR-0236 (F14.11): where the `<background>` line just parsed went, so a
	//`<bgCellRecord>` on the very next line can attach its record - and nothing
	//else can. The rule lives in HdCellRecordBinder (host-free, pinned by
	//`make core-unit-tests`) rather than in two integers this class has to
	//remember to clear: `Line()` is called once per line and is both the read
	//and the clear, so the state that dropped every record in every recorded
	//pack - clearing at the top of the very line the record is on - cannot be
	//written by accident. Coordinates, not a pointer: the vector a slot lives in
	//can reallocate, and a record on the wrong screen is worse than none.
	HdCellRecordBinder _cellRecordBinder;

	HdPackLoader();

	//Counts every occurrence, logs only the first of each distinct message.
	void LogError(const string& message);

	bool InitializeLoader(VirtualFile& romPath, HdPackData* data);
	//F12.3 (ADR-0212 §2): `outDiskPath`, when given, receives the absolute file
	//the bytes came from - empty for a zip-backed pack, which has no file to
	//stat and is therefore never reloaded (ADR-0212 §5).
	bool LoadFile(string filename, vector<uint8_t>& fileData, string* outDiskPath = nullptr);
	bool CheckFile(string filename);
	bool CheckFileExact(const string& filename);
	void TrimTokens(vector<string>& tokens);
	void IndexPackFiles();
	string ResolvePackRelativePath(string filename);

	bool LoadPack();
	void InitializeHdPack();
	void LoadCustomPalette();

	void ReadTileData(HdTileKey& key, string& tileData, string& palData);

	template<typename T> void AddGlobalCondition(string name);
	void InitializeGlobalConditions();

	//Video
	bool ProcessImgTag(string src);
	void ProcessPatchTag(vector<string>& tokens);
	void ProcessOverscanTag(vector<string>& tokens);
	void ProcessConditionTag(vector<string>& tokens, bool createInvertedCondition);
	HdPackConditionOperator ParseConditionOperator(string& opString);
	void ProcessTileTag(vector<string>& tokens, vector<HdPackCondition*> conditions);
	void ProcessBackgroundTag(vector<string>& tokens, vector<HdPackCondition*> conditions);
	//ADR-0236 (F14.11): the `<bgCellRecord>` line that follows its
	//`<background>`. Unparseable or orphaned, it is logged and dropped - the
	//`<background>` still loads and draws exactly as a pack without the record.
	void ProcessCellRecordTag(const string& payload);
	void ProcessAdditionTag(vector<string>& tokens);
	void ProcessFallbackTag(vector<string>& tokens);
	void ProcessOptionTag(vector<string>& tokens);

	//Audio
	int ProcessSoundTrack(string albumString, string trackString, string filename);
	void ProcessBgmTag(vector<string>& tokens);
	void ProcessSfxTag(vector<string>& tokens);

	vector<HdPackCondition*> ParseConditionString(string conditionString);
	bool ParseBooleanValue(string value);
};