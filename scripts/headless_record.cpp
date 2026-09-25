//Headless music-capture harness (F1 regression tool).
//Links directly against the freshly-built core dylib and includes the real
//SettingTypes.h, so config structs are passed with the exact ABI layout the
//core was compiled with - any drift fails at compile time instead of
//corrupting memory at run time.
//
//Build:   make capture-tool
//Usage:   scripts/headless_record <rom> <seconds> <output_prefix> [pal] [hdpack] [screenshot] [log] [hdpack-off|mep-off|mep-notextures|mep-nosynth|mep-disable=<container>] [romtiles] [filter=<name>] [live=<ms>]
//         [state=<file.mss>] [save-state=<file.mss>] [input=<script>] [movie=<file.bk2|file.mmo>] [cheat=AAAA:VV[:CC]]
//
//ADR-0184: "cheat=" takes a RAM-address code only - the AAAA:VV[:CC] form with
//AAAA below $0800. A Game Genie letter code is refused, because it is a PRG
//patch by construction and a game that unpacks its tiles out of PRG (Contra,
//Zelda 1) would then record altered bytes as if they were its own art. A run
//that carries a cheat feeds only the background surfaces of an artist kit.
//
//F9.22: "state=" starts the run from a save state, "save-state=" writes one
//when the run reaches its frame target (never on an INCOMPLETE run). Together
//they chain: entry script -> stage-1 state -> a <= 60 s run per stage, each
//into its own pack folder (scripts/record_stages.sh).
//
//"movie=" replays a recorded movie through the Core's own movie player instead
//of running an input script, so a pack can be recorded off a real playthrough
//rather than a blind scripted one. It is mutually exclusive with "input=" (two
//players fighting over the same pad) and with "state=" (a movie carries its own
//start state and power cycles the console itself). The Core recognises exactly
//two containers, by content and not by extension (MovieManager::Play): a zip
//holding "Input Log.txt" is a BizHawk .bk2, one holding "GameSettings.txt" is a
//Mesen .mmo. There is no .fm2 reader. Anything else is dropped without a word -
//no player, no message - which is why the run refuses to continue when
//MoviePlaying() reads false right after MoviePlay(): a silent refusal otherwise
//records the title screen and calls it a success.
//A .mmo also *overwrites* the emulation-affecting settings this tool pushes
//before LoadRom with the movie's own (MesenMovie::ApplySettings streams
//EmuSettings::Serialize's subset: controller types, RamPowerOnState, Region,
//console type and the per-console quirk flags), restoring them when it stops;
//the palette, channel volumes, EmulationSpeed, video filter and the HD/MEP-pack
//flags are outside that subset and survive. BizHawkMovie::ApplySettings is a
//stub returning true, so a .bk2 clobbers nothing.
//
//ADR-0185 sec. 4 as amended 2026-09-14 (issue #201): every "hdpack" run writes
//<output_prefix>-synctrace.csv - one row per emulated second with the builder's
//coverage counters, whether the movie was still playing and one byte per
//declared watch - so a movie-less run mints the baseline the next movie-driven
//run is judged against for free. On a movie-driven run the gate then reads:
//  sync-watch=AAAA:<rule>[=<n>][:<label>]  a RAM invariant the movie's
//      playthrough keeps (never-decreases|never-increases|never-below=<n>|
//      never-equals=<n>), checked only while the movie drives the pad. Address
//      below $0800 (ADR-0184). THE ONLY RULE THAT FAILS THE RUN: a life lost
//      under movie control is the cheapest decisive evidence that the run
//      stopped being the playthrough the movie describes. Repeatable.
//  sync-baseline=<trace.csv>  the movie-less run's trace.
//  sync-movie-frames=<n>  the frame the movie's own row count says its input
//      runs dry on - rows + 2 for a .bk2.
//  sync-sample=<frames>  sampling interval, default 60 (one emulated second).
//A failed gate is a non-zero exit, exactly like an incomplete capture: the
//recording is not short, it is a different playthrough. The rules live in
//Core/Shared/MovieSyncGate.h, which is also where the reasoning is.
//
//F9.14 (ADR-0157): a run is a number of *emulated frames*, never a number of
//host seconds. <seconds> keeps its name and its meaning for the caller, but is
//converted to a frame count here, at the region's nominal frame rate, and the
//run stops when the core's own frame counter reaches it - both ends of the
//recording are decided from inside the frame (HeadlessInputProvider), so two
//runs of the same ROM, script and binary cover exactly the same frames on any
//host load. The frame limiter is therefore pointless and is turned off
//(EmulationSpeed 0): speed is a free variable that no longer changes what a
//recording contains. Pass "realtime" to keep it on.
//
//<seconds> is *emulated* seconds, and it is the argument, not a frame count -
//passing a frame count by mistake asks for hours of emulation. Measured on an
//M-series host, 2026-09-07: 600 emulated seconds (36060 frames) take 59s of
//wall clock plain and 173s with "bootstrap", i.e. the builder costs about 3x
//the emulator. Budget a validation recording at <= 2 minutes of wall clock -
//roughly 400 emulated seconds with "bootstrap", 1200 without. A recording
//that runs past 5 minutes is a mistake in the invocation, not a slow tool:
//check the unit of <seconds> first.
//
//Default mode writes <output_prefix>.mid and <output_prefix>.vgm from the
//ROM's first N seconds of audio (power-on attract/title music - no input is
//ever fed). With the "hdpack" flag it records an HD pack skeleton instead
//(tiles seen during those N seconds), written to <output_prefix>-hdpack/
//via the StartRecordHdPack shortcut (NES, GB and SMS/GG - F2 validation).
//With the "screenshot" flag it runs N seconds and saves the final frame to
//<home>/Screenshots/ - installing a recorded pack into <home>/HdPacks/<rom>/
//between two runs gives a with/without-replacement pair to diff (F2.3).
//With "filter=<name>" the video filter used by the screenshot pipeline is
//selected (none, hq2x, hq3x, hq4x, scale2x/3x/4x, xbrz2x..6x, prescale2x/3x/
//4x/6x/8x/10x); the default is "none", i.e. a 1:1 native-resolution frame.
//A scaling filter multiplies the PNG dimensions by its scale factor - this is
//what scripts/check_hq4x_screenshot.sh asserts for HQ4x (P.7).
//With the "capture" flag the final frame is pulled into this process' memory
//(HeadlessCaptureFrame/HeadlessReadCapturedPixels, F9.15) instead of - or as
//well as - being written to a PNG, and its dimensions, frame number, FNV-1a
//checksum and uniform border bands are printed. That is what turns a check a
//human used to make by opening a screenshot (letterboxing, a card on screen)
//into a line a shell script can assert on.
//With the "log" flag the core message log is dumped to stdout at the end
//(used by the F3 MEP tests to check "[MEP] ..." matching/rejection lines;
//the MEP folder is <home>/EnhancementPacks/ inside the scratch home).
//The mep-* flags exercise EnhancementPackConfig / SetMepPackEnabled (F3.3).
//"romtiles" runs the static ROM tile export (ExportRomTilesHdPack) into
//<output_prefix>-hdpack/ instead of recording.
//With "live=<ms>" the run publishes itself while it plays (ADR-0169), into a
//scratch folder named <output_prefix>-live/ that is never part of the pack:
//frame.ppm (the composed frame, P6), status.json (frame, target, wall clock)
//and - on a NES run - the sprite layer as data: sprites.json (per-capture OAM,
//palette and the $2000 sprite-control bits from a direct console read, taken
//with the emulation thread held at an end-of-frame boundary) plus chr.bin (the
//mapper-resolved pattern tables) and one palette.json written at startup with
//the exact RGB the run renders with. The viewer that draws these
//is scripts/record_viewer.py. Publishing is lossy-latest on purpose (a slow
//viewer must skip, never block the run) and off by default; if the folder
//cannot be written the run logs one line and continues.
//With "cdl=<file.cdl>" the run also emits the Code/Data Logger state: the map
//of which ROM bytes the run executed as code and which it read as data. This
//needs the emulator's Debugger attached (the CDL feeders live in NesDebugger/
//GbDebugger/SmsDebugger and only run while one exists), so the flag calls
//InitializeDebugger before the run - see the "cdl=" block in main(). An
//existing file at <file.cdl> is loaded first, so coverage accumulates across
//runs as a union, never a maximum. The run fails (non-zero exit, and "the CDL
//was not written" in the "result:" line) if the CDL came back with zero code
//bytes, or if the file is not on disk with the recorded map in it after the
//write - a path the tool cannot write is a failed run, not a warning, because
//the caller's whole reason for the flag is the trace (issue #347).
//One consequence: HeadlessInputEngine refuses to park the run from inside the
//frame while a debugger is attached (Emulator::Pause() would step the debugger
//from the emulation thread, which deadlocks on DebugBreakHelper), and logs
//"the debugger is attached - not pausing". A "cdl=" run therefore stops from
//this thread instead, the instant the core's frame counter reaches the target
//- the same counter, one poll later. It lands on the target frame or the one
//after it, exactly like a plain run, but that is a poll and not the in-frame
//guarantee of ADR-0157: read the "capture finished" line for the frame the run
//actually ended on rather than assuming it.
//A scratch home folder is created next to the output; the NES game database
//is copied into it from the checkout this binary was built in, found from the
//binary's own path and never from the cwd (issue #477).
#include "Core/Shared/SettingTypes.h"
#include "Core/Shared/Video/FrameCapture.h"
//ADR-0169: the live sprite layer is published as data - OAM, palette and the
//mapper-resolved pattern tables are read off the console by the wrapper export
//HeadlessCaptureNesSpriteLayer, and the $2000 sprite-control bits come back in
//a NesPpuState. NesTypes.h keeps the ABI the exact one the core was built with.
#include "NES/NesTypes.h"
//ADR-0185 sec. 4 as amended 2026-09-14 (issue #201): the desync gate's rules
//live in Core/Shared/MovieSyncGate.{h,cpp} - host-free, no Emulator, no
//filesystem - so this file only samples the trace and reports the verdict.
//Read that header before changing anything below; it says which of the three
//rules can be trusted to fail a run and which one only names a window.
#include "Shared/MovieSyncGate.h"
#include "Utilities/LiveRecordFormat.h"
//"cdl=" - the CDL exports take the Core's own MemoryType enum; that header is
//a bare enum with no further dependency, so it is included rather than mirrored.
#include "Core/Shared/MemoryType.h"
//"cdl=" - issue #347: the proof that the map reached disk. Host-free, so the
//same function is exercised branch by branch in scripts/core_unit_tests.cpp.
#include "Debugger/CdlFileCheck.h"
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <filesystem>
#include <thread>
#include <chrono>
#include <functional>
#ifdef __APPLE__
#include <mach-o/dyld.h>
#endif

struct TimingInfoAbi
{
	double Fps;
	uint64_t MasterClock;
	uint32_t MasterClockRate;
	uint32_t FrameCount;
	uint32_t ScanlineCount;
	int32_t FirstScanline;
	uint32_t CycleCount;
};

//Same ABI as InteropHdPackCoverageReport in InteropDLL/EmuApiWrapper.cpp (see
//the TimingInfoAbi note above about why these are mirrored locally). Four
//uint32_t, IsChrRam as 0/1 rather than a bool.
struct InteropHdPackCoverageReportAbi
{
	uint32_t TilesSeen;
	uint32_t TilesWithArt;
	uint32_t ScreensSeen;
	uint32_t IsChrRam;
};

//Same ABI as Core/Shared/Interfaces/INotificationListener.h (see the
//TimingInfoAbi note above about why these are mirrored locally)
struct ExecuteShortcutParamsAbi
{
	EmulatorShortcut Shortcut;
	uint32_t Param;
	void* ParamPtr;
};

