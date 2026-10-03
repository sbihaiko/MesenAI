#include "pch.h"
#include <filesystem>
#include "Shared/EnhancementPacks/MepPackManager.h"
#include "Shared/EnhancementPacks/MepZipExtract.h"
#include "Shared/EnhancementPacks/MepContentId.h"
#include "Shared/EnhancementPacks/MepFileIo.h"
#include "Shared/EnhancementPacks/RemasterProject.h"
#include "Shared/MessageManager.h"
#include "Shared/Emulator.h"
#include "Shared/EmuSettings.h"
#include "Utilities/VirtualFile.h"
#include "Utilities/FolderUtilities.h"
#include "Utilities/StringUtilities.h"
#include "Utilities/miniz.h"
#include "Utilities/JsonReader.h"
#include "Utilities/sha1.h"
#include "Shared/Interfaces/IConsole.h"
#include "Shared/Interfaces/INotificationListener.h"
#include "NES/NesConsole.h"

namespace fs = std::filesystem;

namespace
{
	void Log(const string& message)
	{
		MessageManager::Log("[MEP] " + message);
	}

	bool HasExtension(const string& lowerExt, std::initializer_list<const char*> candidates)
	{
		for(const char* c : candidates) {
			if(lowerExt == c) {
				return true;
			}
		}
		return false;
	}

	//ADR-0120 §4: the archive the extracted MepZipExtract pipeline reads
	//through. miniz is driven directly on a buffer this object owns for as
	//long as the reader lives (mz_zip_reader_init_mem does not copy - the
	//previous ZipReader::LoadArchive(vector&) call left the reader pointing at
	//a dead local), and the per-entry declared sizes are exposed for the
	//MepFileIo decompression caps. Same seam MepRecipeSource already uses.
	class MinizZipArchive : public MepZipExtract::IArchive
	{
	public:
		~MinizZipArchive() override
		{
			if(_loaded) {
				mz_zip_reader_end(&_zip);
			}
		}

		bool Load(const string& zipPath, vector<string>& entries, string& error) override
		{
			if(!MepFileIo::ReadWholeFile(zipPath, _bytes)) {
				error = "cannot open zip";
				return false;
			}
			memset(&_zip, 0, sizeof(_zip));
			if(!mz_zip_reader_init_mem(&_zip, _bytes.data(), _bytes.size(), 0)) {
				error = "not a valid zip archive";
				return false;
			}
			_loaded = true;
			for(mz_uint i = 0, len = mz_zip_reader_get_num_files(&_zip); i < len; i++) {
				mz_zip_archive_file_stat stat;
				if(mz_zip_reader_file_stat(&_zip, i, &stat)) {
					entries.push_back(stat.m_filename);
				}
			}
			return true;
		}

		bool GetUncompressedSize(const string& entry, uint64_t& size) override
		{
			int index = mz_zip_reader_locate_file(&_zip, entry.c_str(), nullptr, 0);
			mz_zip_archive_file_stat stat;
			if(index < 0 || !mz_zip_reader_file_stat(&_zip, (mz_uint)index, &stat)) {
				return false;
			}
			size = stat.m_uncomp_size;
			return true;
		}

		bool ExtractFile(const string& entry, vector<uint8_t>& content) override
		{
			size_t size = 0;
			void* data = mz_zip_reader_extract_file_to_heap(&_zip, entry.c_str(), &size, 0);
			if(!data) {
				return false;
			}
			content.assign((uint8_t*)data, (uint8_t*)data + size);
			mz_free(data);
			return true;
		}

	private:
		mz_zip_archive _zip{};
		bool _loaded = false;
		vector<uint8_t> _bytes;
	};

	//ADR-0145: true when the file at "path" is a BPS patch (magic "BPS1").
	//BPS is the only patch format that self-validates the applied output
	//(embedded source+output CRC32, rejected on mismatch), so it is the only
	//one safe to attempt optimistically on a SHA1 mismatch. Format is decided
	//by content, not extension, matching VirtualFile::ApplyPatch's sniffing.
	bool IsBpsPatchFile(const string& path)
	{
		ifstream in(path, std::ios::in | std::ios::binary);
		if(!in) {
			return false;
		}
		char magic[4];
		in.read(magic, 4);
		return in.gcount() == 4 && memcmp(magic, "BPS1", 4) == 0;
	}

}

MepPackManager::MepPackManager(Emulator* emu)
{
	_emu = emu;
}

string MepPackManager::GetPacksFolder()
{
	string folder = FolderUtilities::CombinePath(FolderUtilities::GetHomeFolder(), FolderName);
	FolderUtilities::CreateFolder(folder);
	return folder;
}

string MepPackManager::ComputeNoIntroSha1(VirtualFile& romFile)
{
	vector<uint8_t>& data = romFile.GetData();
	size_t offset = 0;
	size_t size = data.size();

	string ext = StringUtilities::ToLower(romFile.GetFileExtension());
	if(ext == ".nes") {
		//iNES: 16-byte header, optional 512-byte trainer (flags6 bit 2)
		if(size >= 16 && memcmp(data.data(), "NES\x1A", 4) == 0) {
			offset = 16;
			if(data[6] & 0x04) {
				offset += 512;
			}
			//ADR-0044: hash only the PRG+CHR the header declares, so a dump
			//with trailing garbage still matches its clean No-Intro entry
			size_t prgUnits = data[4];
			size_t chrUnits = data[5];
			if((data[7] & 0x0C) == 0x08 && (data[9] & 0x0F) != 0x0F && (data[9] >> 4) != 0x0F) {
				//NES 2.0 size MSBs (exponent-multiplier form left alone)
				prgUnits |= (size_t)(data[9] & 0x0F) << 8;
				chrUnits |= (size_t)(data[9] >> 4) << 8;
			}
			size_t declared = offset + prgUnits * 0x4000 + chrUnits * 0x2000;
			if(declared > offset && declared < size) {
				size = declared;
			}
		}
	} else if(HasExtension(ext, { ".sfc", ".smc", ".swc", ".fig", ".bs", ".st" })) {
		//SNES copier header
		if(size % 1024 == 512) {
			offset = 512;
		}
	}

	if(offset > size) {
		offset = size;
	}
	return SHA1::GetHash(data.data() + offset, size - offset);
}

string MepPackManager::GetSiblingFolder() const
{
	auto lock = _stateLock.AcquireSafe();
	if(_romFolder.empty() || _romName.empty()) {
		return "";
	}
	return FolderUtilities::CombinePath(_romFolder, _romName);
}

