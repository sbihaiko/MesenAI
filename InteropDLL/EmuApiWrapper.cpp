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
#include "Core/Shared/Audio/MenuSoundHost.h"
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
	#include "Windows/WasapiMenuSoundSink.h"
	#include "Windows/WindowsKeyManager.h"
	#include "Windows/WindowsMouseManager.h"
#elif __APPLE__
	#include "MacOS/MacOSMetalRenderer.h"
	#include "Sdl/SdlSoundManager.h"
	#include "Sdl/SdlMenuSoundSink.h"
	#include "MacOS/MacOSKeyManager.h"
	#include "MacOS/MacOSMouseManager.h"
#else
	#include "Linux/LinuxOglRenderer.h"
	#include "Sdl/SdlSoundManager.h"
	#include "Sdl/SdlMenuSoundSink.h"
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
//ADR-0270 (issue #1126): the menu blip's own output stream (D2), behind the host
//that re-arms it when the player picks another device or backend (D9's "Traps").
//Heap-allocated and never destroyed for the same reason as the holders above
//(#621): a host that exits without Release() must not have the stream torn down
//under a thread that is still in flight.
unique_ptr<MenuSoundHost>& _menuSoundHost = *new unique_ptr<MenuSoundHost>();
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

//The backend reaches the host-free MenuSoundArming as an int, so the enum's
//values are pinned here.
static_assert((int)AudioBackendType::Default == 0 && (int)AudioBackendType::Wasapi == 1
	&& (int)AudioBackendType::DirectSound == 2 && (int)AudioBackendType::Sdl2 == 3,
	"MenuSoundArming::Backend mirrors AudioBackendType");

//ADR-0270 D6/D9 (#1153): the gate and the volume the menu stream reads, taken off
//the live AudioConfig on the thread that applies the settings rather than read
//from the stream's own threads. EmuSettings::SetAudioConfig assigns the whole
//config in place, so a device thread reading it races that assignment - the same
//race the sink carries its device and backend around to avoid (MenuSoundArming.h)
//- and the two values can only change through an audio apply, so refreshing the
//snapshot there answers exactly what a live read would, without the race.
std::atomic<bool> _menuSoundAudioEnabled{true};
std::atomic<uint32_t> _menuSoundMasterVolume{100};

//ADR-0270 D2/D9: where the stream is armed - the output device the player picked
//and the backend they picked. Read here, on the thread that applies the settings,
//and handed down to the sink: a sink thread that read AudioConfig itself would
//race the apply that rewrites it (MenuSoundArming.h). The device name is copied
//into the arming's own string here, so the const char* the apply path repoints
//under it is never held past this call.
MenuSoundArming CurrentMenuSoundArming()
{
	AudioConfig& config = _emu->GetSettings()->GetAudioConfig();
	_menuSoundAudioEnabled.store(config.EnableAudio, std::memory_order_release);
	_menuSoundMasterVolume.store(config.MasterVolume, std::memory_order_release);
	return MenuSoundArming{ config.AudioDevice != nullptr ? config.AudioDevice : "", (int)config.AudioBackend };
}

//ADR-0270 D9: the sink follows the backend the game device would use. A backend
//with no menu sink answers unavailable rather than routing the blip through an
//audio API the player did not pick.
unique_ptr<IMenuSoundSink> CreateMenuSoundSink(const MenuSoundArming& arming)
{
#ifdef _WIN32
	//DirectSound has no menu sink, so a player on it hears no menu sound rather
	//than a blip through a different audio API. Both returns name the same type,
	//since a deduced return type cannot mix nullptr with a unique_ptr.
	if(arming.Backend == (int)AudioBackendType::DirectSound) {
		return unique_ptr<IMenuSoundSink>();
	}
	return unique_ptr<IMenuSoundSink>(new WasapiMenuSoundSink(arming));
#else
	//SDL is the only backend on macOS and Linux.
	return unique_ptr<IMenuSoundSink>(new SdlMenuSoundSink(arming));
#endif
}

