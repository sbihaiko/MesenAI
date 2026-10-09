#pragma once
#include <atomic>
#include <cstdint>

//ADR-0270 D9 (issue #1126): whether a backend's output device is still there.
//The two backends learn it differently - SDL polls its own device status, WASAPI
//reads it off a failed call - so both answers are pinned here, host-free, and each
//sink delegates to this unit: scripts/core_unit_tests.cpp drives the latch
//(one-way, and only the device-invalidated code is fatal) without SDL and without
//the Windows headers, which is the half the fake sink can never cover.
class MenuSoundSinkHealth
{
public:
	//SDL2's SDL_AudioStatus values. A device that is merely paused is not dead -
	//this sink parks its device paused between blips (D8) - so only SDL's
	//"stopped" means the device is gone.
	static constexpr int AudioStopped = 0x1010;
	static constexpr int AudioPlaying = 0x1011;
	static constexpr int AudioPaused = 0x1012;

	//AUDCLNT_E_DEVICE_INVALIDATED: what a WASAPI call answers once the endpoint is
	//gone. The Windows sink static_asserts this against the real constant.
	static constexpr int32_t DeviceInvalidated = (int32_t)0x88890004;

	//SDL's poll: only "stopped" is fatal, so a device that is paused between blips
	//stays alive.
	void ObservedAudioStatus(int status) const
	{
		if(status == AudioStopped) {
			MarkDead();
		}
	}

	//A WASAPI call's result: only the invalidated code is fatal, so a call that
	//failed on anything else does not take the device away with it.
	void ObservedResult(int32_t hr) const
	{
		if(hr == DeviceInvalidated) {
			MarkDead();
		}
	}

	bool IsAlive() const { return !_dead.load(std::memory_order_acquire); }

private:
	//One-way, like the capability it feeds: a stream that dies mid-session stays
	//dead, so a later call that happens to succeed cannot revive it (D9).
	void MarkDead() const { _dead.store(true, std::memory_order_release); }

	mutable std::atomic<bool> _dead{false};
};
