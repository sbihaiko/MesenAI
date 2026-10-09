#pragma once
#include <functional>
#include <mutex>

//ADR-0270 D9 (issue #1126): the audio subsystem's own init, run once and with its
//result checked.
//
//SDL_InitSubSystem(SDL_INIT_AUDIO) is not thread-safe, and SdlSoundManager runs
//the same call from the emulator's audio-init path, so the menu stream must not
//run it on its own thread while that one may be in flight - which is exactly the
//window a first press after launch lands in. It runs on the caller that arms the
//stream (InitializeEmu, ahead of the emulator's own audio init and before the
//stream's thread exists), and the stream's own thread then finds the result
//instead of running the init again.
//
//A nonzero result is latched: the open fails with it, so the capability answers
//unavailable and the Menu sounds row stays hidden, rather than the open proceeding
//against an uninitialized subsystem. Host-free, so scripts/core_unit_tests.cpp
//drives both the once-only rule and the latch.
class MenuSoundAudioSubsystem
{
public:
	//SDL's own answer: zero when the subsystem is up.
	using InitFn = std::function<int()>;

	//True when the subsystem is up. Runs the init on the first ask only, and
	//answers false forever once that ask failed.
	bool Ensure(const InitFn& init) const
	{
		std::lock_guard<std::mutex> lock(_lock);
		if(!_attempted) {
			_attempted = true;
			_initialized = init() == 0;
		}
		return _initialized;
	}

	//True once an init was attempted and failed - the open is refused with it.
	bool Failed() const
	{
		std::lock_guard<std::mutex> lock(_lock);
		return _attempted && !_initialized;
	}

private:
	//Both callers can reach this - the arming caller and the stream's own thread -
	//and the init runs inside the lock, so the second one cannot enter the init
	//while the first is in it.
	mutable std::mutex _lock;
	mutable bool _attempted = false;
	mutable bool _initialized = false;
};