void MepPackManager::StartBootstrapIfNeeded()
{
	//ADR-0243 Q3: Play no longer records by itself. The setting stays (off for
	//new installs, kept for an install that had it on); headless_record's
	//"bootstrap" flag turns it on for one run. Recording on demand is
	//StartRecording, which the Remaster profile's Record button calls.
	string source;
	string note;
	{
		auto lock = _stateLock.AcquireSafe();
		_bootstrapping = false;
		EnhancementPackConfig& cfg = _emu->GetSettings()->GetEnhancementPackConfig();
		if(!cfg.EnableMepPacks || !cfg.BootstrapEnhancementFolder || _romName.empty()) {
			return;
		}
		source = _nextRecordingSource;
		note = _nextRecordingNote;
	}
	StartRecording(source, note, false);
}

void MepPackManager::SetNextRecordingSource(const string& source, const string& note)
{
	auto lock = _stateLock.AcquireSafe();
	_nextRecordingSource = RemasterProject::IsKnownSource(source) ? source : "play";
	_nextRecordingNote = note;
}

string MepPackManager::GetProjectFallbackRoot() const
{
	return FolderUtilities::CombinePath(GetPacksFolder(), _romName);
}

bool MepPackManager::IsOwnProjectLayer(const MepPack* pack, MepSectionType type) const
{
	if(!pack) {
		return false;
	}
	//The project is the sibling, or EnhancementPacks/<Game>/ when the ROM
	//folder was read-only (matched by name, recorded by an earlier bootstrap)
	std::error_code ec;
	bool isProject = pack->Origin == MepPackOrigin::Sibling ||
		(pack->Origin == MepPackOrigin::Folder && StringUtilities::ToLower(pack->ContainerName) == StringUtilities::ToLower(_romName) &&
			fs::exists(fs::u8path(FolderUtilities::CombinePath(pack->RootFolder, ".bootstrap")), ec));
	const MepSection& section = pack->Sections[(int)type];
	return RemasterProject::IsOwnProjectSection(isProject, section.HasHuman, section.Path);
}

bool MepPackManager::StartRecording(const string& source, const string& note, bool onDemand)
{
	auto lock = _emu->AcquireLock();
	if(_bootstrapping) {
		Log("record: a recording is already in progress ('" + _recordingFolder + "') - stop it first");
		return false;
	}
	if(_romName.empty()) {
		Log("record: no ROM loaded - nothing to record");
		return false;
	}
	shared_ptr<IConsole> console = _emu->GetConsole();
	if(!console) {
		return false;
	}
	ConsoleType type = console->GetConsoleType();
	if(type != ConsoleType::Nes && type != ConsoleType::Gameboy && type != ConsoleType::Sms) {
		Log("record: this system has no HD pack builder - nothing to record");
		return false;
	}

	//ADR-0243 Decision 3 / #142: a foreign pack still declines its section;
	//the project's own mep/ and earlier recordings do not.
	const MepPack* texturesPack = GetPackForSection(MepSectionType::Textures);
	const MepPack* audioPack = GetPackForSection(MepSectionType::Audio);
	std::error_code ec;
	string existingManifest = FolderUtilities::CombinePath(FolderUtilities::CombinePath(FolderUtilities::GetHdPackFolder(), _romName), "hires.txt");
	bool loosePack = fs::exists(fs::u8path(existingManifest), ec);
	RemasterProject::RecordingPlan plan = RemasterProject::PlanRecording(
		{ texturesPack != nullptr, IsOwnProjectLayer(texturesPack, MepSectionType::Textures),
			texturesPack != nullptr && texturesPack->Sections[(int)MepSectionType::Textures].HasHuman }, loosePack,
		{ audioPack != nullptr, IsOwnProjectLayer(audioPack, MepSectionType::Audio) },
		type == ConsoleType::Nes, //audio fingerprints (ADR-0047) are NES-only (ADR-0041 scope)
		onDemand);
	string foreignTextures = loosePack ? existingManifest : (texturesPack ? texturesPack->ContainerName : "");
	//Play's automatic bootstrap kept the project's own mep/ art: not a foreign
	//pack, so "delete that pack" would be the wrong advice.
	bool keptOwnArt = !onDemand && !loosePack && texturesPack && IsOwnProjectLayer(texturesPack, MepSectionType::Textures) &&
		texturesPack->Sections[(int)MepSectionType::Textures].HasHuman;
	if(plan.Declined() && keptOwnArt) {
		Log("bootstrap: nothing was recorded - the automatic bootstrap keeps this project's own mep/ textures as they are "
			"(and another pack dresses its audio). Use Remaster's Record button to record on demand.");
		return false;
	}
	if(plan.Declined()) {
		//Declining is the point: this is a first draft, not an override of a pack
		//someone already has. Declining *silently* was not the point. The run
		//exits 0, writes no tile and leaves the pack's mtime alone, so a second
		//recording that recorded nothing is indistinguishable from one that
		//worked - and it was caught the expensive way, by a probe run whose
		//numbers were the previous run's (#229). A quiet wrong answer is the
		//failure mode this whole pipeline exists to remove; say it out loud.
		Log("bootstrap: nothing was recorded - '" + foreignTextures +
			"' already dresses this ROM, so the bootstrap kept it as it was. The pack is unchanged and this run still exits 0.");
		Log("bootstrap: to record a fresh one, delete that pack or record into an empty directory - the ROM's own "
			"project folder (its mep/ and earlier auto/rec-NNN/ recordings) never blocks a recording (ADR-0243).");
		return false;
	}

	//Project root: the sibling folder, or the central folder when the ROM's
	//directory is read-only. Only the sections that will be generated get a
	//folder (an empty textures/ next to an artist's pack would just confuse).
	string root = GetSiblingFolder();
	string autoRoot = FolderUtilities::CombinePath(root, MepPack::AutoFolderName);
	fs::create_directories(fs::u8path(autoRoot), ec);
	if(ec || !fs::is_directory(fs::u8path(autoRoot), ec)) {
		root = GetProjectFallbackRoot();
		autoRoot = FolderUtilities::CombinePath(root, MepPack::AutoFolderName);
		ec.clear();
		fs::create_directories(fs::u8path(autoRoot), ec);
		if(ec) {
			Log("bootstrap: cannot create '" + autoRoot + "' - skipped");
			return false;
		}
		Log("bootstrap: ROM folder is not writable - using '" + root + "' instead (matched by name on the next load)");
	}

	//ADR-0243 Q1: one complete output per recording, auto/rec-NNN/
	string recordingId = RemasterProject::NextRecordingId(RemasterProject::ListChildFolders(autoRoot));
	string recordingFolder = FolderUtilities::CombinePath(autoRoot, recordingId);
	fs::create_directories(fs::u8path(recordingFolder), ec);
	if(ec) {
		Log("bootstrap: cannot create '" + recordingFolder + "' - skipped");
		return false;
	}

	//Stamp: generator version + ROM identity (never touches anything outside auto/)
	{
		ofstream stamp(FolderUtilities::CombinePath(root, ".bootstrap"), std::ios::out | std::ios::binary);
		stamp << "generator=mesence-bootstrap/1\nsha1=" << _romSha1 << "\nrom=" << _romName << "\nfilter=xBRZ\nscale=4\n";
	}

	{
		//#699: the UI reads the recording folder; the console calls below wait
		//for the decoder, so the lock covers these writes only
		auto stateLock = _stateLock.AcquireSafe();
		_recordingProjectRoot = root;
		_recordingFolder = recordingFolder;
		_recordingRomName = _romName;
		_recordingEntry = RemasterProject::Recording{ recordingId, RemasterProject::FormatUtcTimestamp(std::time(nullptr)),
			RemasterProject::IsKnownSource(source) ? source : "play", 0, note };
		_recordingClock.Start(_emu->GetFrameCount());
		WriteRecordingEntry();
	}

	if(plan.NeedAudio) {
		string autoAudio = FolderUtilities::CombinePath(recordingFolder, MepPack::GetConventionPath(MepSectionType::Audio));
		fs::create_directories(fs::u8path(autoAudio), ec);
		if(NesConsole* nes = dynamic_cast<NesConsole*>(console.get())) {
			nes->StartAudioBootstrap(autoAudio);
			auto stateLock = _stateLock.AcquireSafe();
			_bootstrapping = true;
			Log("bootstrap: recording music fingerprints + MIDI into '" + autoAudio + "'");
		}
	}
	if(!plan.NeedTextures) {
		//Reached when the audio section was still missing and got recorded above.
		//The tile half is skipped for the same reason as the early return, and a
		//bootstrap that comes back with audio and no tiles has to say which half
		//it did - otherwise "the run finished" reads as "the recording is there".
		if(keptOwnArt) {
			Log("bootstrap: no tiles were recorded - the automatic bootstrap keeps this project's own mep/ textures as they are. "
				"Only the audio section was written by this run; use Remaster's Record button to record tiles on demand.");
		} else {
			Log("bootstrap: no tiles were recorded - '" + foreignTextures +
				"' already dresses this ROM. Only the audio section was written by this run; the textures are unchanged.");
		}
		return _bootstrapping;
	}

	string autoTextures = FolderUtilities::CombinePath(recordingFolder, MepPack::GetConventionPath(MepSectionType::Textures));
	fs::create_directories(fs::u8path(autoTextures), ec);
	if(ec) {
		Log("bootstrap: cannot create '" + autoTextures + "' - skipped");
		return _bootstrapping;
	}
	_bootstrapSaveFolder = autoTextures;
	HdPackBuilderOptions options = {};
	options.SaveFolder = (char*)_bootstrapSaveFolder.c_str();
	options.FilterType = ScaleFilterType::xBRZ;
	options.Scale = 4;
	options.ChrRamBankSize = 0x1000;
	options.UseLargeSprites = false;
	options.SortByUsageFrequency = true;
	options.GroupBlankTiles = true;
	options.IgnoreOverscan = true;

	//Static pass first (ADR-0043; refused for CHR RAM games), then record what
	//is actually drawn while playing - the builder merges both in the folder
	ExecuteShortcutParams exportParams = { EmulatorShortcut::ExportRomTilesHdPack, 0, &options };
	console->ProcessNotification(ConsoleNotificationType::ExecuteShortcut, &exportParams);
	ExecuteShortcutParams recordParams = { EmulatorShortcut::StartRecordHdPack, 0, &options };
	console->ProcessNotification(ConsoleNotificationType::ExecuteShortcut, &recordParams);
	if(NesConsole* nes = dynamic_cast<NesConsole*>(console.get())) {
		//Static screens as whole-frame <background> PNGs with tileAtPosition anchors (F5.4)
		nes->EnableBootstrapScreenCapture();
	}
	{
		auto stateLock = _stateLock.AcquireSafe();
		_bootstrapping = true;
	}
	Log("bootstrap: recording played tiles (xBRZ 4x) into '" + autoTextures + "' (" + recordingId + ") - the next load of this ROM plays with it");
	return true;
}