//Same ABI as CheatCode in Core/Shared/CheatManager.h (see the TimingInfoAbi
//note above about why these are mirrored locally). Only NesCustom is ever
//sent - see ADR-0184 and parseRamCheat() below.
struct CheatCodeAbi
{
	//CheatType is `enum class CheatType : uint8_t` - one byte, not four. A
	//uint32_t here makes the struct 20 bytes against the Core's 17, so both
	//the array stride and the offset of Code[] come out wrong and every cheat
	//is silently refused by AddCheat. Measured: three runs whose grid dumps
	//were byte-identical to an uncheated one.
	uint8_t Type;
	char Code[16];
};
static_assert(sizeof(CheatCodeAbi) == 17, "CheatCodeAbi must match CheatCode in Core/Shared/CheatManager.h");

//ADR-0184 - the only cheat type a recording may carry. CheatType::NesCustom
//is the AAAA:VV[:CC] form CheatManager::ConvertFromNesCustomCode stores with
//the address taken verbatim; NesGameGenie and NesProActionRocky both force
//the decoded address into PRG space (+0x8000), so neither can ever satisfy
//the rule and neither is accepted here.
static const uint8_t CheatTypeNesCustom = 2;
//The NES's internal RAM. An address at or above this is PRG space or a
//register, and a cheat there could change bytes the game unpacks into CHR RAM
//- on Contra that is the tile data we record as the game's own art.
static const uint32_t NesInternalRamEnd = 0x0800;

//Same ABI as CdlStatistics in Core/Debugger/DebugTypes.h (see the TimingInfoAbi
//note above about why these are mirrored locally). DebugTypes.h itself pulls in
//DisassemblyInfo.h and the rest of the debugger headers, which this harness has
//no business compiling against.
struct CdlStatisticsAbi
{
	uint32_t CodeBytes;
	uint32_t DataBytes;
	uint32_t TotalBytes;

	uint32_t JumpTargetCount;
	uint32_t FunctionCount;

	//CHR ROM (NES-specific)
	uint32_t DrawnChrBytes;
	uint32_t TotalChrBytes;
};
static_assert(sizeof(CdlStatisticsAbi) == 28, "CdlStatisticsAbi must match CdlStatistics in Core/Debugger/DebugTypes.h");

extern "C"
{
	void SetCheats(CheatCodeAbi codes[], uint32_t length);
	void LoadStateFile(char* filepath);
	void SaveStateFile(char* filepath);
	TimingInfoAbi GetTimingInfo(uint8_t cpuType);
	void InitDll();
	void InitializeEmu(const char* homeFolder, void* windowHandle, void* viewerHandle, bool softwareRenderer, bool noAudio, bool noVideo, bool noInput);
	bool LoadRom(char* filename, char* patchFile);
	void SetSmsConfig(SmsConfig config);
	void SetNesConfig(NesConfig config);
	void SetGameboyConfig(GameboyConfig config);
	void SetEnhancementPackConfig(EnhancementPackConfig config);
	void SetVideoConfig(VideoConfig config);
	void SetEmulationConfig(EmulationConfig config);
	void SetMepPackEnabled(const char* containerName, bool enabled);
	//F9.14 (ADR-0157) - InteropDLL/EmuApiWrapperHeadless.cpp
	bool HeadlessLoadInputScript(const char* scriptText, double frameRate, char* outError, uint32_t maxErrorLength);
	void HeadlessSetPauseFrame(uint32_t frame);
void HeadlessSetScriptStartFrame(uint32_t frame);
	uint32_t HeadlessGetScriptFrameCount();
	uint32_t HeadlessGetFrameCount();
	//ADR-0169 2026-09-08 update ("frame/state capture race") - see
	//EmuApiWrapperHeadless.cpp's comment on HeadlessLockEmulator.
	void HeadlessLockEmulator();
	void HeadlessUnlockEmulator();
	//ADR-0169 - the sprite layer is read, not rendered (Decision section 2): the
	//wrapper export holds the emulation thread at an end-of-frame boundary
	//(Emulator::Lock) and reads the OAM/palette buffers, the mapper-resolved
	//pattern tables and the $2000 sprite-control bits (NesPpuState) straight off
	//the NES console - no Debugger is attached, because a run under a live
	//debugger never parks on its target frame.
	//nametables (added 2026-09-08, ADR-0169 "capture every layer") is the
	//background's $2000-$2FFF tile+attribute bytes, mapper-resolved, read the
	//same instant as the sprite layer above.
	//The outHasChrLatch.. outChrFullSize params (same date, MMC2/MMC4 CHR-latch
	//extension) are populated only when the loaded mapper has a tile-index CHR
	//latch (BaseMapper::HasChrBankLatch) - see EmuApiWrapperHeadless.cpp.
	//outScanlineScroll (added 2026-09-08, ADR-0169 "mid-frame raster splits"):
	//240 loopy-v snapshots, one per visible scanline - see
	//BaseNesPpu::GetScanlineScrollTrace's own comment.
	//outScanlineChrBank (added 2026-09-08, ADR-0169 "mid-frame CHR bank
	//splits"): 240*32 CHR-ROM byte offsets, one per 256-byte PPU-side page per
	//visible scanline - see BaseNesPpu::GetScanlineChrBankTrace's own comment.
	//outChrFull is populated for ANY ROM-backed mapper now, not just the
	//MMC2/4 latch case (EmuApiWrapperHeadless.cpp).
	bool HeadlessCaptureNesSpriteLayer(uint8_t* oam, uint8_t* palette, uint8_t* chr, uint8_t* nametables, NesPpuState* ppuState,
		bool* outHasChrLatch, uint16_t* outChrLatchPageSize, uint8_t* outLeftFdBank, uint8_t* outLeftFeBank, uint8_t* outRightFdBank, uint8_t* outRightFeBank,
		uint8_t* outChrFull, uint32_t maxChrFullSize, uint32_t* outChrFullSize, uint32_t* outScanlineScroll, uint32_t* outScanlineChrBank);
	//ADR-0169 2026-09-08 update - is HD pack texture substitution live for this
	//run? Published into status.json so the viewer warns instead of scoring two
	//panes that cannot agree (LiveRecordFormat.h's HdPackActive comment). The
	//"hdpack-off" flag below is the switch that turns it off.
	bool HeadlessIsNesHdPackVideoActive();
	//F12.3 (ADR-0212) - ask the loaded NES pack to re-decode its repainted
	//images. Declared in InteropDLL/EmuApiWrapperMep.cpp.
	bool RequestMepImageReload();
	//F9.15 - in-memory frame capture (same wrapper file)
	bool HeadlessCaptureFrame(uint32_t* outWidth, uint32_t* outHeight, uint32_t* outFrameNumber, uint32_t* outPixelCount);
	uint32_t HeadlessReadCapturedPixels(uint32_t* outPixels, uint32_t maxPixels);
	//ADR-0167 - HUD-only capture (same wrapper file); DisplayMessage already
	//existed for the GUI (InteropDLL/EmuApiWrapper.cpp) and HeadlessSetOsdEnabled
	//is the sibling seam added in EmuApiWrapperHeadless.cpp - declared here so a
	//headless run can gate the OSD queue and queue a deterministic toast before
	//capturing it.
	bool HeadlessCaptureHud(uint32_t width, uint32_t height, uint32_t* outWidth, uint32_t* outHeight, uint32_t* outPixelCount);
	uint32_t HeadlessReadCapturedHudPixels(uint32_t* outPixels, uint32_t maxPixels);
	void DisplayMessage(char* title, char* message, char* param1);
	void HeadlessSetOsdEnabled(bool osdEnabled);
	NesConfig GetNesConfig();
	void ExecuteShortcut(ExecuteShortcutParamsAbi params);
	void TakeScreenshot();
	void MidiRecord(char* filename);
	void MidiStop();
	bool MidiIsRecording();
	void VgmRecord(char* filename);
	void VgmStop();
	bool VgmIsRecording();
	//InteropDLL/RecordApiWrapper.cpp - already exported by the linked core
	//dylib, so "movie=" needs nothing but these declarations. MoviePlay returns
	//void: MoviePlaying() is the only way to learn whether the Core accepted the
	//file (see the "movie=" note at the top of this file).
	void MoviePlay(char* filename);
	bool MoviePlaying();
	//InteropDLL/EmuApiWrapper.cpp - F5.4d's builder-window counters, polled
	//here once per sync sample so a movie-driven run carries its own evidence
	//instead of being judged only by the one number it ends on (ADR-0185 sec. 4
	//as amended, issue #201).
	void GetHdPackCoverageReport(InteropHdPackCoverageReportAbi& report);
	//InteropDLL/EmuApiWrapperHeadless.cpp - the NES internal RAM, for the
	//declared invariant of a "sync-watch=".
	bool HeadlessReadNesRam(uint16_t start, uint32_t length, uint8_t* out);
	//InteropDLL/EmuApiWrapper.cpp - the run's end in movie mode, see the
	//pauseOnBudget comment in the recording wait below.
	void Pause();
	bool IsRunning();
	void Resume();
	bool IsPaused();
	//"cdl=" - see the stop-the-run block in waitForPause below.
	void Pause();
	void GetLog(char* outBuffer, uint32_t maxLength);
	//"cdl=" - InteropDLL/DebugApiWrapper.cpp. The CDL is only fed while a
	//Debugger exists, so InitializeDebugger is what turns the capture on;
	//ResumeExecution releases the break the Debugger's constructor takes when
	//it is created while the emulator is paused - which is exactly the state a
	//headless run is in before Resume().
	void InitializeDebugger();
	bool IsDebuggerRunning();
	void ResumeExecution();
	CdlStatisticsAbi GetCdlStatistics(MemoryType memoryType);
	uint32_t GetCdlFunctions(MemoryType memoryType, uint32_t functions[], uint32_t maxSize);
	void ResetCdl(MemoryType memoryType);
	void LoadCdlFile(MemoryType memoryType, char* cdlFile);
	void SaveCdlFile(MemoryType memoryType, char* cdlFile);
	void Stop();
	void Release();
}

namespace
{
	//Core/Shared/CpuType.h values for the consoles this tool records
	constexpr uint8_t kCpuTypeGameboy = 7;
	constexpr uint8_t kCpuTypeNes = 8;
	constexpr uint8_t kCpuTypeSms = 10;

	uint8_t CpuTypeFromExtension(const std::string& rom)
	{
		std::string ext = std::filesystem::path(rom).extension().string();
		for(char& c : ext) {
			c = (char)tolower(c);
		}
		if(ext == ".nes") {
			return kCpuTypeNes;
		}
		if(ext == ".gb" || ext == ".gbc") {
			return kCpuTypeGameboy;
		}
		return kCpuTypeSms; //.sms/.gg/.sg/.col
	}

	//"cdl=" - the ROM memory region the Code/Data Logger covers, per console.
	//There is one CDL per ROM memory type; on the NES the CHR-ROM one is owned
	//by the PRG-ROM logger (NesCodeDataLogger) and is written into the same
	//file, so NesPrgRom is the only handle this tool needs.
	MemoryType CdlMemoryTypeFor(uint8_t cpuType)
	{
		switch(cpuType) {
			case kCpuTypeGameboy: return MemoryType::GbPrgRom;
			case kCpuTypeSms: return MemoryType::SmsPrgRom;
			default: return MemoryType::NesPrgRom;
		}
	}

	//GetStatistics() fills CodeBytes/DataBytes/TotalBytes (and, on the NES, the
	//CHR pair); FunctionCount is left at zero there, so the number of
	//discovered subroutine entry points is counted from GetCdlFunctions - the
	//CdlFlags::SubEntryPoint bytes.
	uint32_t CountCdlFunctions(MemoryType memType)
	{
		//One entry per PRG byte is the hard ceiling; 64K covers any NES/GB/SMS
		//ROM's realistic function count without a second call.
		std::vector<uint32_t> functions(65536);
		return GetCdlFunctions(memType, functions.data(), (uint32_t)functions.size());
	}

