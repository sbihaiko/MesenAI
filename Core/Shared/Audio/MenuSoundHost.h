#pragma once
#include "Core/Shared/Audio/MenuSoundStream.h"

#include <functional>
#include <memory>
#include <mutex>
#include <string>

//ADR-0270 D9's "Traps" (issue #1126): the stream's lifetime across a settings
//change. A sink is opened once for the session, so an arm that is never revisited
//keeps the blip on whatever device the stream got at startup: picking another
//output device leaves the game on the new device and the blips on the old one
//(D2 - the blip goes out through "the output device the player already picked"),
//and switching the backend to DirectSound keeps playing blips through WASAPI (D9
//- "never through an audio API the player did not pick"). The settings apply
//path therefore re-arms: the old stream is stopped - which joins its owner thread
//and lets that thread release the old device (D4) - and a new stream is built
//from a fresh sink and started.
//
//Host-free: the device is a factory the caller hands in, so
//scripts/core_unit_tests.cpp changes the device and the backend under a fake and
//watches the next blip land on the new device.
class MenuSoundHost
{
public:
	//Where the stream is armed: which output device the player picked, and which
	//backend. The backend is the C#-facing enum as an int
	//(Core/Shared/SettingTypes.h's AudioBackendType: Default, Wasapi,
	//DirectSound, Sdl2), kept an int here because this unit is host-free.
	struct Arming
	{
		std::string Device;
		int Backend = 0;

		bool operator==(const Arming& other) const { return Backend == other.Backend && Device == other.Device; }
		bool operator!=(const Arming& other) const { return !(*this == other); }
	};

	//The sink for one arming, or null when that backend has no menu sink - which
	//is D9's "unavailable" answer, never a fallback to a different audio API.
	using SinkFactory = std::function<std::unique_ptr<IMenuSoundSink>(const Arming& arming)>;

	MenuSoundHost(SinkFactory sinkFactory, MenuSoundStream::AudioEnabledFn audioEnabled, MenuSoundStream::MasterVolumeFn masterVolume, MenuSoundStream::GameRunningFn gameRunning, MenuSoundStream::LogFn log)
		: _sinkFactory(std::move(sinkFactory)), _audioEnabled(std::move(audioEnabled)), _masterVolume(std::move(masterVolume)), _gameRunning(std::move(gameRunning)), _log(std::move(log))
	{
	}

	//Arms the stream on this device and backend, and re-arms it when either one
	//changed. Safe to call on every settings apply: an unchanged arming leaves the
	//running stream alone, because the player saving the audio sheet is not a
	//device change.
	void Apply(const Arming& arming)
	{
		std::lock_guard<std::mutex> lock(_applyLock);
		std::shared_ptr<MenuSoundStream> current = std::atomic_load(&_stream);
		if(current != nullptr && _armed == arming) {
			return;
		}

		if(current != nullptr) {
			//The old stream stops before the new one is built: its own thread is
			//the one that releases the old device, and it has been joined by the
			//time the fresh sink is created (D4 - "re-arming means the old device
			//is released only after its own thread is joined").
			current->Stop();
		}

		Arm(arming);
	}

	void Stop()
	{
		std::lock_guard<std::mutex> lock(_applyLock);
		std::shared_ptr<MenuSoundStream> stream = std::atomic_load(&_stream);
		std::atomic_store(&_stream, std::shared_ptr<MenuSoundStream>());
		if(stream != nullptr) {
			stream->Stop();
		}
	}

	//D9: answerable from any thread at any time, and without opening anything.
	//The stream is read through a shared pointer so a re-arm cannot pull it out
	//from under a reader that is already inside it.
	bool IsAvailable() const
	{
		std::shared_ptr<MenuSoundStream> stream = std::atomic_load(&_stream);
		return stream != nullptr && stream->IsAvailable();
	}

	//D4: the producer's only entry point, forwarded to the armed stream.
	bool Submit(const int16_t* pcm, uint32_t frameCount, uint32_t sampleRate)
	{
		std::shared_ptr<MenuSoundStream> stream = std::atomic_load(&_stream);
		return stream != nullptr && stream->Submit(pcm, frameCount, sampleRate);
	}

private:
	void Arm(const Arming& arming)
	{
		_armed = arming;

		std::unique_ptr<IMenuSoundSink> sink = _sinkFactory(arming);
		std::shared_ptr<MenuSoundStream> stream;
		if(sink != nullptr) {
			stream = std::make_shared<MenuSoundStream>(std::move(sink), _audioEnabled, _masterVolume, _gameRunning, _log);
		}

		//Published before the owner thread starts: until the device opens the
		//stream answers unavailable, so a press that lands during the open is
		//refused rather than queued (D9).
		std::atomic_store(&_stream, stream);
		if(stream != nullptr) {
			stream->Start();
		}
	}

	SinkFactory _sinkFactory;
	MenuSoundStream::AudioEnabledFn _audioEnabled;
	MenuSoundStream::MasterVolumeFn _masterVolume;
	MenuSoundStream::GameRunningFn _gameRunning;
	MenuSoundStream::LogFn _log;

	//Apply is serialized against itself; the producer never takes this lock.
	std::mutex _applyLock;
	//Atomic load/store on the shared pointer: Apply swaps it, everything else
	//reads it.
	std::shared_ptr<MenuSoundStream> _stream;
	Arming _armed;
};