bool MepPackManager::StopRecording()
{
	auto lock = _emu->AcquireLock();
	if(!IsBootstrapping()) {
		return false;
	}
	if(shared_ptr<IConsole> console = _emu->GetConsole()) {
		//The builder writes hires.txt and its pages when it is released
		ExecuteShortcutParams stopParams = { EmulatorShortcut::StopRecordHdPack, 0, nullptr };
		console->ProcessNotification(ConsoleNotificationType::ExecuteShortcut, &stopParams);
		if(NesConsole* nes = dynamic_cast<NesConsole*>(console.get())) {
			nes->StopAudioBootstrap();
		}
	}
	FinishRecordingEntry();
	return true;
}

void MepPackManager::FinishRecordingEntry()
{
	auto lock = _stateLock.AcquireSafe();
	if(!_bootstrapping || _recordingEntry.Id.empty()) {
		_bootstrapping = false;
		return;
	}
	_recordingEntry.DurationSeconds = RemasterProject::DurationSecondsFor(_recordingClock.ElapsedFrames(_emu->GetFrameCount()), _emu->GetFps());
	WriteRecordingEntry();
	Log("bootstrap: " + _recordingEntry.Id + " finished (" + RemasterProject::FormatNumber(_recordingEntry.DurationSeconds) + " s emulated)");
	_bootstrapping = false;
	_recordingEntry = {};
}

void MepPackManager::WriteRecordingEntry()
{
	//ADR-0243 Q2: machine-written, read-modify-write so entries of recordings
	//this run did not make (and their notes) are kept as they were
	string path = FolderUtilities::CombinePath(_recordingProjectRoot, RemasterProject::ManifestFileName);
	RemasterProject::Manifest manifest;
	string text;
	if(ReadTextFile(path, text) && !RemasterProject::ParseManifest(text, manifest)) {
		Log("record: '" + path + "' is not valid JSON - left as it was, this recording is not listed in it");
		return;
	}
	if(manifest.Name.empty()) {
		manifest.Name = _recordingRomName;
	}
	RemasterProject::UpsertRecording(manifest, _recordingEntry);
	ofstream out(path, std::ios::out | std::ios::binary | std::ios::trunc);
	out << RemasterProject::FormatManifest(manifest);
}

void MepPackManager::Clear()
{
	//#694: a recording in progress is not closed here. A recording the user
	//never stopped ends with the ROM it was recording, and Emulator closes it
	//(FinishRecordingEntry) once the next ROM has actually loaded or the
	//emulator stops - a load that fails leaves it running with its game.
	auto lock = _stateLock.AcquireSafe();
	_romSha1.clear();
	_romFileSha1.clear();
	_romExtension.clear();
	_romName.clear();
	_romFolder.clear();
	_packs.clear();
	_rejected.clear();
	_packIdentityByContainer.clear();
	_optimisticContainers.clear();
	_texturesContainer.clear();
	_texturesIsOptimistic = false;
	_forcedPatch.ResetForLoad();
}

