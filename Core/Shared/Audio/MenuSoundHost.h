#pragma once
#include "Core/Shared/Audio/MenuSoundArming.h"
#include "Core/Shared/Audio/MenuSoundStream.h"

#include <functional>
#include <memory>
#include <mutex>

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
//The stream is armed by an apply and by nothing else. A host that is only
//constructed opens no device at all, and the arming that reaches the factory is
//the one the caller's apply carried - never a default of the host's own. That is
//what keeps the app's first arm on the player's own device and backend: the host
//is built as the app starts, and the first audio apply, which carries the
//settings the player actually has, is what opens the device. An arm taken at
//construction would open the default endpoint first and tear it down again on
//that apply, which costs the first press after launch its blip while the new
//device opens (AC1).
//
//#1153: and neither Apply nor the stream's own Stop ever waits. Both run on the
//UI thread, and both replace a stream whose own device may still be opening -
//the case that put a ~10 s stall on the emulation thread in #733 - so the
//replaced stream is taken out of service by the publish alone and its own thread
//finishes the release (D9, D4). No join happens here, and none happens under
//_applyLock either: the lock only serializes applies against each other, and
//every call made while it is held returns at once.
//
//#1153 review: the process teardown is the one exception, and it is a deliberate
//one. EmuApiWrapper::Release destroys the emulator the policy lambdas capture
//right after it stops the host, so a detached owner thread would read freed
//memory; that caller uses StopAndWait, which returns only once the owner thread
//has left OwnerLoop. The wait is taken outside _applyLock, so it blocks no apply.
//
//Host-free: the device is a factory the caller hands in, so
//scripts/core_unit_tests.cpp changes the device and the backend under a fake and
//watches the next blip land on the new device.
class MenuSoundHost
{
public:
	//The sink for one arming, or null when that backend has no menu sink - which
	//is D9's "unavailable" answer, never a fallback to a different audio API.
	using SinkFactory = std::function<std::unique_ptr<IMenuSoundSink>(const MenuSoundArming& arming)>;

	MenuSoundHost(SinkFactory sinkFactory, MenuSoundStream::AudioEnabledFn audioEnabled, MenuSoundStream::MasterVolumeFn masterVolume, MenuSoundStream::GameRunningFn gameRunning, MenuSoundStream::LogFn log)
		: _sinkFactory(std::move(sinkFactory)), _audioEnabled(std::move(audioEnabled)), _masterVolume(std::move(masterVolume)), _gameRunning(std::move(gameRunning)), _log(std::move(log))
	{
	}

	//Arms the stream on this device and backend on the first call, and re-arms it
	//when either one changed after that. Safe to call on every settings apply: an
	//unchanged arming leaves the running stream alone, because the player saving
	//the audio sheet is not a device change.
	void Apply(const MenuSoundArming& arming)
	{
		//The replaced stream is released once the lock is gone: dropping the last
		//reference to a stream whose own thread already exited runs its destructor
		//here, and that is work this call has no reason to do under a lock.
		std::shared_ptr<MenuSoundStream> previous;
		{
			std::lock_guard<std::mutex> lock(_applyLock);
			std::shared_ptr<MenuSoundStream> current = std::atomic_load(&_stream);
			if(current != nullptr && _armed == arming) {
				return;
			}

			//D9 (#1153): the publish is what takes the old stream out of service,
			//and Stop below returns at once - it signals the owner thread and
			//detaches it, so the old device is released by that thread on its way
			//out (D4) and never by this one. Nothing here waits for a device, not
			//even for an open the old stream is still running: that is the stall
			//#1153 removes from the settings-apply path.
			std::atomic_store(&_stream, std::shared_ptr<MenuSoundStream>());
			previous = std::move(current);
			if(previous != nullptr) {
				previous->Stop();
			}

			Arm(arming);
		}
	}

	void Stop()
	{
		//Same rule as Apply: the stream is unpublished here and the release is its
		//own thread's, so the caller - the UI thread tearing the app down, or the
		//headless runner's Release() - is never held by a device's open (D9).
		std::shared_ptr<MenuSoundStream> stream;
		{
			std::lock_guard<std::mutex> lock(_applyLock);
			stream = std::atomic_load(&_stream);
			std::atomic_store(&_stream, std::shared_ptr<MenuSoundStream>());
		}

		if(stream != nullptr) {
			stream->Stop();
		}
	}

	//#1153 review: the teardown's own stop. EmuApiWrapper::Release destroys the
	//emulator the stream's policy lambdas capture on its next line, so a detached
	//owner thread still inside OwnerLoop would read freed memory. This waits for
	//that thread - the release included - and is the one caller where the wait is
	//free: Release runs as the process goes away, not on a UI press (#733).
	//The wait happens outside _applyLock, which no thread of the stream's ever
	//takes, so it cannot deadlock an apply.
	void StopAndWait()
	{
		std::shared_ptr<MenuSoundStream> stream;
		{
			std::lock_guard<std::mutex> lock(_applyLock);
			stream = std::atomic_load(&_stream);
			std::atomic_store(&_stream, std::shared_ptr<MenuSoundStream>());
		}

		if(stream != nullptr) {
			//Held across the wait: the stream owns the latch being waited on, and
			//the owner thread's own reference is gone the moment it exits.
			stream->StopAndWait();
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
	void Arm(const MenuSoundArming& arming)
	{
		std::unique_ptr<IMenuSoundSink> sink = _sinkFactory(arming);
		std::shared_ptr<MenuSoundStream> stream;
		if(sink != nullptr) {
			//Shared-owned: the stream's owner thread keeps a reference for as long
			//as it runs, so Stop can detach it rather than join it (D9, #1153).
			stream = std::make_shared<MenuSoundStream>(std::move(sink), _audioEnabled, _masterVolume, _gameRunning, _log);
		}

		//Published before the owner thread starts: until the device opens the
		//stream answers unavailable, so a press that lands during the open is
		//refused rather than queued (D9).
		std::atomic_store(&_stream, stream);
		//And only once it is published: an arming recorded before the factory ran
		//would describe a stream that never came to exist, and every later apply
		//carrying the same settings would return early on that lie.
		_armed = arming;
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
	//Until the first apply this is only the default value; nothing opens on it.
	MenuSoundArming _armed;
};