	void PrintCdlStatistics(const char* label, const CdlStatisticsAbi& stats, uint32_t functionCount)
	{
		//DataBytes already excludes bytes that are both code and data, so the
		//three buckets partition the ROM and "untouched" is the remainder.
		uint32_t untouched = stats.TotalBytes - stats.CodeBytes - stats.DataBytes;
		printf("cdl %s: prg total=%u code=%u data=%u untouched=%u (%.1f%% touched) functions=%u\n",
			label, stats.TotalBytes, stats.CodeBytes, stats.DataBytes, untouched,
			stats.TotalBytes ? 100.0 * (stats.CodeBytes + stats.DataBytes) / stats.TotalBytes : 0.0,
			functionCount);
		if(stats.TotalChrBytes > 0) {
			printf("cdl %s: chr drawn=%u total=%u (%.1f%% drawn)\n", label,
				stats.DrawnChrBytes, stats.TotalChrBytes, 100.0 * stats.DrawnChrBytes / stats.TotalChrBytes);
		}
	}

	//LiveSnapshot and the ComposePpm/ComposeSpritesJson/ComposeStatusJson/
	//AtomicWrite composers now live in Utilities/LiveRecordFormat.{h,cpp}
	//(shared with the interactive UI's LiveFrameRecorder, 2026-09-08 ADR-0169
	//update) - this scripted run only needs to fill one from a direct console
	//read taken with the run parked for an instant (Decision section 2).
	bool CaptureLiveSnapshot(LiveSnapshot& snapshot, bool readSpriteLayer)
	{
		//Park the emulation thread for the whole capture (ADR-0169 2026-09-08
		//update, "frame/state capture race") - HeadlessCaptureFrame alone does
		//not lock, so without this the console could render further frames
		//between the pixel capture below and the PPU/OAM/palette/VRAM read in
		//HeadlessCaptureNesSpriteLayer, publishing a composite and a
		//reconstruction that disagree about which frame they describe.
		HeadlessLockEmulator();
		uint32_t pixelCount = 0;
		if(!HeadlessCaptureFrame(&snapshot.Width, &snapshot.Height, &snapshot.CaptureFrame, &pixelCount)) {
			HeadlessUnlockEmulator();
			return false; //nothing decoded yet
		}
		snapshot.Pixels.assign(pixelCount, 0);
		if(HeadlessReadCapturedPixels(snapshot.Pixels.data(), pixelCount) != pixelCount) {
			HeadlessUnlockEmulator();
			return false;
		}
		if(readSpriteLayer) {
			//The wrapper export holds the emulation thread at an end-of-frame
			//boundary (Emulator::Lock) and reads the sprite layer as one
			//consistent state - the run's pause/stop machinery is untouched.
			NesPpuState ppu = {};
			snapshot.Chr.assign(0x2000, 0); //the $0000-$1FFF pattern tables
			snapshot.Nametables.assign(0x1000, 0); //the $2000-$2FFF background tile+attribute bytes
			//The CHR-latch extension needs an upper bound to size outChrFull -
			//no NES cartridge exceeds 1MB of CHR-ROM (the largest known mapper 9/10
			//titles ship 128KB), so this leaves ample headroom.
			static const uint32_t kMaxChrFull = 1024 * 1024;
			snapshot.ChrRomFull.assign(kMaxChrFull, 0);
			uint32_t chrFullSize = 0;
			snapshot.ScanlineScroll.assign(240, 0);
			snapshot.ScanlineChrBank.assign(240 * 0x20, 0);
			if(!HeadlessCaptureNesSpriteLayer(snapshot.Oam, snapshot.Palette, snapshot.Chr.data(), snapshot.Nametables.data(), &ppu,
				&snapshot.HasChrLatch, &snapshot.ChrLatchPageSize, &snapshot.LeftChrFdBank, &snapshot.LeftChrFeBank, &snapshot.RightChrFdBank, &snapshot.RightChrFeBank,
				snapshot.ChrRomFull.data(), kMaxChrFull, &chrFullSize, snapshot.ScanlineScroll.data(), snapshot.ScanlineChrBank.data())) {
				HeadlessUnlockEmulator();
				return false; //not a NES console - no sprite layer to publish
			}
			snapshot.ChrRomFull.resize(chrFullSize);
			if(chrFullSize == 0) {
				//No CHR-ROM (CHR-RAM game, or not a ROM-backed mapper) - the
				//per-scanline bank trace has nothing to index into, matching
				//LiveFrameRecorder.cpp's own gating.
				snapshot.ScanlineChrBank.clear();
			}
			snapshot.SpritePatternAddr = ppu.Control.SpritePatternAddr;
			snapshot.LargeSprites = ppu.Control.LargeSprites;
			snapshot.SpritesEnabled = ppu.Mask.SpritesEnabled;
			//Mask.SpriteMask/BackgroundMask are a "show" flag, not a "clip" one
			//(NesPpu.cpp's own comment: "BackgroundMask = false: Hide background
			//in leftmost 8 pixels") - LeftColumnClip/BackgroundLeftColumnClip name
			//the wire field for what it actually gates (clipping), so the polarity
			//is inverted here rather than at every reader.
			snapshot.LeftColumnClip = !ppu.Mask.SpriteMask;
			snapshot.HasSprites = true;
			//See LiveRecordFormat.h's HdPackActive comment - mirrors
			//LiveFrameRecorder::CaptureSnapshot's own line.
			snapshot.HdPackActive = HeadlessIsNesHdPackVideoActive();

			snapshot.BackgroundPatternAddr = ppu.Control.BackgroundPatternAddr;
			snapshot.BackgroundEnabled = ppu.Mask.BackgroundEnabled;
			snapshot.BackgroundLeftColumnClip = !ppu.Mask.BackgroundMask;
			//Loopy "v" (VideoRamAddr) has already scanned the whole frame at this
			//end-of-frame capture point, so the base scroll comes from loopy "t"
			//(TmpVideoRamAddr) - the scroll the game wrote for the frame to render.
			snapshot.TmpVideoRamAddr = ppu.TmpVideoRamAddr;
			snapshot.FineScrollX = ppu.ScrollX;
			snapshot.HasBackground = true;
		}
		HeadlessUnlockEmulator();
		return true;
	}
}

//F9.22: does any non-comment line of the script name a port 2 button? The
//token after "|" has to be something other than "-": "120f R|-" is an idle
//port 2, not a second pad. Mirrors HeadlessInputScript::UsesPortTwo, which
//the tool cannot call before the console config - and the config is what
//plugs the pad in.
static bool ScriptUsesPortTwo(const std::string& text)
{
	size_t pos = 0;
	while(pos < text.size()) {
		size_t end = text.find('\n', pos);
		std::string line = text.substr(pos, end == std::string::npos ? std::string::npos : end - pos);
		pos = end == std::string::npos ? text.size() : end + 1;
		size_t first = line.find_first_not_of(" \t\r");
		if(first == std::string::npos || line[first] == '#') {
			continue;
		}
		size_t bar = line.find('|');
		if(bar == std::string::npos) {
			continue;
		}
		size_t tokenStart = line.find_first_not_of(" \t\r", bar + 1);
		if(tokenStart == std::string::npos) {
			continue;
		}
		size_t tokenEnd = line.find_last_not_of(" \t\r");
		std::string token = line.substr(tokenStart, tokenEnd - tokenStart + 1);
		if(token != "-") {
			return true;
		}
	}
	return false;
}

//ADR-0184 §1 - a recording may carry a cheat only as a RAM-address code, and
//the rule is enforced by the parser rather than by discipline. Accepts the
//`AAAA:VV` / `AAAA:VV:CC` form only, and only with AAAA below 0x0800.
//
//Rejecting the type and rejecting the address are two separate checks and both
//have to be here: a letter code is refused because it is not this form at all,
//and a NesCustom code may still carry a PRG address.
//
//Returns false and fills `error` - the caller refuses the run rather than
//warning, because a run recorded under a PRG patch is evidence nobody can
//tell apart from the real thing afterwards.
static bool parseRamCheat(const std::string& code, CheatCodeAbi& out, std::string& error)
{
	if(code.size() > 15) {
		error = "too long to be an AAAA:VV[:CC] code";
		return false;
	}
	size_t colon = code.find(':');
	if(colon != 4 || (code.size() != 7 && code.size() != 10)) {
		error = "not an AAAA:VV[:CC] RAM code - a Game Genie letter code is a PRG patch and is refused (ADR-0184)";
		return false;
	}
	for(size_t i = 0; i < code.size(); i++) {
		bool wantColon = (i == 4 || i == 7);
		if(wantColon != (code[i] == ':') || (!wantColon && !isxdigit((unsigned char)code[i]))) {
			error = "not an AAAA:VV[:CC] RAM code";
			return false;
		}
	}
	uint32_t address = (uint32_t)strtoul(code.substr(0, 4).c_str(), nullptr, 16);
	if(address >= NesInternalRamEnd) {
		char buf[256];
		snprintf(buf, sizeof(buf),
			"address $%04X is outside the NES's internal RAM ($0000-$07FF) - a cheat above it can "
			"reach PRG, and a game that unpacks its tiles out of PRG would record altered art (ADR-0184)",
			address);
		error = buf;
		return false;
	}
	out = {};
	out.Type = CheatTypeNesCustom;
	memcpy(out.Code, code.c_str(), code.size());
	return true;
}


//Issue #477: the folder this binary lives in. Anything the run reads from the
//checkout resolves from here, because a path relative to the cwd made the run
//depend on where the caller stood - record_library.sh, record_stages.sh and
//replay_chain.sh never cd, and a run from outside the repo root loaded an
//empty game DB and minted a different state. argv[0] is only the fallback: a
//bare name found through PATH does not locate the file.
static std::filesystem::path ExecutableDir(const char* argv0)
{
	std::error_code error;
#ifdef __APPLE__
	char buffer[4096];
	uint32_t size = sizeof(buffer);
	if(_NSGetExecutablePath(buffer, &size) == 0) {
		std::filesystem::path exe = std::filesystem::canonical(buffer, error);
		if(!error) {
			return exe.parent_path();
		}
	}
#else
	std::filesystem::path exe = std::filesystem::read_symlink("/proc/self/exe", error);
	if(!error) {
		return exe.parent_path();
	}
#endif
	std::filesystem::path fallback = std::filesystem::canonical(argv0, error);
	return error ? std::filesystem::path() : fallback.parent_path();
}