MepPackManager::RomState MepPackManager::SaveRomState() const
{
	auto lock = _stateLock.AcquireSafe();
	return RomState{ _romSha1, _romFileSha1, _romExtension, _romName, _romFolder, _packs, _rejected,
		_packIdentityByContainer, _optimisticContainers, _texturesContainer, _texturesIsOptimistic, _forcedPatch.Applied() };
}

void MepPackManager::RestoreRomState(RomState state)
{
	auto lock = _stateLock.AcquireSafe();
	_romSha1 = std::move(state.RomSha1);
	_romFileSha1 = std::move(state.RomFileSha1);
	_romExtension = std::move(state.RomExtension);
	_romName = std::move(state.RomName);
	_romFolder = std::move(state.RomFolder);
	_packs = std::move(state.Packs);
	_rejected = std::move(state.Rejected);
	_packIdentityByContainer = std::move(state.PackIdentityByContainer);
	_optimisticContainers = std::move(state.OptimisticContainers);
	_texturesContainer = std::move(state.TexturesContainer);
	_texturesIsOptimistic = state.TexturesIsOptimistic;
	_forcedPatch.RestoreApplied(std::move(state.ForcedPatch));
}

bool MepPackManager::AllowsForcedPatch() const
{
	auto lock = _stateLock.AcquireSafe();
	return _forcedPatch.Allows(_emu->GetSettings()->GetEnhancementPackConfig().ApplyPatchOnHashMismatch, _romSha1);
}

void MepPackManager::NoteForcedPatch(const string& patchFile)
{
	auto lock = _stateLock.AcquireSafe();
	_forcedPatch.NoteApplied(patchFile);
}

string MepPackManager::GetForcedPatch() const
{
	auto lock = _stateLock.AcquireSafe();
	return _forcedPatch.Applied();
}

bool MepPackManager::SuppressForcedPatch()
{
	auto lock = _stateLock.AcquireSafe();
	bool suppressed = _forcedPatch.SuppressFor(_romSha1);
	if(suppressed) {
		Log("forced patch '" + _forcedPatch.Applied() + "' suppressed for sha1 " + _romSha1 + " until the app quits (the player chose to reload without it)");
	}
	return suppressed;
}

string MepPackManager::GetSiblingFolder(VirtualFile& romFile)
{
	string romPath = romFile.GetFilePath();
	if(romPath.empty()) {
		return "";
	}
	string name = FolderUtilities::GetFilename(romPath, false);
	return FolderUtilities::CombinePath(FolderUtilities::GetFolderName(romPath), name);
}

string MepPackManager::SystemFromExtension(const string& lowerExt)
{
	if(lowerExt == ".nes" || lowerExt == ".fds" || lowerExt == ".unf" || lowerExt == ".unif" || lowerExt == ".nsf" || lowerExt == ".nsfe") {
		return "nes";
	} else if(lowerExt == ".gb") {
		return "gb";
	} else if(lowerExt == ".gbc" || lowerExt == ".gbx") {
		return "gbc";
	} else if(lowerExt == ".sms") {
		return "sms";
	} else if(lowerExt == ".gg") {
		return "gg";
	} else if(lowerExt == ".sg") {
		return "sg1000";
	} else if(lowerExt == ".sfc" || lowerExt == ".smc" || lowerExt == ".swc" || lowerExt == ".fig" || lowerExt == ".bs" || lowerExt == ".st") {
		return "snes";
	}
	return "";
}

string MepPackManager::JoinPresentSections(const MepPack& pack)
{
	string sections;
	for(int i = 0; i < kMepSectionCount; i++) {
		if(pack.Sections[i].Present) {
			sections += (sections.empty() ? "" : ",") + string(MepPack::GetSectionName((MepSectionType)i));
		}
	}
	return sections;
}

bool MepPackManager::LoadConventionPack(const string& rootFolder, const string& containerName, MepPackOrigin origin, const string& humanPrefix, MepPack& outPack)
{
	outPack = MepPack();
	outPack.RootFolder = rootFolder;
	outPack.ContainerName = containerName;
	outPack.Origin = origin;
	outPack.FromZip = origin == MepPackOrigin::Zip;
	if(!outPack.DetectConventionLayout(humanPrefix)) {
		return false;
	}
	outPack.Synthetic = true;
	outPack.SpecVersion = "1.1.0";
	outPack.Name = _romName;
	outPack.Version = "0.0.0";
	outPack.License = "unspecified";
	MepTarget target;
	target.System = SystemFromExtension(_romExtension);
	target.Sha1 = _romSha1;
	target.Name = _romName;
	outPack.Targets.push_back(target);
	return true;
}

bool MepPackManager::HasSiblingMepPack(const string& sibling) const
{
	std::error_code ec;
	if(!fs::is_directory(fs::u8path(FolderUtilities::CombinePath(sibling, "mep")), ec)) {
		return false;
	}
	MepPack layout;
	layout.RootFolder = sibling;
	if(layout.DetectConventionLayout("mep")) {
		for(int i = 0; i < kMepSectionCount; i++) {
			if(layout.Sections[i].HasHuman) {
				return true;
			}
		}
	}
	//A mep/pack.json alone also makes mep/ the human layer (metadata +
	//identity, even before the convention probes are populated)
	return (bool)ifstream(FolderUtilities::CombinePath(FolderUtilities::CombinePath(sibling, "mep"), "pack.json"));
}

