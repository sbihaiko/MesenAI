#pragma once
#include "pch.h"
#include "Sdl/include/Sdl2.h"
#include "Core/Shared/Audio/AsyncAudioDeviceOpen.h"
#include "Core/Shared/Audio/MenuSoundArming.h"
#include "Core/Shared/Audio/MenuSoundAudioSubsystem.h"
#include "Core/Shared/Audio/MenuSoundSinkHealth.h"
#include "Core/Shared/Audio/MenuSoundStream.h"
#include "Core/Shared/MessageManager.h"

//ADR-0270 D9 (issue #1126): the menu-sound sink on the SDL backends - macOS and
//Linux, the same pair the game device uses (D2: the blip goes out through the
//output device and audio stack the player already picked).
//
//It is the second output device, never a second writer on the game device's ring:
//its own SDL device, its own callback, its own buffer, and the emulator's device
//keeps its one writer and its one owner of play/pause. D9's rule - the open runs
//off the UI thread and is never awaited by it - is AsyncAudioDeviceOpen, the
//mechanism the game device already uses, so the menu device cannot stall a UI
//press the way a bad CoreAudio device stalled the emulation thread (#733).
class SdlMenuSoundSink final : public IMenuSoundSink
{
public:
	//The statuses the liveness latch reads are SDL's own, and the values are
	//pinned against the loader's copy of SDL_AudioStatus (Sdl/include/Sdl2.h).
	static_assert(MenuSoundSinkHealth::AudioStopped == SDL_AUDIO_STOPPED
		&& MenuSoundSinkHealth::AudioPlaying == SDL_AUDIO_PLAYING
		&& MenuSoundSinkHealth::AudioPaused == SDL_AUDIO_PAUSED,
		"MenuSoundSinkHealth's audio statuses mirror SDL_AudioStatus");

	//The device and the backend travel in with the sink, read once on the thread
	//that applied the settings. Every thread below - the stream's owner thread and
	//the open's own - reads them from here and never from AudioConfig, which the
	//settings apply path rewrites under them (MenuSoundArming.h).
	explicit SdlMenuSoundSink(const MenuSoundArming& arming)
		: _deviceName(arming.Device)
	{
	}

	//ADR-0270 D9: SDL's audio subsystem init, run once and on the caller that
	//arms the stream - before the stream's own thread exists, and ahead of the
	//emulator's audio-init path, which runs the same call from
	//SdlSoundManager::InitializeAudio. SDL2's subsystem init is not thread-safe,
	//so the two must never be in flight at once, which is exactly the window a
	//first press after launch lands in. False when SDL is absent or the init came
	//back nonzero: every sink then fails its open instead of opening against an
	//uninitialized subsystem (the capability stays false and the row stays hidden).
	static bool InitializeAudioSubsystem()
	{
		if(!LoadSdl()) {
			return false;
		}
		return SharedSubsystem().Ensure(&SdlMenuSoundSink::InitAudioSubsystem);
	}

	~SdlMenuSoundSink() override
	{
		Release();
	}

	void SetReader(IMenuSoundReader* reader) override
	{
		_reader = reader;
	}

	void BeginOpen() override
	{
		if(!LoadSdl()) {
			//No SDL at all: there is no device to wait for, so TryTakeOpen answers
			//"failed" on its first call and the capability stays false (D9).
			_noSdl = true;
			return;
		}

		//The init already ran on the caller that armed the stream; this ask finds
		//that result instead of running it here, on the stream's own thread. A
		//nonzero result fails the open (D9), and so does a latched earlier failure.
		if(!SharedSubsystem().Ensure(&SdlMenuSoundSink::InitAudioSubsystem)) {
			_openFailed = true;
			return;
		}

		SDL_AudioSpec audioSpec;
		memset(&audioSpec, 0, sizeof(audioSpec));
		audioSpec.freq = MenuSoundStream::DeviceSampleRate;
		audioSpec.format = AUDIO_S16SYS;
		audioSpec.channels = 2;
		//D8: a UI tick wants an immediate answer, so the menu device uses a small
		//fixed buffer and is not sized from AudioConfig::AudioLatency.
		audioSpec.samples = CallbackFrames;
		audioSpec.callback = &SdlMenuSoundSink::FillAudioBuffer;
		audioSpec.userdata = this;

		//The device opens paused, so the callback cannot run before the stream's
		//owner thread unpauses it (D4).
		AsyncAudioDeviceOpen::OpenFn open = [audioSpec](const string& deviceName) -> uint32_t {
			SDL_AudioSpec obtainedSpec;
			return SDL_OpenAudioDevice(deviceName.empty() ? nullptr : deviceName.c_str(), 0, &audioSpec, &obtainedSpec, 0);
		};
		AsyncAudioDeviceOpen::LogFn log = [](const string& message) { MessageManager::Log(message); };

		//D2: the same device the player picked for the game, so the blip goes out
		//through the output device and audio stack they already chose. The name came
		//in with the sink, on the thread that applied the settings - reading it here
		//would be an off-thread read of a field the apply path reassigns
		//(MenuSoundArming.h).
		_deviceOpen.Start(_deviceName, open, log);
	}