int main(int argc, char** argv)
{
	if(argc < 4) {
		fprintf(stderr, "usage: %s <rom> <seconds> <output-prefix> [pal] [hdpack] [romtiles]\n"
			"       [screenshot] [capture] [log] [bootstrap] [filter=<name>] [mep-off]\n"
"       [reload-at-frame=<n>] [replace=<destination>=<source>]...\n"
			"       [hdpack-off] [mep-notextures] [mep-nosynth] [mep-forcepatch] [mep-disable=<pack>]\n"
			"       [state=<file.mss>] [save-state=<file.mss>] [input=<script>] [realtime]\n"
			"       [movie=<file.bk2|file.mmo>] (excludes input= and state=)\n"
			"       [cheat=AAAA:VV[:CC]] (RAM addresses $0000-$07FF only, ADR-0184; repeatable)\n"
			"       [sync-watch=AAAA:<rule>[=<n>][:<label>]] (ADR-0185 sec. 4; repeatable)\n"
			"       [sync-baseline=<trace.csv>] [sync-movie-frames=<n>] [sync-sample=<frames>]\n"
			"       [hud-message=<title>|<msg>] [live=<ms>] [cdl=<file.cdl>]\n", argv[0]);
		return 1;
	}
	std::string rom = argv[1];
	double seconds = atof(argv[2]);
	std::string prefix = argv[3];
	bool pal = false;
	bool hdPack = false;
	bool hdPackOff = false;
	bool romTiles = false;
	bool screenshot = false;
	bool capture = false;
	bool dumpLog = false;
	//F12.3 (ADR-0212): "reload-at-frame=<n>" asks for a pack image reload once
	//the run reaches that emulated frame; "replace=<dst>=<src>" copies one file
	//over another immediately before the request, so the overwrite and the
	//reload are ordered by construction instead of by a race with an outside
	//script. "replace" may be given more than once.
	//
	//The trigger counts emulated frames, not wall seconds: a headless run is
	//far faster than real time (80 emulated seconds finish in about 11 wall
	//seconds on an M1), so a wall-clock trigger would simply never fire.
	int64_t reloadAtFrame = -1;
	bool reloadRequested = false;
	std::vector<std::pair<std::string, std::string>> replacements; //destination -> source
	VideoFilterType videoFilter = VideoFilterType::None;
	EnhancementPackConfig mep = {};
	mep.BootstrapEnhancementFolder = false; //opt-in headless ("bootstrap" flag) - it writes beside the ROM
	std::string mepDisable;
	std::string stateFile;
	std::string cdlPath; //"cdl=" - see the Code/Data Logger block below
	//F9.22: written when the run reaches its frame target, so a stage reached
	//by one scripted run is the start of the next - a per-stage recording
	//roadmap needs states minted by the same tool that consumes them.
	std::string saveStateFile;
	//input script: lines "<count>f <buttons>" or "<count>s <buttons>", buttons
	//in U D L R A B S(elect) T(start) or "-". A bare count is a parse error
	//(ADR-0157 section 1). Parsed core-side by HeadlessInputScript, which is
	//also what scripts/core_unit_tests.cpp covers.
	std::string inputScriptText;
	std::string inputScriptPath;
	//"movie=": a recorded playthrough replayed by the Core's own movie player
	//(MovieManager::Play), in place of an input script. See the "movie=" note at
	//the top of this file for the two containers the Core accepts and for what a
	//.mmo does to the settings pushed below.
	std::string moviePath;
	//ADR-0185 sec. 4 as amended 2026-09-14 (issue #201): the desync gate. Any
	//hdpack recording writes <prefix>-synctrace.csv - one row per sample of the
	//builder's own counters, the movie player's state and every declared watch
	//byte - so a movie-less run is a usable baseline for the next movie run at
	//no extra cost. The rules are in Core/Shared/MovieSyncGate.h.
	std::vector<MovieSyncWatch> syncWatches;
	std::string syncBaselinePath;
	MovieSyncParams syncParams;
	//60 frames = one emulated second. The sample costs a lock and a walk of the
	//builder's tile map; once a second is far below the noise of the recording
	//itself and fine enough to place a divergence inside a second.
	uint32_t syncSampleFrames = 60;
	bool realtime = false;
	//ADR-0169: live=<ms> publishing interval in wall-clock milliseconds (0=off);
	//the publish lambda below and the scratch folder <prefix>-live/ it writes.
	int liveMs = 0;
	std::string liveDir;
	bool liveReadSprites = false;
	double liveNextWall = 0.0;
	//status.json's "rom": the file name this run is recording, so a viewer
	//attached to the run names the game the same way it does for the
	//emulator's own session (the "rom" field of ComposeStatusJson).
	std::string liveRomName;
	//ADR-0167: queued right before a "capture" run's settle sleep, so a test
	//can assert the HUD capture's blank flag flips. title|message, split on
	//the first '|' (neither Localize()'d key needs one).
	std::string hudMessageTitle;
	std::string hudMessageText;
	//ADR-0184 - RAM-address cheats, validated by parseRamCheat() as they are
	//parsed and applied after the ROM (and any state) is loaded.
	std::vector<CheatCodeAbi> cheats;
	for(int i = 4; i < argc; i++) {
		if(strcmp(argv[i], "pal") == 0) {
			pal = true;
		} else if(strcmp(argv[i], "hdpack") == 0) {
			hdPack = true;
		} else if(strcmp(argv[i], "romtiles") == 0) {
			romTiles = true;
		} else if(strcmp(argv[i], "screenshot") == 0) {
			screenshot = true;
		} else if(strcmp(argv[i], "capture") == 0) {
			capture = true;
		} else if(strncmp(argv[i], "filter=", 7) == 0) {
			const char* name = argv[i] + 7;
			if(strcmp(name, "none") == 0) { videoFilter = VideoFilterType::None; }
			else if(strcmp(name, "hq2x") == 0) { videoFilter = VideoFilterType::HQ2x; }
			else if(strcmp(name, "hq3x") == 0) { videoFilter = VideoFilterType::HQ3x; }
			else if(strcmp(name, "hq4x") == 0) { videoFilter = VideoFilterType::HQ4x; }
			else if(strcmp(name, "scale2x") == 0) { videoFilter = VideoFilterType::Scale2x; }
			else if(strcmp(name, "scale3x") == 0) { videoFilter = VideoFilterType::Scale3x; }
			else if(strcmp(name, "scale4x") == 0) { videoFilter = VideoFilterType::Scale4x; }
			else if(strcmp(name, "xbrz2x") == 0) { videoFilter = VideoFilterType::xBRZ2x; }
			else if(strcmp(name, "xbrz3x") == 0) { videoFilter = VideoFilterType::xBRZ3x; }
			else if(strcmp(name, "xbrz4x") == 0) { videoFilter = VideoFilterType::xBRZ4x; }
			else if(strcmp(name, "xbrz5x") == 0) { videoFilter = VideoFilterType::xBRZ5x; }
			else if(strcmp(name, "xbrz6x") == 0) { videoFilter = VideoFilterType::xBRZ6x; }
			else if(strcmp(name, "prescale2x") == 0) { videoFilter = VideoFilterType::Prescale2x; }
			else if(strcmp(name, "prescale3x") == 0) { videoFilter = VideoFilterType::Prescale3x; }
			else if(strcmp(name, "prescale4x") == 0) { videoFilter = VideoFilterType::Prescale4x; }
			else if(strcmp(name, "prescale6x") == 0) { videoFilter = VideoFilterType::Prescale6x; }
			else if(strcmp(name, "prescale8x") == 0) { videoFilter = VideoFilterType::Prescale8x; }
			else if(strcmp(name, "prescale10x") == 0) { videoFilter = VideoFilterType::Prescale10x; }
			else {
				fprintf(stderr, "unknown filter name: %s\n", name);
				return 1;
			}
		} else if(strcmp(argv[i], "log") == 0) {
			dumpLog = true;
		} else if(strcmp(argv[i], "mep-off") == 0) {
			mep.EnableMepPacks = false;
		} else if(strcmp(argv[i], "hdpack-off") == 0) {
			//The player's own switch (NesConfig::EnableHdPacks), not an
			//approximation of it: "mep-off" only takes MEP-installed packs out
			//of discovery, while a loose HdPacks/<rom>/ pack (MEP-v1 §5.1) still
			//loads and still replaces pixels. A run that has to compare the
			//composed frame against PPU data needs substitution off at the one
			//gate NesConsole::LoadHdPack checks first.
			hdPackOff = true;
		} else if(strncmp(argv[i], "reload-at-frame=", 16) == 0) {
			reloadAtFrame = atoll(argv[i] + 16);
			if(reloadAtFrame < 0) {
				fprintf(stderr, "reload-at-frame must be a non-negative frame number\n");
				return 1;
			}
		} else if(strncmp(argv[i], "replace=", 8) == 0) {
			std::string spec = argv[i] + 8;
			size_t eq = spec.find('=');
			if(eq == std::string::npos || eq == 0 || eq + 1 >= spec.size()) {
				fprintf(stderr, "replace must be <destination>=<source>\n");
				return 1;
			}
			replacements.emplace_back(spec.substr(0, eq), spec.substr(eq + 1));
		} else if(strcmp(argv[i], "mep-notextures") == 0) {
			mep.EnableTextures = false;
		} else if(strcmp(argv[i], "mep-nosynth") == 0) {
			mep.EnableSynth = false;
		} else if(strcmp(argv[i], "bootstrap") == 0) {
			mep.BootstrapEnhancementFolder = true;
		} else if(strcmp(argv[i], "mep-forcepatch") == 0) {
			mep.ApplyPatchOnHashMismatch = true;
		} else if(strncmp(argv[i], "cheat=", 6) == 0) {
			CheatCodeAbi cheat;
			std::string why;
			if(!parseRamCheat(argv[i] + 6, cheat, why)) {
				fprintf(stderr, "refused cheat \"%s\": %s\n", argv[i] + 6, why.c_str());
				return 1;
			}
			cheats.push_back(cheat);
		} else if(strncmp(argv[i], "cdl=", 4) == 0) {
			cdlPath = argv[i] + 4;
		} else if(strncmp(argv[i], "state=", 6) == 0) {
			stateFile = argv[i] + 6;
		} else if(strncmp(argv[i], "save-state=", 11) == 0) {
			saveStateFile = argv[i] + 11;
		} else if(strncmp(argv[i], "input=", 6) == 0) {
			inputScriptPath = argv[i] + 6;
			FILE* f = fopen(inputScriptPath.c_str(), "rb");
			if(!f) {
				fprintf(stderr, "cannot open input script: %s\n", inputScriptPath.c_str());
				return 1;
			}
			char buffer[4096];
			size_t read;
			while((read = fread(buffer, 1, sizeof(buffer), f)) > 0) {
				inputScriptText.append(buffer, read);
			}
			fclose(f);
		} else if(strncmp(argv[i], "movie=", 6) == 0) {
			moviePath = argv[i] + 6;
		} else if(strncmp(argv[i], "sync-watch=", 11) == 0) {
			//ADR-0185 sec. 4 as amended (issue #201). Refused, not warned
			//about, for the reason every other parser here refuses: a watch
			//the harness silently dropped is a gate that reports "clean" on a
			//run nobody checked, which is the exact failure this gate exists
			//to end.
			MovieSyncWatch watch;
			std::string error;
			if(!MovieSyncGate::ParseWatch(argv[i] + 11, watch, error)) {
				fprintf(stderr, "refused sync-watch: %s\n", error.c_str());
				return 1;
			}
			syncWatches.push_back(watch);
		} else if(strncmp(argv[i], "sync-baseline=", 14) == 0) {
			syncBaselinePath = argv[i] + 14;
		} else if(strncmp(argv[i], "sync-movie-frames=", 18) == 0) {
			//The frame the movie's own row count says its input runs dry on -
			//rows + 2 for a .bk2 (ADR-0185's second measured pass). The caller
			//computes it because only the caller has the movie file open.
			syncParams.ExpectedMovieEndFrame = (uint32_t)atoi(argv[i] + 18);
		} else if(strncmp(argv[i], "sync-sample=", 12) == 0) {
			int sample = atoi(argv[i] + 12);
			if(sample < 1) {
				fprintf(stderr, "sync-sample= needs a positive number of frames: %s\n", argv[i]);
				return 1;
			}
			syncSampleFrames = (uint32_t)sample;
		} else if(strcmp(argv[i], "realtime") == 0) {
			realtime = true;
		} else if(strncmp(argv[i], "mep-disable=", 12) == 0) {
			mepDisable = argv[i] + 12;
		} else if(strncmp(argv[i], "hud-message=", 12) == 0) {
			std::string spec = argv[i] + 12;
			size_t sep = spec.find('|');
			if(sep == std::string::npos) {
				fprintf(stderr, "hud-message= needs a '|' between title and message: %s\n", spec.c_str());
				return 1;
			}
			hudMessageTitle = spec.substr(0, sep);
			hudMessageText = spec.substr(sep + 1);
		} else if(strncmp(argv[i], "live=", 5) == 0) {
			//ADR-0169: publish cadence in wall-clock milliseconds. Below ~50ms the
			//capture+publish itself costs more than the interval - just spin disk.
			liveMs = atoi(argv[i] + 5);
			if(liveMs < 50) {
				fprintf(stderr, "live= needs a wall-clock interval of at least 50 ms: %s\n", argv[i]);
				return 1;
			}
		}
	}

	//Refused, not warned about: a movie drives the same pad an input script
	//drives and resets the poll counter for its own input rows, and it carries
	//its own start state and power cycles the console itself - so either
	//combination silently records something other than what was asked for.
	//Checked after the loop so the order the flags were typed in never matters.
	if(!moviePath.empty() && !inputScriptPath.empty()) {
		fprintf(stderr, "refused: movie=%s and input=%s cannot be combined - a movie drives the pad itself\n", moviePath.c_str(), inputScriptPath.c_str());
		return 1;
	}
	if(!moviePath.empty() && !stateFile.empty()) {
		fprintf(stderr, "refused: movie=%s and state=%s cannot be combined - a movie carries its own start state and power cycles the console\n", moviePath.c_str(), stateFile.c_str());
		return 1;
	}

	std::filesystem::path outDir = std::filesystem::absolute(prefix).parent_path();
	std::filesystem::path home = outDir / "mesen-home";
	std::filesystem::create_directories(home);

	if(liveMs > 0) {
		//ADR-0169: the live folder is scratch, created up front so a failure to
		//write it disables the feature before the run starts, never mid-run.
		liveDir = std::filesystem::absolute(prefix + "-live").string();
		std::error_code liveError;
		std::filesystem::create_directories(liveDir, liveError);
		if(liveError) {
			fprintf(stderr, "live: cannot create %s (%s) - live view disabled, recording continues\n", liveDir.c_str(), liveError.message().c_str());
			liveDir.clear();
			liveMs = 0;
		} else {
			//Empty the folder of a previous run's publish set - the interactive
			//producer does the same in LiveFrameRecorder::ClearSlot, see its
			//comment for the cross-ROM contamination this prevents. Reusing a
			//<prefix> across two ROMs is the case that bites: a run only writes
			//the files its own mapper has data for, and the viewer selects a
			//reconstruction path by a file's mere presence.
			static const char* kPublishFiles[] = {
				"frame.ppm", "sprites.json", "chr.bin", "palette.json", "nametables.bin",
				"background.json", "scanlinescroll.bin", "scanlinechrbank.bin",
				"chrfull.bin", "chrlatch.json", "status.json"
			};
			for(const char* name : kPublishFiles) {
				std::error_code ignored;
				std::filesystem::path path = std::filesystem::path(liveDir) / name;
				std::filesystem::remove(path, ignored);
				std::filesystem::remove(path.string() + ".tmp", ignored);
			}
		}
	}

	//NES mapper detection wants the game DB in the home folder; copy it from
	//the checkout next to this binary (scripts/../UI/Dependencies), or from a
	//copy beside the binary itself. Resolved from the binary, never the cwd
	//(issue #477); a run with no DB anywhere says so instead of silently
	//falling back to the iNES header.
	if(!std::filesystem::exists(home / "MesenNesDB.txt")) {
		std::filesystem::path exeDir = ExecutableDir(argv[0]);
		const std::filesystem::path candidates[] = {
			exeDir / ".." / "UI" / "Dependencies" / "MesenNesDB.txt",
			exeDir / "MesenNesDB.txt",
		};
		bool copied = false;
		for(const std::filesystem::path& candidate : candidates) {
			if(!exeDir.empty() && std::filesystem::exists(candidate)) {
				std::filesystem::copy_file(candidate, home / "MesenNesDB.txt");
				copied = true;
				break;
			}
		}
		if(!copied) {
			fprintf(stderr, "warning: no MesenNesDB.txt found from %s - NES games load without the game database\n",
				exeDir.string().c_str());
		}
	}

	InitDll();
	//Same headless pattern as UI/Utilities/TestRunner.cs: null handles mean
	//no renderer/sound/input backends are created at all.
	InitializeEmu(home.string().c_str(), nullptr, nullptr, true, true, true, true);

	//The GUI normally pushes every config struct at startup; headless we must
	//supply the audible channel volumes ourselves - the core-side defaults
	//for SMS/CV/NES ChannelVolumes are all ZERO (the UI-side defaults are the
	//100s the user actually hears), which would silence the enhanced synth
	//and therefore the MIDI capture. GB/SNES default to 100 in the core.
	SmsConfig sms = {};
	for(int i = 0; i < 4; i++) {
		sms.ChannelVolumes[i] = 100;
	}
	if(pal) {
		sms.Region = ConsoleRegion::Pal;
	}
	//The core defaults every port to ControllerType::None, so with no input
	//backend no control device is created at all - and the debugger's input
	//overrides (SetInputOverrides, used by "input=<script>") are dropped on the
	//floor because NesDebugger/SmsDebugger only write into a device that exists.
	sms.Port1.Type = ControllerType::SmsController;
	//F9.22: a second pad only when the script names one ("<port1>|<port2>"),
	//so a one-player script keeps producing the recording it always did.
	bool portTwo = ScriptUsesPortTwo(inputScriptText);
	if(portTwo) {
		sms.Port2.Type = ControllerType::SmsController;
	}
	//Power-on RAM defaults to RamState::Random, which is a second source of
	//run-to-run variation on top of the one F9.14 removed: a game that reads
	//uninitialised RAM takes a different path, and the recording differs even
	//when both runs cover the same frames. The core's own deterministic replay
	//harness zeroes it for the same reason (RecordedRomTest::Run).
	sms.RamPowerOnState = RamState::AllZeros;
	SetSmsConfig(sms);

	NesConfig nes = GetNesConfig();
	for(int i = 0; i < 11; i++) {
		nes.ChannelVolumes[i] = 100;
	}
	//Default 2C02 palette (the UI writes it into UserPalette; the core reads it
	//as-is, so without this every NES color - tiles, HD pack builder - is black)
	static const uint32_t kDefaultNesPalette[64] = {
		0xFF666666, 0xFF002A88, 0xFF1412A7, 0xFF3B00A4, 0xFF5C007E, 0xFF6E0040, 0xFF6C0600, 0xFF561D00, 0xFF333500, 0xFF0B4800, 0xFF005200, 0xFF004F08, 0xFF00404D, 0xFF000000, 0xFF000000, 0xFF000000, 0xFFADADAD, 0xFF155FD9, 0xFF4240FF, 0xFF7527FE, 0xFFA01ACC, 0xFFB71E7B, 0xFFB53120, 0xFF994E00, 0xFF6B6D00, 0xFF388700, 0xFF0C9300, 0xFF008F32, 0xFF007C8D, 0xFF000000, 0xFF000000, 0xFF000000, 0xFFFFFEFF, 0xFF64B0FF, 0xFF9290FF, 0xFFC676FF, 0xFFF36AFF, 0xFFFE6ECC, 0xFFFE8170, 0xFFEA9E22, 0xFFBCBE00, 0xFF88D800, 0xFF5CE430, 0xFF45E082, 0xFF48CDDE, 0xFF4F4F4F, 0xFF000000, 0xFF000000, 0xFFFFFEFF, 0xFFC0DFFF, 0xFFD3D2FF, 0xFFE8C8FF, 0xFFFBC2FF, 0xFFFEC4EA, 0xFFFECCC5, 0xFFF7D8A5, 0xFFE4E594, 0xFFCFEF96, 0xFFBDF4AB, 0xFFB3F3CC, 0xFFB5EBF2, 0xFFB8B8B8, 0xFF000000, 0xFF000000
	};
	bool paletteEmpty = true;
	for(int i = 0; i < 64; i++) {
		if(nes.UserPalette[i] != 0) {
			paletteEmpty = false;
			break;
		}
	}
	if(paletteEmpty) {
		memcpy(nes.UserPalette, kDefaultNesPalette, sizeof(kDefaultNesPalette));
		nes.IsFullColorPalette = false;
	}
	if(pal) {
		nes.Region = ConsoleRegion::Pal;
	}
	//See the SmsConfig note above: without a standard controller in port 1 an
	//input script has nothing to drive.
	nes.Port1.Type = ControllerType::NesController;
	if(portTwo) {
		nes.Port2.Type = ControllerType::NesController; //see the SmsConfig note above
	}
	nes.RamPowerOnState = RamState::AllZeros; //see the SmsConfig note above
	if(hdPackOff) {
		nes.EnableHdPacks = false; //see the "hdpack-off" argument above
	}
	SetNesConfig(nes);

	//Pin the GB model to the ROM extension so the HD pack capture path
	//(DMG vs CGB tile keys - ADR-0036) is deterministic; the default
	//AutoFavorGbc would run plain .gb ROMs on CGB hardware.
	GameboyConfig gameboy = {};
	gameboy.Model = std::filesystem::path(rom).extension() == ".gb" ? GameboyModel::Gameboy : GameboyModel::AutoFavorGbc;
	//Neutral video pipeline so a 1:1 HD pack screenshot matches the default
	//filter's output exactly (GbcAdjustColors/BlendFrames both recolor pixels)
	gameboy.GbcAdjustColors = false;
	gameboy.BlendFrames = false;
	gameboy.RamPowerOnState = RamState::AllZeros; //see the SmsConfig note above
	SetGameboyConfig(gameboy);

	//The screenshot pipeline (BaseVideoFilter::TakeScreenshot) runs the
	//configured scale filter before writing the PNG, so the video config has
	//to be pushed before the frame is captured. Every other field keeps the
	//struct's own default (neutral pipeline: no scanlines, no rotation).
	VideoConfig video = {};
	video.VideoFilter = videoFilter;
	SetVideoConfig(video);

	//F9.14: the frame limiter only decides how long a run takes on the wall
	//clock, which is now nothing the output depends on. Off by default so a
	//300 s recording does not cost 300 s; "realtime" puts it back for anyone
	//who wants to watch one go by.
	EmulationConfig emulation = {};
	if(!realtime) {
		emulation.EmulationSpeed = 0;
	}
	SetEmulationConfig(emulation);

	SetEnhancementPackConfig(mep);
	if(!mepDisable.empty()) {
		SetMepPackEnabled(mepDisable.c_str(), false);
	}

	//The script's own unit is the frame; 's' steps and the <seconds> argument
	//are resolved at the region's nominal rate (ADR-0157 section 1). This is
	//the region the flags force, not the console's exact fps - the console is
	//not loaded yet, and the provider has to be registered before it is (the
	//control manager that holds it is created by the game load itself).
	const double frameRate = pal ? 50.0070 : 60.0988;
	uint32_t totalFrames = (uint32_t)std::max(1.0, std::round(seconds * frameRate));

	if(!inputScriptPath.empty()) {
		char scriptError[1024] = {};
		if(!HeadlessLoadInputScript(inputScriptText.c_str(), frameRate, scriptError, (uint32_t)sizeof(scriptError))) {
			fprintf(stderr, "%s: %s\n", inputScriptPath.c_str(), scriptError);
			return 1;
		}
		printf("input script: %s (%u frames%s)\n", inputScriptPath.c_str(), HeadlessGetScriptFrameCount(), portTwo ? ", drives port 2" : "");
	}

	//Freeze the run on its first frame, so what the recorders are started on
	//is a fixed frame rather than "whatever the emulation thread reached while
	//this thread was calling into the DLL".
	HeadlessSetPauseFrame(1);

	//ADR-0167: with the OSD on (the default), LoadRom enqueues a "game loaded"
	//toast (Emulator.cpp) that never ages out of a short parked run, so a HUD
	//capture could never read blank. Gate it off for the load and the run;
	//HeadlessSetOsdEnabled(true) is turned on for the one instant a capture run
	//queues its own test toast (see the capture block below).
	HeadlessSetOsdEnabled(false);

	if(!LoadRom((char*)rom.c_str(), (char*)"")) {
		fprintf(stderr, "failed to load ROM: %s\n", rom.c_str());
		return 1;
	}

	//The watchdog exists so a *hung* emulator cannot hang CI forever, and it
	//measures exactly that: wall clock since the frame counter last moved.
	//Issue #165: it used to be a budget for the whole run, 120 s + 3x the
	//recording's <seconds>. That relationship died with ADR-0157/F9.14 - the
	//frame limiter is off, so a run's length in emulated frames says nothing
	//about how long it takes on the host, and four parallel jobs on a loaded
	//machine make it say even less. Eight of thirty runs then tripped it at
	//1020 s (= 120 + 3x300) while the emulator was plainly still advancing -
	//Zelda at frame 7482 of 18030, Double Dragon at 15710 - and the message
	//said "the emulator is not advancing", which was simply false. Each one
	//wrote a *truncated* pack that the batch went on to install.
	const double stallTimeout = 90.0;
	auto t0 = std::chrono::steady_clock::now();
	auto elapsed = [&t0]() { return std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count(); };
	auto waitForPause = [&](const char* what, std::function<void()> onTick = std::function<void()>()) {
		uint32_t lastFrame = HeadlessGetFrameCount();
		double lastProgress = elapsed();
		while(!IsPaused()) {
			if(!IsRunning()) {
				fprintf(stderr, "emulation stopped unexpectedly while %s\n", what);
				return false;
			}
			uint32_t frame = HeadlessGetFrameCount();
			//"cdl=" - with a debugger attached the run will not park itself
			//(HeadlessInputEngine::ApplyFrame logs "the debugger is attached -
			//not pausing"), so the stop is issued from here. Pause() routes
			//through Debugger::Step, which is safe on this thread and not on
			//the emulation one.
			if(!cdlPath.empty() && frame >= totalFrames) {
				Pause();
			}
			if(frame != lastFrame) {
				lastFrame = frame;
				lastProgress = elapsed();
			} else if(elapsed() - lastProgress > stallTimeout) {
				fprintf(stderr, "STALLED: no frame in %.1fs of wall clock while %s (stuck on frame %u, %.1fs into the run)\n", elapsed() - lastProgress, what, frame, elapsed());
				return false;
			}
			if(onTick) {
				onTick();
			}
			std::this_thread::sleep_for(std::chrono::milliseconds(2));
		}
		return true;
	};

	if(!waitForPause("waiting for the first frame")) {
		return 1;
	}

	if(!stateFile.empty()) {
		LoadStateFile((char*)stateFile.c_str());
		//A state restores the emulator's frame counter, and the pause target
		//below is absolute: <seconds> must count from the state's frame, or a
		//state saved past the target ends the run on the spot (and one saved
		//before it silently shortens the run by its own age).
		uint32_t stateFrame = HeadlessGetFrameCount();
		totalFrames += stateFrame;
		//The script is indexed by emulator frame too: its frame 0 is the
		//state's frame, or the whole script lands before the run begins.
		HeadlessSetScriptStartFrame(stateFrame);
		printf("state loaded: %s (at frame %u; the run ends at frame %u)\n", stateFile.c_str(), stateFrame, totalFrames);
	}

	if(!moviePath.empty()) {
		//Same ordering the Core's own headless replay uses (RecordedRomTest::Run:
		//LoadRom, then GetMovieManager()->Play) - MesenMovie::Play power cycles
		//the console itself, which resets the frame counter, so the run's frame
		//budget below counts from the movie's first frame with nothing to add.
		MoviePlay((char*)moviePath.c_str());
		//MovieManager::Play drops a file it does not recognise on the floor: no
		//player is constructed, no message is shown, and MoviePlay returns void.
		//The refusal is synchronous and happens on this thread, so the poll below
		//is a fact and not a race - _player is either set or empty by the time
		//MoviePlay returns. Without this check a mistyped or unconvertible file
		//records the title screen and the run still exits 0.
		if(!MoviePlaying()) {
			fprintf(stderr, "refused movie \"%s\": the Core would not play it. It accepts only a zip containing \"Input Log.txt\" (BizHawk .bk2) or \"GameSettings.txt\" (Mesen .mmo) - there is no .fm2 reader\n", moviePath.c_str());
			//Unlike the failure paths above this one, the ROM is loaded and the
			//emulation thread is alive: returning straight out of main leaves it
			//running through the process' static teardown, which segfaults.
			Stop();
			Release();
			return 1;
		}
		printf("movie playing: %s\n", moviePath.c_str());
	}

	//ADR-0184 - after LoadRom and after any state, because Emulator::LoadRom
	//clears the cheat list: applied before either, this would silently do
	//nothing and the run would look like a normal one.
	if(!cheats.empty()) {
		SetCheats(cheats.data(), (uint32_t)cheats.size());
		for(const CheatCodeAbi& c : cheats) {
			printf("cheat applied: %s (RAM address, ADR-0184)\n", c.Code);
		}
	}
	//"cdl=" - Code/Data Logger capture. The CDL is fed from the Debugger's
	//instruction/read hooks (NesDebugger::ProcessInstruction / ProcessRead and
	//the GB/SMS equivalents), which exist only while a Debugger is attached, so
	//nothing is logged unless InitializeDebugger is called here - before the
	//run, and after LoadRom/state, since a Debugger is built around the loaded
	//console. An existing file is loaded first so coverage is the union of
	//every run that wrote to this path (LoadCdlFile with autoResetCdl=false:
	//it seeds the map, and the run ORs its own flags on top).
	MemoryType cdlMemType = CdlMemoryTypeFor(CpuTypeFromExtension(rom));
	if(!cdlPath.empty()) {
		InitializeDebugger();
		if(!IsDebuggerRunning()) {
			fprintf(stderr, "cdl: the debugger did not start - no CDL would be recorded\n");
			return 1;
		}
		if(std::filesystem::exists(cdlPath)) {
			LoadCdlFile(cdlMemType, (char*)cdlPath.c_str());
			CdlStatisticsAbi seed = GetCdlStatistics(cdlMemType);
			printf("cdl seeded from %s: code=%u data=%u of %u prg bytes\n",
				cdlPath.c_str(), seed.CodeBytes, seed.DataBytes, seed.TotalBytes);
		} else {
			//NesDebugger's constructor has just auto-loaded <home>/Debugger/
			//<rom>.cdl, and its destructor writes that file back at teardown -
			//so without this reset a second run in the same scratch home would
			//silently start from the first run's coverage, and a "new" map
			//would not be new. The seed is the file named by "cdl=", or
			//nothing; never the debugger folder's own copy.
			ResetCdl(cdlMemType);
			printf("cdl: new map at %s (no existing file to accumulate onto)\n", cdlPath.c_str());
		}
	}

	TimingInfoAbi timing = GetTimingInfo(CpuTypeFromExtension(rom));
	printf("ROM loaded: %s%s\n", rom.c_str(), pal ? " [region forced: PAL]" : "");
	printf("emulated fps: %.3f (master clock %u Hz)\n", timing.Fps, timing.MasterClockRate);

	std::string mid = prefix + ".mid", vgm = prefix + ".vgm";
	std::string packFolder = std::filesystem::absolute(prefix + "-hdpack").string();
	if(romTiles) {
		HdPackBuilderOptions options = {};
		options.SaveFolder = (char*)packFolder.c_str();
		options.FilterType = ScaleFilterType::Prescale;
		options.Scale = 1;
		options.ChrRamBankSize = 0x1000;
		ExecuteShortcut({ EmulatorShortcut::ExportRomTilesHdPack, 0, &options });
		printf("static tile export: hdpack=%s\n", packFolder.c_str());
		//The export is synchronous and needs no gameplay - stop on the frame
		//the run is already paused on.
		totalFrames = 1;
	} else if(hdPack) {
		HdPackBuilderOptions options = {};
		options.SaveFolder = (char*)packFolder.c_str();
		options.FilterType = ScaleFilterType::Prescale;
		options.Scale = 1;
		options.ChrRamBankSize = 0x1000; //NES-only field
		ExecuteShortcut({ EmulatorShortcut::StartRecordHdPack, 0, &options });
		printf("recording: hdpack=%s\n", packFolder.c_str());
	} else if(screenshot || capture) {
		printf("running %u frames for a final %s\n", totalFrames, screenshot ? "screenshot" : "capture");
	} else {
		MidiRecord((char*)mid.c_str());
		VgmRecord((char*)vgm.c_str());
		printf("recording: midi=%d vgm=%d\n", MidiIsRecording(), VgmIsRecording());
	}

	//ADR-0169 live publish. The sprite layer is read, not rendered (Decision
	//section 2): OAM, palette and the mapper-resolved pattern tables come off
	//the console, and the $2000 control bits (sprite pattern table, 8x16) out
	//of the PPU state snapshot - the viewer rebuilds sprite pixels from them.
	//CaptureLiveSnapshot holds the emulation thread at an end-of-frame boundary
	//(Emulator::Lock) for the instant the bytes are read - no Debugger is
	//attached, so the run still parks on its target frame; the cost of that hold
	//is the "must be measured" of the ADR's Consequences, measured against the
	//plain run below.
	auto publishLive = [&]() {
		if(liveMs <= 0 || liveDir.empty()) {
			return;
		}
		double now = elapsed();
		if(now < liveNextWall) {
			return;
		}
		liveNextWall = now + (double)liveMs / 1000.0;

		LiveSnapshot snapshot;
		if(!CaptureLiveSnapshot(snapshot, liveReadSprites)) {
			return; //no decoded frame yet - try on the next tick
		}
		uint32_t cpuFrame = HeadlessGetFrameCount();
		bool ok = LiveRecordFormat::AtomicWrite(liveDir + "/frame.ppm", LiveRecordFormat::ComposePpm(snapshot));
		if(liveReadSprites) {
			ok = LiveRecordFormat::AtomicWrite(liveDir + "/sprites.json", LiveRecordFormat::ComposeSpritesJson(snapshot, cpuFrame)) && ok;
			ok = LiveRecordFormat::AtomicWrite(liveDir + "/chr.bin", snapshot.Chr.data(), snapshot.Chr.size()) && ok;
			ok = LiveRecordFormat::AtomicWrite(liveDir + "/nametables.bin", snapshot.Nametables.data(), snapshot.Nametables.size()) && ok;
			ok = LiveRecordFormat::AtomicWrite(liveDir + "/scanlinescroll.bin", snapshot.ScanlineScroll.data(), snapshot.ScanlineScroll.size() * sizeof(uint32_t)) && ok;
			ok = LiveRecordFormat::AtomicWrite(liveDir + "/background.json", LiveRecordFormat::ComposeBackgroundJson(snapshot)) && ok;
			if(!snapshot.ScanlineChrBank.empty()) {
				ok = LiveRecordFormat::AtomicWrite(liveDir + "/scanlinechrbank.bin", snapshot.ScanlineChrBank.data(), snapshot.ScanlineChrBank.size() * sizeof(uint32_t)) && ok;
			}
			//ADR-0169 2026-09-08 update: chrfull.bin now covers any ROM-backed
			//mapper's CHR-ROM, not just the MMC2/4 latch case - see
			//LiveFrameRecorder.cpp's identical block.
			if(!snapshot.ChrRomFull.empty()) {
				ok = LiveRecordFormat::AtomicWrite(liveDir + "/chrfull.bin", snapshot.ChrRomFull.data(), snapshot.ChrRomFull.size()) && ok;
				ok = LiveRecordFormat::AtomicWrite(liveDir + "/chrlatch.json", LiveRecordFormat::ComposeChrLatchJson(snapshot)) && ok;
			}
		}
		ok = LiveRecordFormat::AtomicWrite(liveDir + "/status.json", LiveRecordFormat::ComposeStatusJson(false, cpuFrame, totalFrames, elapsed(), snapshot.HdPackActive, liveRomName)) && ok;
		if(!ok) {
			fprintf(stderr, "live: cannot write %s - live view disabled, recording continues\n", liveDir.c_str());
			liveDir.clear();
		}
	};
	if(liveMs > 0) {
		liveRomName = rom.substr(rom.find_last_of("/\\") + 1);
		//NES runs read the sprite layer too; GB/SMS/GG runs publish frames only.
		liveReadSprites = CpuTypeFromExtension(rom) == kCpuTypeNes;
		if(liveReadSprites) {
			//palette.json: the exact RGB this run renders with, so the viewer
			//colors reconstructed sprites like the composed frame. Written once,
			//and the viewer treats it as immutable for the run. The sprite bytes
			//themselves come from a direct console read at publish time
			//(HeadlessCaptureNesSpriteLayer), so no debugger is attached here
			//and the run keeps parking on its target frame.
			std::string colors = "{\n  \"colors\": [";
			char hex[16];
			for(int i = 0; i < 64; i++) {
				if(i) colors += ",";
				snprintf(hex, sizeof(hex), "\"#%06X\"", kDefaultNesPalette[i] & 0xFFFFFF);
				colors += hex;
			}
			colors += "]\n}\n";
			LiveRecordFormat::AtomicWrite(liveDir + "/palette.json", colors);
		}
		liveNextWall = 0.0; //publish on the first tick of the recording wait
	}

	//Movie mode's two per-tick duties, on top of the live publish. There is no
	//end-of-movie callback anywhere in the Core - MesenMovie::SetInput calls
	//MovieManager::Stop() when it runs out of input rows - so polling
	//MoviePlaying() is the only observable, and it is polled here rather than
	//waited on: the frame budget stays the authority over the run's length, and
	//a movie that ends early only prints where it ended.
	uint32_t movieEndFrame = 0;
	bool pauseOnBudget = false;

	//ADR-0185 sec. 4 as amended (issue #201). Sampled from this thread, on the
	//frame counter and not on wall clock, so two runs of the same length sample
	//the same frames however fast the host is - the matched-frame comparison in
	//MovieSyncGate depends on that and on nothing else.
	std::vector<MovieSyncSample> syncTrace;
	uint32_t syncNextFrame = 0;
	auto sampleSync = [&]() {
		if(!hdPack) {
			return; //no builder, no counters to sample
		}
		uint32_t frame = HeadlessGetFrameCount();
		if(frame < syncNextFrame) {
			return;
		}
		syncNextFrame = frame + syncSampleFrames;
		MovieSyncSample sample;
		sample.Frame = frame;
		sample.MoviePlaying = !moviePath.empty() && MoviePlaying();
		InteropHdPackCoverageReportAbi coverage = {};
		GetHdPackCoverageReport(coverage);
		sample.TilesSeen = coverage.TilesSeen;
		sample.TilesWithArt = coverage.TilesWithArt;
		sample.ScreensSeen = coverage.ScreensSeen;
		for(const MovieSyncWatch& watch : syncWatches) {
			uint8_t value = 0;
			//A non-NES console has no internal RAM to read; the byte stays 0
			//and the watch then only ever sees a constant, which no rule reads
			//as a violation.
			HeadlessReadNesRam(watch.Address, 1, &value);
			sample.WatchValues.push_back(value);
		}
		syncTrace.push_back(sample);
	};

	//F12.3 (ADR-0212): the run's own reload trigger. Copies the replacement
	//file first, then asks - so the bytes are on disk before the stat that
	//decides whether anything changed. Fires once.
	auto serveReload = [&]() {
		if(reloadAtFrame < 0 || reloadRequested || (int64_t)HeadlessGetFrameCount() < reloadAtFrame) {
			return;
		}
		reloadRequested = true;
		for(const std::pair<std::string, std::string>& swap : replacements) {
			std::error_code ec;
			std::filesystem::copy_file(std::filesystem::u8path(swap.second), std::filesystem::u8path(swap.first),
				std::filesystem::copy_options::overwrite_existing, ec);
			if(ec) {
				fprintf(stderr, "replace failed: %s -> %s (%s)\n", swap.second.c_str(), swap.first.c_str(), ec.message().c_str());
				return;
			}
			printf("replaced %s with %s\n", swap.first.c_str(), swap.second.c_str());
		}
		bool asked = RequestMepImageReload();
		printf("pack image reload requested at frame %u: %s\n", HeadlessGetFrameCount(), asked ? "accepted" : "no NES console loaded");
		fflush(stdout);
	};

	auto onTick = [&]() {
		serveReload();
		publishLive();
		sampleSync();
		if(moviePath.empty()) {
			return;
		}
		if(movieEndFrame == 0 && !MoviePlaying()) {
			movieEndFrame = HeadlessGetFrameCount();
			printf("movie ended at frame %u of %u - the run continues to its frame budget\n", movieEndFrame, totalFrames);
			fflush(stdout);
		}
		//The run's end has to come from this thread while a movie plays.
		//BaseControlManager::UpdateInputState stops at the first input provider
		//whose SetInput returns true, and the movie's provider registers on
		//AfterInitConsole while HeadlessInputProvider re-registers on the later
		//GameLoaded (Emulator.cpp) - so the movie is always ahead of it in the
		//list and always returns true, and the in-frame pause of ADR-0157 never
		//runs. A host-side Pause() lands a few frames past the target instead of
		//exactly on it; that is the one property movie mode gives up, and the
		//"capture finished" line below reports the frame actually reached.
		//Measured on an M-series host, Zelda: with this guard a 300-frame budget
		//parks at 301, exactly like a scripted run; with it removed the same run
		//ran to 602 - the frame the movie's input ran out and its provider
		//unregistered, which is the first frame the in-frame pause could fire.
		if(!pauseOnBudget && HeadlessGetFrameCount() >= totalFrames) {
			pauseOnBudget = true;
			Pause();
		}
	};

	//The run itself: resume, and let the provider stop it from inside the
	//frame it was told to stop on. Nothing here decides how many frames run
	//(except in movie mode - see onTick above).
	HeadlessSetPauseFrame(totalFrames);
	Resume();
	if(!cdlPath.empty()) {
		//The Debugger was constructed while the emulator was paused, and
		//Debugger::Debugger takes a one-instruction break in that case. Release
		//it, or the run parks on its first instruction and never reaches its
		//frame target.
		ResumeExecution();
	}
	bool reachedTarget = waitForPause("recording", onTick);

	//ADR-0169: one final status so the viewer can show "done" rather than stale
	//- the run stops being published the moment it parks on its target frame.
	if(liveMs > 0 && !liveDir.empty()) {
		LiveRecordFormat::AtomicWrite(liveDir + "/status.json", LiveRecordFormat::ComposeStatusJson(true, HeadlessGetFrameCount(), totalFrames, elapsed(), HeadlessIsNesHdPackVideoActive(), liveRomName));
	}

	//The hud-message toast is emitted inside the capture block below, not here:
	//with the OSD gated off across the load/run, a DisplayMessage before the
	//OSD is re-enabled for the capture would go to the log instead of the queue.

	if(screenshot || capture) {
		//The video decoder runs on its own thread; give it a moment to drain
		//so what we read is the paused frame and not the one before it. This
		//is a display-pipeline settle, not part of the run length.
		std::this_thread::sleep_for(std::chrono::milliseconds(200));
	}

	if(screenshot) {
		TakeScreenshot();
		printf("screenshot saved in %s\n", (home / "Screenshots").string().c_str());
	}

	bool captureFailed = false;
	//Issue #506: set when the HUD capture froze the emulation thread on the
	//emulator's run lock (see the capture block). The lock is released just
	//before Stop(), on this thread, because Stop() joins the thread that is
	//waiting for it.
	bool emulatorFrozen = false;
	if(capture) {
		//F9.15: the frame never reaches the disk. Two calls, because the size
		//of the capture is only known after it is taken and the pixels must
		//come from *that* capture, not from a second one taken later.
		uint32_t width = 0, height = 0, frameNumber = 0, pixelCount = 0;
		if(!HeadlessCaptureFrame(&width, &height, &frameNumber, &pixelCount)) {
			fprintf(stderr, "capture failed: the emulator has no decoded frame\n");
			captureFailed = true;
		} else {
			std::vector<uint32_t> pixels(pixelCount);
			uint32_t copied = HeadlessReadCapturedPixels(pixels.data(), pixelCount);
			if(copied != pixelCount) {
				fprintf(stderr, "capture failed: read %u of %u pixels\n", copied, pixelCount);
				captureFailed = true;
			} else {
				FrameBorders borders = FrameCaptureMath::MeasureBorders(pixels.data(), width, height);
				printf("capture: %ux%u frame=%u pixels=%u checksum=0x%08X\n",
					width, height, frameNumber, pixelCount, FrameCaptureMath::Checksum(pixels.data(), pixelCount));
				printf("capture borders: left=%u right=%u top=%u bottom=%u colour=0x%08X blank=%d\n",
					borders.Left, borders.Right, borders.Top, borders.Bottom, borders.Colour, borders.IsBlank ? 1 : 0);

				//ADR-0167: same canvas size as the frame capture above (the
				//base frame size when no video filter is active). Additive -
				//the two lines above are unchanged, so an existing consumer's
				//parsing does not break.
				//
				//Two details keep the blank flag meaningful:
				//1. The queue holds exactly the messages a test queues. The OSD
				//   has been off since before LoadRom, so no "game loaded" toast
				//   is resident; a hud-message= run re-enables it for just the
				//   DisplayMessage below and disables it again, so the queue
				//   gains exactly that one toast and nothing the resumed run may
				//   throw at the OSD in the meantime.
				//2. The capture is taken with the emulator *not paused*, not on
				//   the paused frame the captures above used. SystemHud::Draw
				//   paints the pause icon on every paused frame - its one
				//   always-on element - so a paused HUD capture could never read
				//   blank=1, and blank=0 would not mean a message was queued.
				//   Clearing the pause flag (the run's pause latch was consumed
				//   when it stopped at its target frame) drops that icon from
				//   the draw - without letting a frame run, see below.
				//Together they restore the ADR's meaning for blank: uniform
				//transparent = no message, anything else = one is queued.
				bool queuedToast = !hudMessageTitle.empty();
				if(queuedToast) {
					HeadlessSetOsdEnabled(true);
					DisplayMessage((char*)hudMessageTitle.c_str(), (char*)hudMessageText.c_str(), (char*)"");
					HeadlessSetOsdEnabled(false);
				}
				//Issue #506: the resume above is for the pause icon alone, and it
				//must not move the run. It did: the stop was re-armed only after
				//the HUD read, at whatever frame the emulation thread had reached
				//by then, so a loaded machine put "capture finished", the last
				//sync-trace sample and the save-state 1-6 frames past the target
				//while the frame capture above stayed fixed on the paused frame.
				//The emulation thread is frozen on the emulator's own run lock
				//instead of left running: with the pause flag cleared, IsPaused()
				//is false, so SystemHud::Draw skips the pause icon and `blank`
				//keeps the meaning ADR-0167 gave it, and the thread cannot take a
				//frame, so the counter, the console state and the state this run
				//writes all stay on the frame it parked on. The lock is held to
				//the end of the run (released just before Stop(), which needs the
				//thread able to leave the lock wait to be joined).
				HeadlessLockEmulator();
				emulatorFrozen = true;
				Resume();
				if(queuedToast) {
					//The run is frozen, not paused: no frame runs, but the toast's
					//fade-in and expiry are wall-clock (MessageInfo::GetOpacity),
					//so a moment still has to pass before the capture. Its reason
					//was a decode-thread transient observed before this freeze -
					//the capture read the HUD surface before the just-queued
					//toast was in the queue (~1/3 of runs with no settle, 0/N with
					//this one). 100ms is comfortably inside the toast's 3000ms
					//lifetime.
					std::this_thread::sleep_for(std::chrono::milliseconds(100));
				}
				//Issue #506 test seam: the wall clock this read costs was the
				//run's frame error, so a case that wants to prove it no longer is
				//has to be able to make the read slow on purpose. Off unless the
				//variable is set - nothing else in the harness reads it, and it
				//changes no behaviour of its own.
#ifdef _MSC_VER
				//getenv is not deprecated, MSVC's CRT just says so (C4996), and
				//this file is built by the makefile only - the guard keeps the
				//option open without a bare disable.
#pragma warning(push)
#pragma warning(disable: 4996)
#endif
				if(const char* hudReadDelay = std::getenv("HEADLESS_HUD_CAPTURE_DELAY_MS")) {
					uint32_t delayMs = (uint32_t)std::strtoul(hudReadDelay, nullptr, 10);
					if(delayMs > 0) {
						std::this_thread::sleep_for(std::chrono::milliseconds(delayMs));
					}
				}
#ifdef _MSC_VER
#pragma warning(pop)
#endif
				uint32_t hudWidth = 0, hudHeight = 0, hudPixelCount = 0;
				if(!HeadlessCaptureHud(width, height, &hudWidth, &hudHeight, &hudPixelCount)) {
					fprintf(stderr, "hud capture failed: degenerate size %ux%u\n", width, height);
					captureFailed = true;
				} else {
					std::vector<uint32_t> hudPixels(hudPixelCount);
					uint32_t hudCopied = HeadlessReadCapturedHudPixels(hudPixels.data(), hudPixelCount);
					if(hudCopied != hudPixelCount) {
						fprintf(stderr, "hud capture failed: read %u of %u pixels\n", hudCopied, hudPixelCount);
						captureFailed = true;
					} else {
						FrameBorders hudBorders = FrameCaptureMath::MeasureBorders(hudPixels.data(), hudWidth, hudHeight);
						printf("capture hud: %ux%u checksum=0x%08X blank=%d\n",
							hudWidth, hudHeight, FrameCaptureMath::Checksum(hudPixels.data(), hudPixelCount), hudBorders.IsBlank ? 1 : 0);
					}
				}
			}
		}
	}

	//One last sample on the frame the run actually parked on, so the trace's
	//final row is the number the old total-only gate would have compared.
	syncNextFrame = 0;
	sampleSync();

	if(hdPack) {
		ExecuteShortcut({ EmulatorShortcut::StopRecordHdPack, 0, nullptr });
	} else if(!screenshot && !capture) {
		MidiStop();
		VgmStop();
	}
	printf("capture finished: %u frames (target %u), %.1fs of wall clock%s\n", HeadlessGetFrameCount(), totalFrames, elapsed(), reachedTarget ? "" : " - INCOMPLETE");

	//--- ADR-0185 sec. 4, amended 2026-09-14 (issue #201): the desync gate ---
	//The trace is written for every hdpack run, movie or not: a movie-less run
	//is the baseline the next movie-driven run of the same ROM and length is
	//compared against, and it costs nothing to have already recorded it. The
	//verdict is only computed for a movie-driven run - a run with no movie
	//cannot desync from one.
	bool syncGateFailed = false;
	if(hdPack && !syncTrace.empty()) {
		std::string tracePath = prefix + "-synctrace.csv";
		FILE* trace = fopen(tracePath.c_str(), "w");
		if(!trace) {
			fprintf(stderr, "could not write the sync trace to %s\n", tracePath.c_str());
			syncGateFailed = true;
		} else {
			std::string text = MovieSyncGate::FormatTraceHeader(syncWatches);
			for(const MovieSyncSample& sample : syncTrace) {
				text += MovieSyncGate::FormatTraceRow(sample);
			}
			fwrite(text.data(), 1, text.size(), trace);
			fclose(trace);
			printf("sync trace: %s (%zu samples, every %u frames)\n", tracePath.c_str(), syncTrace.size(), syncSampleFrames);
		}
	}
	if(!moviePath.empty() && hdPack) {
		std::vector<MovieSyncSample> baseline;
		if(!syncBaselinePath.empty()) {
			FILE* f = fopen(syncBaselinePath.c_str(), "r");
			if(!f) {
				fprintf(stderr, "sync gate: could not read the baseline trace %s\n", syncBaselinePath.c_str());
				syncGateFailed = true;
			} else {
				std::string text;
				char buffer[4096];
				size_t read;
				while((read = fread(buffer, 1, sizeof(buffer), f)) > 0) {
					text.append(buffer, read);
				}
				fclose(f);
				std::vector<std::string> labels;
				std::string error;
				if(!MovieSyncGate::ParseTrace(text, baseline, labels, error)) {
					fprintf(stderr, "sync gate: %s is not a usable trace: %s\n", syncBaselinePath.c_str(), error.c_str());
					syncGateFailed = true;
					baseline.clear();
				}
			}
		}
		std::vector<MovieSyncFinding> findings = MovieSyncGate::Evaluate(syncTrace, baseline, syncWatches, syncParams);
		for(const MovieSyncFinding& finding : findings) {
			fprintf(finding.Fatal ? stderr : stdout, "sync gate [%s] %s: %s\n",
				finding.Fatal ? "FAIL" : "note", finding.Code.c_str(), finding.Detail.c_str());
		}
		if(MovieSyncGate::IsFatal(findings)) {
			syncGateFailed = true;
		} else {
			printf("sync gate: no fatal finding\n");
		}
		fflush(stdout);
	}
	if(!saveStateFile.empty()) {
		//Only a run that reached its target parks on a frame worth keeping; a
		//state from an INCOMPLETE run would silently move the stage.
		if(reachedTarget) {
			SaveStateFile((char*)saveStateFile.c_str());
			printf("state saved: %s\n", saveStateFile.c_str());
		} else {
			printf("state NOT saved (run incomplete): %s\n", saveStateFile.c_str());
		}
	}
	//"cdl=" - write the map, then prove it is not empty and that it reads back
	//as what was written. A capture that silently records nothing has cost this
	//project days twice (a cheat ABI mismatch, a movie that played unsynced),
	//so an all-zero CDL fails the run instead of leaving a plausible file on
	//disk.
	bool cdlFailed = false;
	if(!cdlPath.empty()) {
		CdlStatisticsAbi stats = GetCdlStatistics(cdlMemType);
		uint32_t functionCount = CountCdlFunctions(cdlMemType);
		PrintCdlStatistics("recorded", stats, functionCount);
		if(stats.TotalBytes == 0) {
			fprintf(stderr, "cdl: no PRG ROM memory region for this ROM - nothing to log\n");
			cdlFailed = true;
		} else if(stats.CodeBytes == 0) {
			fprintf(stderr, "cdl: zero code bytes after %u frames - the debugger was attached but nothing was logged\n", HeadlessGetFrameCount());
			cdlFailed = true;
		} else {
			SaveCdlFile(cdlMemType, (char*)cdlPath.c_str());
			//The DllExport SaveCdlFile returns void, so the ofstream failing
			//(an unwritable path, a full disk) is not something this tool can
			//be told. Issue #347: the round-trip below used to be the only
			//guard, and it cannot see that either - LoadCdlFile leaves the map
			//untouched when the file will not read, so the statistics were
			//compared against themselves and an unwritable path printed
			//"round-trip verified" and exited 0. The bytes on disk are checked
			//first, and they carry the reason.
			CdlFileOnDiskCheck onDisk = CheckCdlFileOnDisk(cdlPath, stats.TotalBytes);
			if(!onDisk.Ok) {
				fprintf(stderr, "cdl: %s %s\n", cdlPath.c_str(), onDisk.Reason.c_str());
				cdlFailed = true;
			} else {
				//Round-trip: reload what was just written and require identical
				//statistics. This catches a truncated write and, on the NES, a CHR
				//block that did not survive the append/split in NesCodeDataLogger.
				//ResetCdl first so a load that refuses the file (a CRC that does
				//not match the ROM) cannot leave the run's own map in place and
				//pass as its own reload; a load that succeeds resets it anyway.
				ResetCdl(cdlMemType);
				LoadCdlFile(cdlMemType, (char*)cdlPath.c_str());
				CdlStatisticsAbi reread = GetCdlStatistics(cdlMemType);
				uint32_t rereadFunctions = CountCdlFunctions(cdlMemType);
				bool sameStats = reread.CodeBytes == stats.CodeBytes && reread.DataBytes == stats.DataBytes &&
					reread.TotalBytes == stats.TotalBytes && reread.DrawnChrBytes == stats.DrawnChrBytes &&
					reread.TotalChrBytes == stats.TotalChrBytes && rereadFunctions == functionCount;
				if(sameStats) {
					printf("cdl written: %s (%llu bytes on disk, round-trip verified)\n",
						cdlPath.c_str(), (unsigned long long)onDisk.SizeOnDisk);
				} else {
					PrintCdlStatistics("reloaded", reread, rereadFunctions);
					fprintf(stderr, "cdl: %s does not read back as what was written\n", cdlPath.c_str());
					cdlFailed = true;
				}
			}
		}
	}
	if(dumpLog) {
		std::string log(65536, '\0');
		GetLog(log.data(), (uint32_t)log.size());
		log.resize(strlen(log.c_str()));
		printf("--- core log ---\n%s--- end log ---\n", log.c_str());
	}
	if(emulatorFrozen) {
		//Issue #506: the run was ended by freezing the emulation thread rather
		//than by parking it, so release it here - the count is still the frame
		//the run parked on, and the stop armed on that frame parks the emulator
		//again within one frame of the release, exactly as a plain run ends.
		//Everything that reads the frame count, the trace or the state has
		//already run, so what Stop() cuts short is one frame nobody asked for.
		HeadlessSetPauseFrame(HeadlessGetFrameCount());
		HeadlessUnlockEmulator();
	}
	Stop();
	Release();

	//The run's own verdict, in the output and not only in the exit code.
	//A run that did not reach its frame target is a failed capture, not a
	//short one - the caller (bootstrap_auto_packs.sh) must see it.
	//A run the sync gate failed is a corrupt recording, not a short one: its
	//art comes from a playthrough nobody intended (ADR-0185 sec. 4 as amended,
	//issue #201), so it must never be archived as if it were the movie's.
	//
	//Printed because the exit code is the first thing a caller loses: the
	//2026-09-19 F12.2 sweep reported "headless_record exits 1 on a successful
	//render" from a shell line that piped this tool into `tail` and ended in an
	//`ls` of a folder that did not exist - the 1 was the `ls`, this tool had
	//already returned 0, and nothing in the output said so. A run that ends
	//without a "result:" line did not finish; one that ends with "result: ok"
	//succeeded whatever the surrounding pipeline reports.
	std::string verdict;
	if(!reachedTarget) { verdict += verdict.empty() ? "" : ", "; verdict += "the run never reached its frame target"; }
	if(captureFailed) { verdict += verdict.empty() ? "" : ", "; verdict += "the frame capture failed"; }
	if(syncGateFailed) { verdict += verdict.empty() ? "" : ", "; verdict += "the movie sync gate failed"; }
	if(cdlFailed) { verdict += verdict.empty() ? "" : ", "; verdict += "the CDL was not written"; }
	printf("result: %s\n", verdict.empty() ? "ok" : ("FAILED - " + verdict).c_str());
	fflush(stdout);
	return verdict.empty() ? 0 : 1;
}