void MepPackManager::ScanSiblingFolder()
{
	if(_romFolder.empty() || _romName.empty()) {
		return;
	}
	string sibling = FolderUtilities::CombinePath(_romFolder, _romName);
	std::error_code ec;
	if(!fs::is_directory(fs::u8path(sibling), ec)) {
		return;
	}

	MepPack pack;
	string error;
	string json;
	//ADR-0147: the sibling may root the human layer at mep/ (a sibling of
	//auto/, not a child). Detect mep/ first; a pack.json there is read for
	//metadata, and legacy (mep/-less) siblings keep the root layout.
	bool hasMep = HasSiblingMepPack(sibling);
	string humanPrefix = hasMep ? "mep" : "";
	string packJsonPath = hasMep
		? FolderUtilities::CombinePath(FolderUtilities::CombinePath(sibling, "mep"), "pack.json")
		: FolderUtilities::CombinePath(sibling, "pack.json");
	if(ReadTextFile(packJsonPath, json)) {
		//Explicit metadata beside the ROM: parsed for name/author/license/
		//patches, but location is identity - targets are not required to match
		if(!MepPack::Parse(json, pack, error)) {
			_rejected.push_back(_romName + "/ (sibling): " + error);
			Log("rejected sibling folder '" + sibling + "': " + error);
			return;
		}
		pack.RootFolder = sibling;
		pack.ContainerName = _romName;
		pack.Origin = MepPackOrigin::Sibling;
		//auto/ (and, under mep/, the human) layers still come from the
		//convention; the pack.json-declared sections are relative to the pack
		//root, so under mep/ they are re-rooted with the 'mep/' prefix
		MepPack layout;
		layout.RootFolder = sibling;
		if(layout.DetectConventionLayout(humanPrefix)) {
			for(int i = 0; i < kMepSectionCount; i++) {
				MepSection& ps = pack.Sections[i];
				const MepSection& ls = layout.Sections[i];
				//machine layer always comes from the convention (auto/...)
				if(!ls.AutoPath.empty()) {
					ps.Present = true;
					ps.AutoPath = ls.AutoPath;
				}
				if(humanPrefix.empty()) {
					//legacy: keep the pack.json-declared sections as-is
					continue;
				}
				//mep/ layout: a section the pack.json did not declare takes the
				//convention human layer; one it did declare is re-rooted to mep/
				if(ps.Path.empty()) {
					if(ls.HasHuman) {
						ps.Present = true;
						ps.HasHuman = true;
						ps.Path = ls.Path; //e.g. "mep/textures"
					}
				} else {
					//(a declared path never starts with '/': Parse already ran
					//NormalizeRelativePath on it)
					ps.Path = "mep/" + ps.Path;
				}
			}
		}
	} else if(!LoadConventionPack(sibling, _romName, MepPackOrigin::Sibling, humanPrefix, pack)) {
		Log("sibling folder '" + sibling + "' has no textures/, audio/ or synth/ layer - ignored");
		return;
	}
	_packs.push_back(std::move(pack));
}

void MepPackManager::LoadForRom(VirtualFile& romFile)
{
	//#699: the scan rewrites what the UI and decode threads read. Nothing here
	//waits for another thread, so holding the lock for the whole scan is safe.
	auto lock = _stateLock.AcquireSafe();
	Clear();
	if(!romFile.IsValid()) {
		return;
	}

	_romSha1 = ComputeNoIntroSha1(romFile);
	//ADR-0211: the installer needs the whole-file form too - an HD pack's
	//<supportedRom> is that hash, not the No-Intro body one.
	_romFileSha1 = romFile.GetSha1Hash();
	_romExtension = StringUtilities::ToLower(romFile.GetFileExtension());
	string romPath = romFile.GetFilePath();
	_romName = FolderUtilities::GetFilename(romPath, false);
	_romFolder = FolderUtilities::GetFolderName(romPath);
	if(!_emu->GetSettings()->GetEnhancementPackConfig().EnableMepPacks) {
		Log("enhancement packs disabled in settings - folder not scanned");
		return;
	}
	//ADR-0049: the folder beside the ROM always comes first
	ScanSiblingFolder();
	ScanAndMatch();

	//P.3: fill each pack's identity (pack_id/content_id) from its
	//.mep-install.json stamp, for the preferred-pack lookup below and the
	//pack-list columns the UI resolver reads.
	//P.1-local (ADR-0206 §3): the local-identity cache is read once here and
	//answers for the containers that have no stamp - the file is small and no
	//tree is walked or hashed on this path.
	MepLocalIdentityCache localIdentityCache;
	localIdentityCache.Load(MepLocalIdentityCache::GetCacheFilePath(GetPacksFolder()));
	for(MepPack& pack : _packs) {
		ReadInstallIdentity(pack, localIdentityCache);
	}
	AdoptEqualContentIds();

	//ADR-0145: snapshot the winning textures pack for the renderer's health
	//signal. Taken here on the emulation thread (after ScanAndMatch and
	//ReadInstallIdentity, so the resolution matches what the consoles see when
	//they call GetSectionPath during LoadRom) - the decode thread's
	//HandleLowTextureMatchRate must never read _packs directly.
	_texturesContainer.clear();
	_texturesIsOptimistic = false;
	if(const MepPack* texturesPack = GetPackForSection(MepSectionType::Textures)) {
		_texturesContainer = texturesPack->ContainerName;
		_texturesIsOptimistic = IsOptimistic(*texturesPack);
	}

	if(!_packs.empty()) {
		for(const MepPack& pack : _packs) {
			string sections = JoinPresentSections(pack);
			string origin = pack.Origin == MepPackOrigin::Sibling ? "sibling folder" : pack.FromZip ? "zip" :
																																	"folder";
			string matchNote = IsOptimistic(pack) ? " does not match ROM sha1 " + _romSha1 + " (optimistic, ADR-0145 - textures/BPS may still apply)" : " matches ROM sha1 " + _romSha1;
			Log("pack '" + pack.Name + "' v" + pack.Version + (pack.Synthetic ? " [folder convention]" : "") + matchNote + " (" + origin + " '" + pack.ContainerName + "', sections: " + sections + (IsPackEnabled(pack.ContainerName) ? ")" : ") - disabled by user"));
		}
		if(IsNoPackPreference(PreferredIdForRom())) {
			Log("\"No pack\" is chosen for this game - only a sibling-folder pack applies");
		}
		uint8_t layersOff = RomLayersOff();
		if(layersOff != 0) {
			Log(string("turned off for this game:") + (IsLayerOff(layersOff, MepRomLayer::Textures) ? " textures" : "") +
				(IsLayerOff(layersOff, MepRomLayer::Audio) ? " audio" : "") + (IsLayerOff(layersOff, MepRomLayer::Patch) ? " ROM patch" : ""));
		}
	}
}

bool MepPackManager::ReadTextFile(const string& path, string& out)
{
	return MepFileIo::ReadWholeFile(path, out);
}

void MepPackManager::ReadInstallIdentity(MepPack& pack, const MepLocalIdentityCache& cache)
{
	//P.3: the .mep-install.json a MEP Recipe install writes at the container
	//root (MepRecipeInstaller::WriteInstallStamp) carries the ADR-0140 pack_id
	//and ADR-0139 content_id. Missing/malformed stamp -> the P.1-local cache
	//answers instead when it has an entry whose container stamp still matches;
	//with neither, the identity stays empty and the UI derives
	//`local:<container>` (PRD §5).
	MepPackIdentity identity;
	string text;
	if(ReadTextFile(InstallStampPath(pack.RootFolder, pack.Origin), text)) {
		JsonValue root;
		JsonReader reader;
		if(reader.Parse(text, root) && root.IsObject()) {
			identity.PackId = root.GetString("pack_id");
			identity.ContentId = root.GetString("content_id");
		}
	}
	if(identity.ContentId.empty()) {
		const MepLocalIdentityCache::Entry* entry = cache.Find(pack.RootFolder);
		if(entry != nullptr && !entry->ContainerStamp.empty() &&
			entry->ContainerStamp == MepLocalIdentityCache::ComputeContainerStamp(pack.RootFolder)) {
			identity.ContentId = entry->ContentId;
		}
	}
	_packIdentityByContainer[StringUtilities::ToLower(pack.ContainerName)] = std::move(identity);
}

