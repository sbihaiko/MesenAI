#pragma once
#include "pch.h"
#include "Sdl/include/Sdl2.h"
#include "Core/Shared/Audio/AsyncAudioDeviceOpen.h"
#include "Core/Shared/Audio/MenuSoundStream.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/MessageManager.h"

class Emulator;

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
	explicit SdlMenuSoundSink(Emulator* emu)
	{
		_emu = emu;
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

		SDL_InitSubSystem(SDL_INIT_AUDIO);

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
		//through the output device and audio stack they already chose.
		const char* configuredDevice = _emu->GetSettings()->GetAudioConfig().AudioDevice;
		_deviceName = configuredDevice != nullptr ? configuredDevice : "";
		_deviceOpen.Start(_deviceName, open, log);
	}

	bool TryTakeOpen(bool& opened) override
	{
		if(_noSdl) {
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
		return _audioDeviceID != 0;
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

	static constexpr int CallbackFrames = 1024;

	Emulator* _emu = nullptr;
	IMenuSoundReader* _reader = nullptr;
	AsyncAudioDeviceOpen _deviceOpen;
	string _deviceName;
	SDL_AudioDeviceID _audioDeviceID = 0;
	bool _noSdl = false;
};
