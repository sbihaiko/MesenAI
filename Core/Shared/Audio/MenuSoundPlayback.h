#pragma once
#include "pch.h"
#include <mutex>
#include <thread>
#include <condition_variable>
#include <functional>
#include <chrono>
#include "Core/Shared/Interfaces/IAudioDevice.h"

//Host-free half of SoundMixer::PlayMenuSound: renders the menu blip at the
//configured output rate and makes sure the device is configured for that rate
//before the blip is queued. An IAudioDevice resets (and drops the buffer it was
//handed) whenever PlayBuffer's rate/channel layout differs from the one it was
//opened with, so a blip submitted in another format is lost on the first press
//and again on every press when the output rate is not the blip's own.
class MenuSoundPlayback
{
public:
	//Linear-interpolated stereo resample, scaled by the master volume (percent).
	static vector<int16_t> Render(const int16_t* samples, uint32_t frameCount, uint32_t sourceRate, uint32_t outputRate, uint32_t masterVolume)
	{
		vector<int16_t> out;
		if(frameCount == 0 || sourceRate == 0 || outputRate == 0) {
			return out;
		}

		uint32_t outFrames = (uint32_t)((uint64_t)frameCount * outputRate / sourceRate);
		out.resize((size_t)outFrames * 2);
		for(uint32_t i = 0; i < outFrames; i++) {
			double pos = (double)i * sourceRate / outputRate;
			uint32_t i0 = (uint32_t)pos;
			uint32_t i1 = i0 + 1 < frameCount ? i0 + 1 : i0;
			double frac = pos - i0;
			for(uint32_t ch = 0; ch < 2; ch++) {
				double v = samples[i0 * 2 + ch] * (1.0 - frac) + samples[i1 * 2 + ch] * frac;
				out[(size_t)i * 2 + ch] = (int16_t)(v * masterVolume / 100.0);
			}
		}
		return out;
	}

	//The format the device was last primed for. Values, not a flag: a changed
	//output rate or AudioLatency makes the device reset on the next PlayBuffer.
	struct State
	{
		uint32_t Rate = 0;
		uint32_t Latency = 0;
	};

	//Returns how long the blip plays, in ms (0 when nothing was queued).
	static uint32_t Play(IAudioDevice* device, State& state, const int16_t* samples, uint32_t frameCount, uint32_t sourceRate, uint32_t outputRate, uint32_t masterVolume, uint32_t latency = 0)
	{
		vector<int16_t> rendered = Render(samples, frameCount, sourceRate, outputRate, masterVolume);
		if(rendered.empty()) {
			return 0;
		}

		if(state.Rate != outputRate || state.Latency != latency) {
			//Configure the device in the output format first; the reset this
			//triggers drops only this silent frame, not the blip.
			int16_t silence[2] = {};
			device->PlayBuffer(silence, 1, outputRate, true);
			//A device that reopens in the background drops writes until the open
			//finishes; wait so the blip is not sent into a pending open.
			device->WaitUntilReady();
			state.Rate = outputRate;
			state.Latency = latency;
		}
		device->PlayBuffer(rendered.data(), (uint32_t)(rendered.size() / 2), outputRate, true);
		return (uint32_t)((uint64_t)(rendered.size() / 2) * 1000 / outputRate);
	}
};

//PlayBuffer starts the device once enough audio is queued and the device loops
//its ring until told otherwise, so with no game running nothing would ever stop
//it. This pauses the device once the last blip has drained. The pause runs under
//the caller's device lock. The settle callback owns the device lookup (it runs
//under that lock and reads the registered device then), so no device pointer is
//held across the delay.
class MenuSoundSettler
{
private:
	std::mutex& _lock;
	std::condition_variable _cv;
	std::thread _thread;
	bool _stop = false;
	bool _pending = false;
	std::chrono::steady_clock::time_point _deadline;
	std::function<void()> _settle;

	void Run()
	{
		std::unique_lock<std::mutex> lk(_lock);
		while(!_stop) {
			if(!_pending) {
				_cv.wait(lk);
			} else if(_cv.wait_until(lk, _deadline) == std::cv_status::timeout && _pending && !_stop && std::chrono::steady_clock::now() >= _deadline) {
				_pending = false;
				_settle();
			}
		}
	}

public:
	//Caller holds the device lock. Drops a settle that has not fired yet.
	void Cancel()
	{
		_pending = false;
	}

	explicit MenuSoundSettler(std::mutex& deviceLock) : _lock(deviceLock) {}

	~MenuSoundSettler()
	{
		{
			std::lock_guard<std::mutex> lk(_lock);
			_stop = true;
		}
		_cv.notify_all();
		if(_thread.joinable()) {
			_thread.join();
		}
	}

	//Caller holds the device lock.
	void Schedule(uint32_t delayMs, std::function<void()> settle)
	{
		_settle = settle;
		_deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(delayMs);
		_pending = true;
		if(!_thread.joinable()) {
			_thread = std::thread([this]() { Run(); });
		}
		_cv.notify_all();
	}
};