void MepPackManager::AdoptEqualContentIds()
{
	//ADR-0206 §4, applied to what this load discovered (the rule itself lives
	//in the header so the unit tests can drive it without a manager).
	vector<string> containers;
	containers.reserve(_packs.size());
	for(const MepPack& pack : _packs) {
		containers.push_back(StringUtilities::ToLower(pack.ContainerName));
	}
	for(const auto& adopted : AdoptIdentities(containers, _packIdentityByContainer)) {
		Log("local pack '" + adopted.first + "' shares its content with a stamped container - adopting pack_id '" + adopted.second + "'");
	}
}

MepLocalIdentityCache::RefreshResult MepPackManager::RefreshLocalIdentityCache()
{
	//ADR-0206 §3/§6: the byte-reading half, off the ROM-load path. The walk
	//itself lives in the cache module (which the unit tests link, and which
	//takes the folder as a parameter) and reads the packs folder rather than
	//_packs, so it shares no state with the emulation thread and needs no lock.
	MepLocalIdentityCache::RefreshResult result = MepLocalIdentityCache::RefreshFolder(GetPacksFolder());
	Log("local pack identity cache refreshed: " + std::to_string(result.Scanned) + " container(s) scanned, " + std::to_string(result.Recomputed) + " recomputed, " + std::to_string(result.Pruned) + " pruned");
	return result;
}

string MepPackManager::EffectivePackId(const MepPack& pack) const
{
	//P.3: a stamped pack_id wins; a stamp-less container is the ADR-0140 rule-4
	//`local:<container>` fallback. Lower-cased so the preference comparison is
	//case-insensitive on both sides.
	auto it = _packIdentityByContainer.find(StringUtilities::ToLower(pack.ContainerName));
	if(it != _packIdentityByContainer.end() && !it->second.PackId.empty()) {
		return StringUtilities::ToLower(it->second.PackId);
	}
	return "local:" + StringUtilities::ToLower(pack.ContainerName);
}

bool MepPackManager::PrepareZip(const string& zipPath, const string& cacheRoot, string& outFolder, string& error)
{
	//ADR-0120 §4: the pipeline itself (cache stamp, zip-slip validation, the
	//root/fallback-subfolder decision, extraction) lives in MepZipExtract so
	//scripts/core_unit_tests.cpp can drive it against real archive bytes.
	MinizZipArchive archive;
	return MepZipExtract::PrepareZip(archive, zipPath, cacheRoot, _romName, outFolder, error);
}

bool MepPackManager::LoadContainer(const string& rootFolder, const string& containerName, bool fromZip, MepPack& outPack, string& error)
{
	string json;
	if(!ReadTextFile(FolderUtilities::CombinePath(rootFolder, "pack.json"), json)) {
		error = "no pack.json";
		return false;
	}
	if(!MepPack::Parse(json, outPack, error)) {
		return false;
	}
	outPack.ContainerName = containerName;
	outPack.RootFolder = rootFolder;
	outPack.FromZip = fromZip;
	outPack.Origin = fromZip ? MepPackOrigin::Zip : MepPackOrigin::Folder;
	return true;
}

void MepPackManager::ScanAndMatch()
{
	string packsFolder = GetPacksFolder();
	string cacheRoot = FolderUtilities::CombinePath(packsFolder, CacheFolderName);

	std::error_code ec;
	struct Candidate
	{
		string key; //lower-cased container name (precedence key, ADR-0040)
		MepPack pack;
		bool exactSha1Match;
	};
	vector<Candidate> candidates;
	for(fs::directory_iterator it(fs::u8path(packsFolder), ec), end; !ec && it != end; it.increment(ec)) {
		const fs::directory_entry& entry = *it;
		string name = entry.path().filename().u8string();
		if(name.empty() || name[0] == '.') {
			continue; //includes .cache
		}

		string rootFolder;
		bool fromZip = false;
		string error;
		if(entry.is_directory(ec)) {
			rootFolder = entry.path().u8string();
		} else if(entry.is_regular_file(ec) && StringUtilities::ToLower(FolderUtilities::GetExtension(name)) == ".zip") {
			fromZip = true;
			if(!PrepareZip(entry.path().u8string(), cacheRoot, rootFolder, error)) {
				_rejected.push_back(name + ": " + error);
				Log("rejected '" + name + "': " + error);
				continue;
			}
			name = FolderUtilities::GetFilename(name, false);
		} else {
			continue;
		}

		MepPack pack;
		if(!LoadContainer(rootFolder, name, fromZip, pack, error)) {
			if(error == "no pack.json") {
				//ADR-0049: a container named like the ROM is "the folder, zipped"
				//(or the fallback location when the ROM's folder is read-only).
				//ADR-0120: when PrepareZip resolved a fallback subfolder,
				//rootFolder's own last segment - not the zip's file name in
				//"name" - is the one guaranteed to match the ROM, so the gate
				//has to look there for the recovery to ever be reachable.
				//#695: the fallback folder is a Remaster project like the sibling
				//(ADR-0147), so its human layer may live under mep/ - probed the
				//same way ScanSiblingFolder does, or Build's output never plays.
				string rootLeaf = FolderUtilities::GetFilename(rootFolder, true);
				bool nameMatches = StringUtilities::ToLower(rootLeaf) == StringUtilities::ToLower(_romName);
				string humanPrefix = nameMatches && HasSiblingMepPack(rootFolder) ? "mep" : "";
				if(nameMatches && LoadConventionPack(rootFolder, name, fromZip ? MepPackOrigin::Zip : MepPackOrigin::Folder, humanPrefix, pack)) {
					//ADR-0145: a name-matched convention pack is a definite
					//match (its target *is* the current ROM), never optimistic
					candidates.push_back({ StringUtilities::ToLower(name), std::move(pack), true });
				} else if(fromZip) {
					_rejected.push_back(name + ": " + error);
					Log("rejected '" + name + "': " + error);
				}
			} else {
				_rejected.push_back(name + ": " + error);
				Log("rejected '" + name + "': " + error);
			}
			continue;
		}

		//ADR-0145: no mandatory exact SHA1 match. A pack that matches stays
		//ahead; one that does not is kept as an *optimistic* candidate so its
		//textures (and self-validating BPS patches) can still apply to a
		//near-matching ROM (same game, different dump/revision). The bg-tile
		//match-rate health signal auto-disables a wrong-game pack.
		bool exactMatch = pack.MatchesSha1(_romSha1);
		if(!exactMatch) {
			_optimisticContainers.insert(StringUtilities::ToLower(name));
		}
		candidates.push_back({ StringUtilities::ToLower(name), std::move(pack), exactMatch });
	}

	//Deterministic precedence: exact SHA1 matches first (ADR-0145), then
	//case-insensitive lexicographic container name (ADR-0040); ties (same
	//lower-cased name) fall back to the exact name
	std::sort(candidates.begin(), candidates.end(), [](const Candidate& a, const Candidate& b) {
		if(a.exactSha1Match != b.exactSha1Match) {
			return a.exactSha1Match; //exact matches win over optimistic ones
		}
		if(a.key != b.key) {
			return a.key < b.key;
		}
		return a.pack.ContainerName < b.pack.ContainerName;
	});

	for(auto& candidate : candidates) {
		_packs.push_back(std::move(candidate.pack));
	}
}

