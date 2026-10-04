#include "Common.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/Video/VideoDecoder.h"
#include "Core/Shared/Interfaces/IConsole.h"
#include "Core/Shared/Video/VideoRenderer.h"
#include "Core/Shared/SystemActionManager.h"
#include "Core/Shared/MessageManager.h"
#include "Core/Shared/SaveStateManager.h"
#include "Core/Shared/Interfaces/INotificationListener.h"
#include "Core/Shared/KeyManager.h"
#include "Core/Shared/ShortcutKeyHandler.h"
#include "Core/Shared/TimingInfo.h"
#include "Core/Shared/CheatManager.h"
#include "Core/Shared/DebuggerRequest.h"
#include "Core/Shared/EnhancementPacks/MepPackManager.h"
#include "Core/NES/NesConsole.h"
#include "Core/NES/HdPacks/HdPackBuilder.h"
#include "Core/Netplay/GameClient.h"
#include "Utilities/ArchiveReader.h"
#include "Utilities/FolderUtilities.h"
#include "Utilities/StringUtilities.h"
#include "InteropNotificationListeners.h"

#ifdef _WIN32
	#include "Windows/Renderer.h"
	#include "Windows/SoundManager.h"
	#include "Windows/WindowsKeyManager.h"
	#include "Windows/WindowsMouseManager.h"
#elif __APPLE__
	#include "MacOS/MacOSMetalRenderer.h"
	#include "Sdl/SdlSoundManager.h"
	#include "MacOS/MacOSKeyManager.h"
	#include "MacOS/MacOSMouseManager.h"
#else
	#include "Linux/LinuxOglRenderer.h"
	#include "Sdl/SdlSoundManager.h"
	#include "Linux/LinuxKeyManager.h"
	#include "Linux/LinuxMouseManager.h"
#endif

#include "Shared/Video/SoftwareRenderer.h"
#include "Shared/Video/RendererSelection.h"

//Issue #621: the process-global core is torn down only by Release(), never by
//the C++ static destructors that exit() runs. A host that exits without
//Release() (the headless UI test process, a ctypes script) still has managed
//thread-pool work in flight - an update-check continuation calling
//GetMesenVersion, a timer calling IsPaused - and ~unique_ptr<Emulator> nulled
//and deleted the emulator under those threads: SIGSEGV after every test had
//passed. Each holder is heap-allocated and never destroyed, so the objects
//outlive every caller until the OS reclaims the process; Release() still
//resets them explicitly, exactly as before.
unique_ptr<IKeyManager>& _keyManager = *new unique_ptr<IKeyManager>();
unique_ptr<IMouseManager>& _mouseManager = *new unique_ptr<IMouseManager>();
unique_ptr<Emulator>& _emu = *new unique_ptr<Emulator>(new Emulator());
bool _softwareRenderer = false;

static void* _windowHandle = nullptr;
static void* _viewerHandle = nullptr;

static constexpr const char* _buildDateTime = __DATE__ ", " __TIME__;

static InteropNotificationListeners _listeners;

struct InteropRomInfo
{
	char RomPath[2000];
	char PatchPath[2000];
	RomFormat Format;
	ConsoleType Console;
	DipSwitchInfo DipSwitches;
	CpuType CpuTypes[5];
	uint32_t CpuTypeCount;
};

//F5.4d: mirror of HdPackCoverageReport (HdPackBuilder.h) flattened for C#
//marshaling. IsChrRam is 0/1 (uint32, not bool, to keep the struct layout
//unambiguous across the C/C# boundary).
struct InteropHdPackCoverageReport
{
	uint32_t TilesSeen = 0;
	uint32_t TilesWithArt = 0;
	uint32_t ScreensSeen = 0;
	uint32_t IsChrRam = 0;
};