	bool TryTakeOpen(bool& opened) override
	{
		if(_noSdl || _openFailed) {
			//No SDL at all, or a subsystem that never came up: there is no device
			//to wait for, so the first call answers "failed" and the capability
			//stays false (D9).
			opened = false;
			return true;
		}

		uint32_t deviceId = 0;
		if(!_deviceOpen.TryTake(deviceId)) {
			return false;
		}

		_audioDeviceID = deviceId;
		opened = deviceId != 0;
		return true;
	}

	void Unpause() override
	{
		if(_audioDeviceID != 0) {
			SDL_PauseAudioDevice(_audioDeviceID, 0);
		}
	}

	void Pause() override
	{
		if(_audioDeviceID != 0) {
			SDL_PauseAudioDevice(_audioDeviceID, 1);
		}
	}

	bool IsAlive() const override
	{
		if(_audioDeviceID == 0) {
			return false;
		}

		//D9: a device that is gone reports SDL_AUDIO_STOPPED - unplugged, or the
		//subsystem taken down under it - and that flips the capability back. A
		//paused device is alive, because this sink parks its device paused between
		//blips (D8). A build without the probe keeps the open-device answer rather
		//than losing the game device's audio over it (Sdl/include/Sdl2.h).
		if(SDL_GetAudioDeviceStatus != nullptr) {
			_health.ObservedAudioStatus((int)SDL_GetAudioDeviceStatus(_audioDeviceID));
		}
		return _health.IsAlive();
	}

	uint32_t BufferFrames() const override
	{
		//SDL double-buffers the requested sample count.
		return (uint32_t)CallbackFrames * 2;
	}

	void Release() override
	{
		if(_deviceOpen.IsPending()) {
			//The open can outlive the stream: Wait() joins it here, on the owner
			//thread, so the device it returns is closed rather than leaked with a
			//callback pointing at a stream that is going away.
			_deviceOpen.Wait();
			_deviceOpen.TryTake(_audioDeviceID);
		}

		if(_audioDeviceID != 0) {
			SDL_PauseAudioDevice(_audioDeviceID, 1);
			SDL_CloseAudioDevice(_audioDeviceID);
			_audioDeviceID = 0;
		}
		//The game device owns the SDL audio subsystem, so this sink only closes the
		//one device it opened - quitting the subsystem here would tear down the
		//game's device too, which is exactly the second-owner defect D2 removes.
	}

private:
	//D3: the reader. SDL calls this on its own thread, only while the device is
	//unpaused; it forwards to the stream, which emits silence when its queue is
	//empty rather than replaying the game ring's stale block.
	static void FillAudioBuffer(void* userData, uint8_t* stream, int len)
	{
		SdlMenuSoundSink* sink = (SdlMenuSoundSink*)userData;
		if(sink->_reader == nullptr || len <= 0) {
			return;
		}

		sink->_reader->ReadMenuSound((int16_t*)stream, (uint32_t)len / (2 * sizeof(int16_t)));
	}

	//SDL's own zero-on-success answer, so the init can be handed to the run-once
	//unit as it is.
	static int InitAudioSubsystem()
	{
		return (int)SDL_InitSubSystem(SDL_INIT_AUDIO);
	}

	//One per process: SDL refcounts the subsystem process-wide, and the result of
	//its init belongs to the process rather than to one sink, so a re-arm finds the
	//same answer instead of initializing the subsystem again.
	static MenuSoundAudioSubsystem& SharedSubsystem()
	{
		static MenuSoundAudioSubsystem subsystem;
		return subsystem;
	}

	static constexpr int CallbackFrames = 1024;

	IMenuSoundReader* _reader = nullptr;
	AsyncAudioDeviceOpen _deviceOpen;
	//The device the player picked, carried in from the arming rather than read off
	//the live settings.
	string _deviceName;
	SDL_AudioDeviceID _audioDeviceID = 0;
	//D9: one-way, fed by the device's own status.
	MenuSoundSinkHealth _health;
	bool _noSdl = false;
	bool _openFailed = false;
};