void MepPackManager::SetPackEnabled(const string& containerName, bool enabled)
{
	string key = StringUtilities::ToLower(containerName);
	auto lock = _stateLock.AcquireSafe();
	if(enabled) {
		_disabledContainers.erase(key);
	} else {
		_disabledContainers.insert(key);
	}
}

bool MepPackManager::IsPackEnabled(const string& containerName) const
{
	string key = StringUtilities::ToLower(containerName);
	auto lock = _stateLock.AcquireSafe();
	return _disabledContainers.find(key) == _disabledContainers.end();
}

bool MepPackManager::IsOptimistic(const MepPack& pack) const
{
	return _optimisticContainers.find(StringUtilities::ToLower(pack.ContainerName)) != _optimisticContainers.end();
}

const MepPatch* MepPackManager::FindFirstBpsPatch(const MepPack& pack) const
{
	for(const MepPatch& patch : pack.Patches) {
		string path = FolderUtilities::CombinePath(pack.RootFolder, patch.File);
		if(IsBpsPatchFile(path)) {
			return &patch;
		}
	}
	return nullptr;
}

void MepPackManager::HandleLowTextureMatchRate()
{
	//ADR-0145: the HD renderer reports a sustained low bg-tile match rate.
	//Only act when the winning textures pack was applied *optimistically*
	//(SHA1 mismatch): a low rate there is a wrong-game signal, and disabling
	//it makes the next load stop trying it. An exact-match pack with low
	//coverage is legitimate (partial packs) and must NOT be auto-disabled.
	//Uses the load-time snapshot - this runs on the decode thread, so it must
	//not touch _packs/_optimisticContainers (those are written on the
	//emulation thread by LoadForRom). The snapshot already reflects the winning
	//textures pack as resolved enabled at load time.
	string container;
	{
		auto lock = _stateLock.AcquireSafe();
		if(_texturesContainer.empty() || !_texturesIsOptimistic) {
			return;
		}
		container = _texturesContainer;
	}
	SetPackEnabled(container, false);
	MessageManager::DisplayMessage("MEP", "Textures auto-disabled: the pack does not match this game's tiles (applied without an exact SHA1 match)");
	Log("textures pack '" + container + "' auto-disabled: bg-tile match rate stayed low (optimistic SHA1-mismatch apply, ADR-0145) - re-enable from the pack list if this is a false positive");
}

void MepPackManager::SetPreferredMepPack(const string& romSha1, const string& packId)
{
	//P.3: per-ROM preferred pack_id pushed by the UI at config-apply time.
	//An empty packId removes the entry; the key stays the ROM's No-Intro sha1
	//so GetPackForSection can look it up per loaded ROM. Stale keys (a pack
	//removed) are harmless: FindPreferredPack just finds no match.
	string sha1 = StringUtilities::Trim(romSha1);
	string id = StringUtilities::Trim(packId);
	if(sha1.empty()) {
		return;
	}
	auto lock = _stateLock.AcquireSafe();
	if(id.empty()) {
		_preferredPackIdByRomSha1.erase(sha1);
	} else {
		_preferredPackIdByRomSha1[sha1] = StringUtilities::ToLower(id);
	}
}

void MepPackManager::ClearPreferredMepPacks()
{
	auto lock = _stateLock.AcquireSafe();
	_preferredPackIdByRomSha1.clear();
	_romLayersOffBySha1.clear();
}

void MepPackManager::SetRomLayersOff(const string& romSha1, const string& layers)
{
	//W-P6: keyed like the preference (the No-Intro sha1 of the ROM as loaded).
	string sha1 = StringUtilities::Trim(romSha1);
	if(sha1.empty()) {
		return;
	}
	uint8_t mask = ParseRomLayersOff(layers);
	auto lock = _stateLock.AcquireSafe();
	if(mask == 0) {
		_romLayersOffBySha1.erase(sha1);
	} else {
		_romLayersOffBySha1[sha1] = mask;
	}
}

uint8_t MepPackManager::RomLayersOff() const
{
	auto it = _romLayersOffBySha1.find(_romSha1);
	return it == _romLayersOffBySha1.end() ? 0 : it->second;
}

bool MepPackManager::IsRomLayerOff(MepRomLayer layer) const
{
	auto lock = _stateLock.AcquireSafe();
	return IsLayerOff(RomLayersOff(), layer);
}

string MepPackManager::PreferredIdForRom() const
{
	auto it = _preferredPackIdByRomSha1.find(_romSha1);
	return it == _preferredPackIdByRomSha1.end() ? "" : it->second;
}

const MepPack* MepPackManager::FindPreferredPack(MepSectionType type) const
{
	string preferredId = PreferredIdForRom();
	//W-P5's "No pack" names no pack, even one whose stamp claims the sentinel.
	if(preferredId.empty() || IsNoPackPreference(preferredId)) {
		return nullptr;
	}
	for(const MepPack& pack : _packs) {
		if(pack.HasSection(type) && IsPackEnabled(pack.ContainerName) && EffectivePackId(pack) == preferredId) {
			//ADR-0145: an optimistic pack may serve Textures, but Audio/Synth
			//still require an exact match (out of the ADR's scope)
			if(type != MepSectionType::Textures && IsOptimistic(pack)) {
				continue;
			}
			return &pack;
		}
	}
	return nullptr;
}

const MepPack* MepPackManager::GetPackForSection(MepSectionType type) const
{
	EnhancementPackConfig& cfg = _emu->GetSettings()->GetEnhancementPackConfig();
	if(!SectionSwitchEnabled(cfg, type)) {
		return nullptr;
	}
	auto lock = _stateLock.AcquireSafe();
	//W-P6: the player turned this layer off for this game.
	if(SectionOff(RomLayersOff(), type)) {
		return nullptr;
	}
	//P.3: the per-ROM preference overrides the ADR-0040 lexicographic order;
	//the default stays "first enabled pack in precedence order" when there is
	//no stored preference or the preferred pack_id does not match a candidate.
	if(const MepPack* preferred = FindPreferredPack(type)) {
		return preferred;
	}

	//W-P5's "No pack": every pack is off for this ROM but a sibling folder.
	string preferredId = PreferredIdForRom();
	const MepPack* autoOnlyFallback = nullptr;
	for(const MepPack& pack : _packs) {
		if(IsPackEnabled(pack.ContainerName) && pack.HasSection(type) && PreferenceAllowsPack(preferredId, pack.Origin)) {
			//ADR-0145: an optimistic candidate is only eligible for textures -
			//HdNesPack falls through per-tile without crashing, and the health signal
			//auto-disables a wrong-game pack. Audio/Synth stay gated on an
			//exact match (out of the ADR's scope: offsets/timing per-ROM).
			if(type != MepSectionType::Textures && IsOptimistic(pack)) {
				continue;
			}
			if(pack.Sections[(int)type].HasHuman) {
				return &pack;
			}
			if(!autoOnlyFallback) {
				autoOnlyFallback = &pack;
			}
		}
	}
	return autoOnlyFallback;
}