IRenderingDevice* InitRenderer()
{
	if(_softwareRenderer) {
		return new SoftwareRenderer(_emu.get());
	} else {
#ifdef _WIN32
		return new Renderer(_emu.get(), (HWND)_viewerHandle);
#elif __APPLE__
		//ADR-0237: Metal when it comes up on the viewer's native view, otherwise
		//the software renderer, so a handle that cannot back a CAMetalLayer
		//never leaves the window black.
		IRenderingDevice* metal = MacOSMetalRenderer::Create(_emu.get(), _viewerHandle);
		MacRendererKind kind = ChooseMacRenderer(false, metal != nullptr);
		return kind == MacRendererKind::Metal ? metal : new SoftwareRenderer(_emu.get());
#else
		return new LinuxOglRenderer(_emu.get(), _viewerHandle);
#endif
	}
}

IAudioDevice* InitSoundManager()
{
#ifdef _WIN32
	return SoundManager::Create(_emu.get(), (HWND)_windowHandle);
#else
	return SdlSoundManager::Create(_emu.get());
#endif
}

extern "C"
{
	DllExport bool __stdcall TestDll()
	{
		return true;
	}

	DllExport uint32_t __stdcall GetMesenVersion()
	{
		return _emu->GetSettings()->GetVersion();
	}

	DllExport const char* __stdcall GetMesenBuildDate()
	{
		return _buildDateTime;
	}

	DllExport void __stdcall InitDll()
	{
		_emu->Initialize();
		KeyManager::SetSettings(_emu->GetSettings());
	}

	DllExport void __stdcall InitializeEmu(const char* homeFolder, void* windowHandle, void* viewerHandle, bool softwareRenderer, bool noAudio, bool noVideo, bool noInput)
	{
		FolderUtilities::SetHomeFolder(homeFolder);

		if(windowHandle != nullptr && viewerHandle != nullptr) {
			_windowHandle = windowHandle;
			_viewerHandle = viewerHandle;
			_softwareRenderer = softwareRenderer;

			//Upstream 3924215 passed these crossed (noVideo gated audio, noAudio gated
			//video); each flag gates its own device here.
			_emu->SetAudioVideoInitCallback(noAudio ? nullptr : InitSoundManager, noVideo ? nullptr : InitRenderer);

			if(!noInput) {
#ifdef _WIN32
				_keyManager.reset(new WindowsKeyManager(_emu.get(), (HWND)_windowHandle));
				_mouseManager.reset(new WindowsMouseManager());
#elif __APPLE__
				_keyManager.reset(new MacOSKeyManager(_emu.get()));
				_mouseManager.reset(new MacOSMouseManager());
#else
				_keyManager.reset(new LinuxKeyManager(_emu.get()));
				_mouseManager.reset(new LinuxMouseManager(_windowHandle));
#endif

				KeyManager::RegisterKeyManager(_keyManager.get());
			}
		}
	}

	DllExport void __stdcall SetFullscreenMode(FullscreenSettings settings)
	{
		shared_ptr<IRenderingDevice> renderer = _emu->GetRenderer();
		if(renderer) {
			renderer->SetFullscreenMode(settings);
		}
	}

	DllExport bool __stdcall LoadRom(char* filename, char* patchFile)
	{
		_emu->GetGameClient()->Disconnect();
		return _emu->LoadRom((VirtualFile)filename, patchFile ? (VirtualFile)patchFile : VirtualFile());
	}

	DllExport void __stdcall AddKnownGameFolder(char* folder)
	{
		FolderUtilities::AddKnownGameFolder(folder);
	}

	DllExport void __stdcall GetRomInfo(InteropRomInfo& info)
	{
		RomInfo romInfo = _emu->GetRomInfo();

		string romPath = romInfo.RomFile;
		string patchPath = romInfo.PatchFile;

		memset(info.RomPath, 0, sizeof(info.RomPath));
		memset(info.PatchPath, 0, sizeof(info.PatchPath));

		memcpy(info.RomPath, romPath.c_str(), romPath.size());
		memcpy(info.PatchPath, patchPath.c_str(), patchPath.size());
		info.Format = romInfo.Format;
		info.Console = _emu->GetConsoleType();
		info.DipSwitches = romInfo.DipSwitches;

		vector<CpuType> cpuTypes = _emu->GetCpuTypes();
		info.CpuTypeCount = std::min<uint32_t>((uint32_t)cpuTypes.size(), 5);
		for(size_t i = 0; i < 5 && i < cpuTypes.size(); i++) {
			info.CpuTypes[i] = cpuTypes[i];
		}
	}

	DllExport TimingInfo __stdcall GetTimingInfo(CpuType cpuType)
	{
		return _emu->GetTimingInfo(cpuType);
	}

	//F5.4d: "what you played" coverage for the HD Pack Builder window. Zero-fills
	//when no NES console is loaded or the builder is not recording. Read via
	//_emu->GetConsole() (safe weak_ptr lock - no Debug-only emulation-thread assert)
	//so the UI thread can poll it while recording; NesConsole acquires the emulation
	//lock internally before touching the builder's maps.
	DllExport void __stdcall GetHdPackCoverageReport(InteropHdPackCoverageReport& report)
	{
		report = {};
		auto console = _emu->GetConsole();
		if(NesConsole* nes = dynamic_cast<NesConsole*>(console.get())) {
			HdPackCoverageReport r = {};
			nes->GetHdPackCoverageReport(r);
			report.TilesSeen = r.TilesSeen;
			report.TilesWithArt = r.TilesWithArt;
			report.ScreensSeen = r.ScreensSeen;
			report.IsChrRam = r.IsChrRam ? 1 : 0;
		}
	}

	//ADR-0246 (P.13): does a loaded pack draw this game's picture? The same
	//decision GetVideoFilter makes (IConsole::IsDrawingPackArt) - Settings >
	//Look disables Pixels and labels NTSC as not applied while it is true.
	DllExport bool __stdcall IsDrawingPackArt()
	{
		shared_ptr<IConsole> console = _emu->GetConsole();
		return console ? console->IsDrawingPackArt() : false;
	}

	//ADR-0246 §5: Hold to Compare. On: the decoder skips Pixels and NTSC and
	//the renderer bypasses the shader chain (kept loaded); off restores both.
	//A paused game is redrawn so the change shows at once.
	DllExport void __stdcall SetLookCompare(bool enabled)
	{
		_emu->GetSettings()->SetLookCompare(enabled);
		_emu->GetVideoDecoder()->RedrawPausedFrame();
	}

	//ADR-0246: a Look change made while paused shows without resuming.
	DllExport void __stdcall RedrawPausedFrame()
	{
		_emu->GetVideoDecoder()->RedrawPausedFrame();
	}

	DllExport void __stdcall TakeScreenshot()
	{
		_emu->GetVideoDecoder()->TakeScreenshot();
	}

	DllExport void __stdcall ProcessAudioPlayerAction(AudioPlayerActionParams p)
	{
		_emu->ProcessAudioPlayerAction(p);
	}

	DllExport void __stdcall GetArchiveRomList(char* filename, char* outBuffer, uint32_t maxLength)
	{
		std::ostringstream out;
		unique_ptr<ArchiveReader> reader = ArchiveReader::GetReader(filename);
		if(reader) {
			for(string romName : reader->GetFileList(VirtualFile::RomExtensions)) {
				out << romName << "[!|!]";
			}
		}

		StringUtilities::CopyToBuffer(out.str(), outBuffer, maxLength);
	}

	//#689: writes the file a ROM resource names (an archive's inner ROM, in the
	//"<archive>\x1<inner>[\x1<index>]" form RomInfo.RomPath uses) to outPath,
	//so the Remaster jobs get the ROM itself and not the .zip/.7z around it.
	DllExport bool __stdcall ExtractRomFile(char* resourcePath, char* outPath)
	{
		VirtualFile file(resourcePath);
		vector<uint8_t> data;
		if(!file.ReadFile(data) || data.empty()) {
			return false;
		}
		ofstream out(outPath, std::ios::out | std::ios::binary | std::ios::trunc);
		if(!out.good()) {
			return false;
		}
		out.write((const char*)data.data(), data.size());
		return out.good();
	}

	DllExport bool __stdcall IsRunning()
	{
		return _emu->IsRunning();
	}

	DllExport int32_t __stdcall GetStopCode()
	{
		return _emu->GetStopCode();
	}

	DllExport void __stdcall Stop()
	{
		_emu->GetGameClient()->Disconnect();
		_emu->Stop(true);
	}

	DllExport void __stdcall Pause()
	{
		if(!_emu->GetGameClient()->Connected()) {
			_emu->Pause();
		}
	}

	DllExport void __stdcall Resume()
	{
		if(!_emu->GetGameClient()->Connected()) {
			_emu->Resume();
		}
	}

	DllExport bool __stdcall IsPaused()
	{
		return _emu->IsPaused();
	}

	DllExport void __stdcall Release()
	{
		if(_emu) {
			_emu->Stop(true);
			_emu->Release();
		}

		_keyManager.reset();
		_emu.reset();
	}

	DllExport INotificationListener* __stdcall RegisterNotificationCallback(NotificationListenerCallback callback)
	{
		return _listeners.RegisterNotificationCallback(callback, _emu.get());
	}

	DllExport void __stdcall UnregisterNotificationCallback(INotificationListener* listener)
	{
		_listeners.UnregisterNotificationCallback(listener);
	}

	DllExport void __stdcall DisplayMessage(char* title, char* message, char* param1)
	{
		MessageManager::DisplayMessage(title, message, param1 ? param1 : "");
	}

	DllExport void __stdcall GetLog(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(MessageManager::GetLog(), outBuffer, maxLength);
	}

	//MEP enhancement packs (F3) - see MepPackManager::GetPackListText for the format
	DllExport void __stdcall GetMepPackList(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetEnhancementPackManager()->GetPackListText(), outBuffer, maxLength);
	}

	DllExport void __stdcall GetMepRomSha1(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetEnhancementPackManager()->GetRomSha1(), outBuffer, maxLength);
	}

	//ADR-0211: the whole-file SHA-1 (header included), the form an HD pack's
	//<supportedRom> line carries. Deliberately a second export rather than a
	//flag on GetMepRomSha1 - the two hashes must never be mistaken for one
	//another at the call site.
	DllExport void __stdcall GetMepRomFileSha1(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetEnhancementPackManager()->GetRomFileSha1(), outBuffer, maxLength);
	}

	DllExport void __stdcall GetMepSiblingFolder(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetEnhancementPackManager()->GetSiblingFolder(), outBuffer, maxLength);
	}

	DllExport bool __stdcall IsMepBootstrapping()
	{
		return _emu->GetEnhancementPackManager()->IsBootstrapping();
	}

	//ADR-0243 (F12.20): Remaster's Record / Stop. The bootstrap recorder writes
	//the next <project>/auto/rec-NNN/ and lists it in project.json; source is
	//play/tas/ai/script. False when it declined (a foreign pack dresses the
	//ROM, #142 - the reason is in the log) or nothing is loaded.
	DllExport bool __stdcall StartMepRecording(const char* source, const char* note)
	{
		return _emu->GetEnhancementPackManager()->StartRecording(source ? source : "play", note ? note : "");
	}

	DllExport bool __stdcall StopMepRecording()
	{
		return _emu->GetEnhancementPackManager()->StopRecording();
	}

	//Source/note the next on-load bootstrap records with (BootstrapEnhancementFolder)
	DllExport void __stdcall SetMepNextRecordingSource(const char* source, const char* note)
	{
		_emu->GetEnhancementPackManager()->SetNextRecordingSource(source ? source : "play", note ? note : "");
	}

	DllExport void __stdcall GetMepRecordingFolder(char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetEnhancementPackManager()->GetRecordingFolder(), outBuffer, maxLength);
	}

	DllExport void __stdcall SetMepPackEnabled(const char* containerName, bool enabled)
	{
		_emu->GetEnhancementPackManager()->SetPackEnabled(containerName ? containerName : "", enabled);
		_emu->GetVideoRenderer()->InvalidateBorderAsset();
	}

	//P.3 (PRD Part B §5): per-ROM-sha1 preferred pack_id pushed by the UI
	//at config-apply time; the core consults it per loaded ROM so the choice
	//overrides the ADR-0040 lexicographic order without touching DisabledPacks.
	DllExport void __stdcall SetPreferredMepPack(const char* romSha1, const char* packId)
	{
		_emu->GetEnhancementPackManager()->SetPreferredMepPack(romSha1 ? romSha1 : "", packId ? packId : "");
		_emu->GetVideoRenderer()->InvalidateBorderAsset();
	}

	//W-P6: the layers ("textures,audio,patch") the player turned off for one
	//ROM, pushed beside the per-ROM pack choice; "" turns them all back on.
	DllExport void __stdcall SetMepRomLayersOff(const char* romSha1, const char* layers)
	{
		_emu->GetEnhancementPackManager()->SetRomLayersOff(romSha1 ? romSha1 : "", layers ? layers : "");
	}

	//P.3: the UI resets the per-ROM preferences (and W-P6's layer switches)
	//before re-pushing the current maps, so a choice removed from the config
	//is never left stale in the core.
	DllExport void __stdcall ClearPreferredMepPacks()
	{
		_emu->GetEnhancementPackManager()->ClearPreferredMepPacks();
	}

	DllExport void __stdcall SetRendererSize(uint32_t width, uint32_t height)
	{
		if(_emu->GetVideoRenderer()) {
			_emu->GetVideoRenderer()->SetRendererSize(width, height);
		}
	}

	DllExport double __stdcall GetAspectRatio()
	{
		return _emu->GetSettings()->GetAspectRatio(_emu->GetRegion(), _emu->GetVideoDecoder()->GetBaseFrameInfo(true));
	}

	DllExport FrameInfo __stdcall GetBaseScreenSize()
	{
		if(_emu->GetVideoDecoder()) {
			return _emu->GetVideoDecoder()->GetBaseFrameInfo(true);
		}
		return { 256, 240 };
	}

	DllExport uint32_t __stdcall GetGameMemorySize(MemoryType type)
	{
		return _emu->GetMemory(type).Size;
	}

	DllExport void __stdcall ClearCheats()
	{
		_emu->GetCheatManager()->ClearCheats();
	}

	DllExport void __stdcall SetCheats(CheatCode codes[], uint32_t length)
	{
		_emu->GetCheatManager()->SetCheats(codes, length);
	}

	DllExport bool __stdcall GetConvertedCheat(CheatCode input, InternalCheatCode& output)
	{
		return _emu->GetCheatManager()->GetConvertedCheat(input, output);
	}

	DllExport void __stdcall GetRomHash(HashType hashType, char* outBuffer, uint32_t maxLength)
	{
		StringUtilities::CopyToBuffer(_emu->GetHash(hashType), outBuffer, maxLength);
	}

	DllExport void __stdcall InputBarcode(uint64_t barcode, uint32_t digitCount)
	{
		_emu->InputBarcode(barcode, digitCount);
	}

	DllExport void __stdcall ProcessTapeRecorderAction(TapeRecorderAction action, char* filename)
	{
		_emu->ProcessTapeRecorderAction(action, filename);
	}

	DllExport void __stdcall ExecuteShortcut(ExecuteShortcutParams params)
	{
		_emu->GetNotificationManager()->SendNotification(ConsoleNotificationType::ExecuteShortcut, &params);
	}

	//#787: the release half of ExecuteShortcut. Without it a host could press a
	//shortcut and never let go, so a shortcut that arms something until release -
	//RunSingleFrame, which re-arms its pause every 50 ms while held - could not be
	//driven from outside the Core at all, tests included. The Core already sends
	//this notification on a real key release (ShortcutKeyHandler::CheckMappedKeys);
	//this is the same call, reachable by a host.
	DllExport void __stdcall ReleaseShortcut(ExecuteShortcutParams params)
	{
		_emu->GetNotificationManager()->SendNotification(ConsoleNotificationType::ReleaseShortcut, &params);
	}

	DllExport bool __stdcall IsShortcutAllowed(EmulatorShortcut shortcut, uint32_t shortcutParam)
	{
		return _emu->GetShortcutKeyHandler()->IsShortcutAllowed(shortcut, shortcutParam);
	}

	DllExport void __stdcall WriteLogEntry(char* message)
	{
		MessageManager::Log(message);
	}

	DllExport void __stdcall SaveState(uint32_t stateIndex)
	{
		_emu->GetSaveStateManager()->SaveState(stateIndex);
	}

	DllExport void __stdcall LoadState(uint32_t stateIndex)
	{
		_emu->GetSaveStateManager()->LoadState(stateIndex);
	}
	DllExport void __stdcall SaveStateFile(char* filepath)
	{
		_emu->GetSaveStateManager()->SaveState(filepath);
	}

	DllExport void __stdcall LoadStateFile(char* filepath)
	{
		_emu->GetSaveStateManager()->LoadState(filepath);
	}

	DllExport void __stdcall LoadRecentGame(char* filepath, bool resetGame)
	{
		_emu->GetSaveStateManager()->LoadRecentGame(filepath, resetGame);
	}

	DllExport int32_t __stdcall GetSaveStatePreview(char* saveStatePath, uint8_t* pngData)
	{
		return _emu->GetSaveStateManager()->GetSaveStatePreview(saveStatePath, pngData);
	}

	class PgoKeyManager : public IKeyManager
	{
	public:
		void RefreshState() {}
		void UpdateDevices() {}
		bool IsMouseButtonPressed(MouseButton button) { return false; }
		bool IsKeyPressed(uint16_t keyCode) { return keyCode == 10 && (_emu->GetFrameCount() % 7) <= 3; }

		vector<uint16_t> GetPressedKeys() { return {}; }
		string GetKeyName(uint16_t keyCode) { return ""; }
		uint16_t GetKeyCode(string keyName) { return 0; }

		bool SetKeyState(uint16_t scanCode, bool state) { return false; }
		void ResetKeyState() {}
		void SetDisabled(bool disabled) {}
	};

	DllExport void __stdcall PgoRunTest(vector<string> testRoms, bool enableDebugger)
	{
		FolderUtilities::SetHomeFolder("../PGOMesenHome");
		PgoKeyManager pgoKeyManager;
		KeyManager::RegisterKeyManager(&pgoKeyManager);

		for(size_t i = 0; i < testRoms.size(); i++) {
			std::cout << "Running: " << testRoms[i] << std::endl;

			KeyManager::SetSettings(_emu->GetSettings());
			_emu->Initialize();

			//Map key #10 to the start button for all consoles - this key is toggled on/off every 4 frames
			NesConfig& nesCfg = _emu->GetSettings()->GetNesConfig();
			nesCfg.Port1.Type = ControllerType::NesController;
			nesCfg.Port1.Keys.Mapping1.Start = 10;

			SnesConfig& snesCfg = _emu->GetSettings()->GetSnesConfig();
			snesCfg.Port1.Type = ControllerType::SnesController;
			snesCfg.Port1.Keys.Mapping1.Start = 10;

			GameboyConfig& gbCfg = _emu->GetSettings()->GetGameboyConfig();
			gbCfg.Model = GameboyModel::GameboyColor;
			gbCfg.Controller.Keys.Mapping1.Start = 10;

			PcEngineConfig& pceCfg = _emu->GetSettings()->GetPcEngineConfig();
			pceCfg.Port1.Type = ControllerType::PceController;
			pceCfg.Port1.Keys.Mapping1.Start = 10;

			_emu->GetSettings()->SetFlag(EmulationFlags::MaximumSpeed);
			_emu->LoadRom((VirtualFile)testRoms[i], VirtualFile());

			if(enableDebugger) {
				//turn on debugger to profile the debugger's code too
				_emu->GetDebugger(true);
			}

			std::this_thread::sleep_for(std::chrono::duration<int, std::milli>(5000));
			std::cout << "Ran for " << _emu->GetFrameCount() << " frames" << std::endl;

			_emu->Stop(false);
			_emu->Release();
		}
	}
}