//ADR-0270 D9 (issue #1126): the menu stream is built here, at app start, and
//armed by the first audio apply - never lazily by the first blip, which drops it
//(the #1117 defect the panel rejected), and never on an arming of the app's own.
//
//Arming here would use whatever the settings hold before the UI has applied them
//(Device "", Backend Default) and the first SetAudioConfig would then tear that
//device down and open the player's: on Windows a WASAPI client would open for a
//player who picked DirectSound (D9 - "never through an audio API the player did
//not pick"), the default endpoint would open for a player who picked another
//device (D2), and while the second open ran MenuSoundsAvailable() would be false,
//so the first press after launch would play nothing (AC1). The first apply
//carries the settings the player actually has, so it is the one that arms.
//
//Ordering is still guaranteed for D9's non-thread-safe SDL subsystem init: it
//runs here, on the caller of InitializeEmu, ahead of the emulator's own audio
//init and before any sink thread exists.
void PrepareMenuSoundStream()
{
	if(_menuSoundHost) {
		return;
	}

#ifndef _WIN32
	//ADR-0270 D9: SDL2's subsystem init is not thread-safe, and the emulator's own
	//audio-init path runs it too (SdlSoundManager::InitializeAudio). It runs here
	//- on the caller of InitializeEmu, ahead of the emulator's audio init and
	//before the stream's own thread exists - so the two can never be in flight at
	//once, which is the window a first press after launch lands in. A nonzero
	//result is remembered and fails the menu stream's open instead of opening
	//against an uninitialized subsystem.
	SdlMenuSoundSink::InitializeAudioSubsystem();
#endif

	Emulator* emu = _emu.get();
	_menuSoundHost.reset(new MenuSoundHost(
		[](const MenuSoundArming& arming) { return CreateMenuSoundSink(arming); },
		//D6: audio off means the app is silent, game and menu alike; the volume is
		//always cfg.MasterVolume, never the audio player's and never the game
		//path's ducking rules. Both are the snapshot the audio apply takes, because
		//the volume is read on the device's own thread at fill time (#1153).
		[]() { return _menuSoundAudioEnabled.load(std::memory_order_acquire); },
		[]() { return _menuSoundMasterVolume.load(std::memory_order_acquire); },
		//D7: the stream's own cut, on the predicate the caller's gate uses.
		[emu]() { return emu->IsRunning() && !emu->IsPaused(); },
		[](const string& message) { MessageManager::Log(message); }));
}

//ADR-0270 D9's "Traps" (issue #1126): the settings apply path arms the stream on
//the settings the player actually has, and re-arms it when they pick another
//output device or another backend, so the blip follows them there - on the device
//they picked (D2) and never through an audio API they did not (D9). An unchanged
//arming leaves the running stream alone, and with no host (noAudio, or an apply
//before InitializeEmu) this is a no-op.
//Defined out of the exports for ConfigApiWrapper's SetAudioConfig, which is
//called from the same thread as every other settings change.
void ReArmMenuSoundStream()
{
	if(!_menuSoundHost) {
		return;
	}

	_menuSoundHost->Apply(CurrentMenuSoundArming());
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

			//ADR-0270 D9: the stream exists only when the host asked for audio at
			//all - the same flag that gates the game device. The headless test
			//runner passes noAudio: true, so there the capability is false and the
			//Audio sheet keeps its three rows. The host is built here, on this
			//thread - the SDL backend's subsystem init is not thread-safe and the
			//game device's own open runs the same call, so the menu stream claims it
			//before the emulator's audio device is even created (see
			//SdlMenuSoundSink::InitializeAudioSubsystem) - and the UI's audio apply
			//that follows arms it on the player's own settings
			//(PrepareMenuSoundStream).
			if(!noAudio) {
				PrepareMenuSoundStream();
			}

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

	//ADR-0270 D1 (issue #1126): the seam is unchanged - one whole blip, frameCount
	//frames of interleaved stereo int16 at sampleRate, exactly as
	//UI/Logic/MenuSounds.cs rendered it. D4: the caller may only submit; opening,
	//unpausing, settling, pausing and releasing belong to the stream's own thread.
	DllExport bool __stdcall PlayMenuSound(int16_t* samples, uint32_t frameCount, uint32_t sampleRate)
	{
		if(!_menuSoundHost) {
			return false;
		}
		return _menuSoundHost->Submit(samples, frameCount, sampleRate);
	}

	//D9: true only while the stream is up, so a failed open leaves the Menu sounds
	//row hidden. Answerable from any thread without opening anything.
	DllExport bool __stdcall MenuSoundsAvailable()
	{
		return _menuSoundHost && _menuSoundHost->IsAvailable();
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
		if(_menuSoundHost) {
			//ADR-0270 D2/D4/D9 (#1153): this device is not the emulator's, and it
			//is let go first, so the emulator's own teardown never runs under a
			//menu stream that is still opening a device of its own. The host's
			//owner thread closes this device on its way out (D4: the thread that
			//releases is the thread that opened it). The two devices share nothing
			//but the backend - the menu one is never routed through the emulator's
			//device, its ring or its pause ownership.
			//
			//#1153 review: and this is the one place that waits for that thread.
			//Stop alone detaches it, and the thread reads the running predicate
			//through a lambda that captured this emulator - which the next three
			//lines destroy. Waiting here is free: it is the process going away,
			//not the UI press #733 stalled. Same rule as the settings-apply path
			//otherwise: the device is not closed by this thread either way.
			_menuSoundHost->StopAndWait();
			_menuSoundHost.reset();
		}

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