string MepPackManager::GetPackListText() const
{
	auto lock = _stateLock.AcquireSafe();
	string out;
	for(const MepPack& pack : _packs) {
		string sections = JoinPresentSections(pack);
		MepPackIdentity identity;
		auto identityIt = _packIdentityByContainer.find(StringUtilities::ToLower(pack.ContainerName));
		if(identityIt != _packIdentityByContainer.end()) {
			identity = identityIt->second;
		}
		//Column 11 (isAutoOnly): "1" when the pack is a sibling whose content is
		//entirely under auto/ (no HasHuman on any section) - the F5 bootstrap's
		//machine-layer-only output. The UI uses this to skip the auto-only sibling
		//when applying the §4 sibling-suppresses-picker rule (issue #150).
		bool isAutoOnly = (pack.Origin == MepPackOrigin::Sibling);
		if(isAutoOnly) {
			for(int i = 0; i < kMepSectionCount; i++) {
				if(pack.Sections[i].HasHuman) {
					isAutoOnly = false;
					break;
				}
			}
		}
		out += pack.ContainerName + "\t" + pack.Name + "\t" + pack.Version + "\t" + pack.Author + "\t" + pack.License + "\t" + sections + "\t" + (IsPackEnabled(pack.ContainerName) ? "1" : "0") + "\t" + std::to_string((int)pack.Origin) + "\t" + identity.PackId + "\t" + identity.ContentId + "\t" + (isAutoOnly ? "1" : "0") + "\n";
	}
	for(const string& rejected : _rejected) {
		out += "!" + rejected + "\n";
	}
	return out;
}

string MepPackManager::GetSectionPath(MepSectionType type) const
{
	auto lock = _stateLock.AcquireSafe();
	const MepPack* pack = GetPackForSection(type);
	return pack ? pack->GetSectionPath(type) : "";
}

string MepPackManager::GetSectionAutoPath(MepSectionType type) const
{
	auto lock = _stateLock.AcquireSafe();
	const MepPack* pack = GetPackForSection(type);
	return pack ? pack->GetSectionAutoPath(type) : "";
}

vector<string> MepPackManager::GetSynthPresetPaths() const
{
	auto lock = _stateLock.AcquireSafe(); //both layers from one resolution (#699)
	vector<string> paths;
	string autoPath = GetSectionAutoPath(MepSectionType::Synth);
	string humanPath = GetSectionPath(MepSectionType::Synth);
	if(!autoPath.empty()) {
		paths.push_back(autoPath);
	}
	if(!humanPath.empty()) {
		paths.push_back(humanPath);
	}
	return paths;
}

bool MepPackManager::IsSectionFromSibling(MepSectionType type) const
{
	auto lock = _stateLock.AcquireSafe();
	const MepPack* pack = GetPackForSection(type);
	return pack && pack->Origin == MepPackOrigin::Sibling;
}

bool MepPackManager::ApplyPatches(VirtualFile& romFile)
{
	EnhancementPackConfig& cfg = _emu->GetSettings()->GetEnhancementPackConfig();
	if(!cfg.EnableMepPacks || !cfg.EnablePatches) {
		return false;
	}
	auto lock = _stateLock.AcquireSafe();
	if(IsLayerOff(RomLayersOff(), MepRomLayer::Patch)) {
		Log("ROM patch turned off for this game - no pack patch applied");
		return false;
	}
	//W-P5's "No pack" plays the original game: no pack's ROM patch either.
	string preferredId = PreferredIdForRom();
	for(const MepPack& pack : _packs) {
		if(pack.Patches.empty() || !IsPackEnabled(pack.ContainerName)) {
			continue;
		}
		if(!PreferenceAllowsPack(preferredId, pack.Origin)) {
			Log("pack '" + pack.Name + "': patch skipped - \"No pack\" is chosen for this game");
			continue;
		}
		const MepPatch* patch = pack.FindPatch(_romSha1);
		bool forced = false;
		if(!patch) {
			//ADR-0145: relax the exact-match gate by format. A self-validating
			//BPS patch (embedded source+output CRC32, refuses bad applies) is
			//safe to attempt on a SHA1 mismatch - a near-matching ROM (same
			//game, different dump/revision) can take it. IPS/UPS have no such
			//validation and stay gated on an exact match, unless the user has
			//explicitly opted into the ApplyPatchOnHashMismatch override.
			const MepPatch* bpsPatch = FindFirstBpsPatch(pack);
			if(bpsPatch) {
				patch = bpsPatch;
				Log("pack '" + pack.Name + "': no patch for sha1 " + _romSha1 + " - attempting self-validating BPS patch '" + patch->File + "' optimistically (ADR-0145)");
			} else if(AllowsForcedPatch()) {
				patch = &pack.Patches[0];
				forced = true;
				MessageManager::DisplayMessage("MEP", "Applying patch made for another ROM revision (hash override enabled)");
				Log("pack '" + pack.Name + "': no patch for sha1 " + _romSha1 + " - applying '" + patch->File + "' anyway (ApplyPatchOnHashMismatch)");
			} else {
				Log("pack '" + pack.Name + "': patch skipped - none of its " + std::to_string(pack.Patches.size()) + " patches[] entries matches sha1 " + _romSha1 + " and no self-validating BPS patch to fall back to" +
					(_emu->GetSettings()->GetEnhancementPackConfig().ApplyPatchOnHashMismatch ? " (the forced patch is off for this ROM: the player reloaded without it)" : ""));
				continue;
			}
		}
		VirtualFile patchFile(FolderUtilities::CombinePath(pack.RootFolder, patch->File));
		if(!patchFile.IsValid()) {
			Log("pack '" + pack.Name + "': patch file not found: " + patch->File);
			continue;
		}
		if(romFile.ApplyPatch(patchFile)) {
			Log("pack '" + pack.Name + "': applied patch '" + patch->File + "'");
			if(forced) {
				_forcedPatch.NoteApplied(patch->File);
			}
			return true;
		}
		Log("pack '" + pack.Name + "': failed to apply patch '" + patch->File + "'");
	}
	return false;
}
