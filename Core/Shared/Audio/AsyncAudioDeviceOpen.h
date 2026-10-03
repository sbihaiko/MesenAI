#pragma once
#include <atomic>
#include <cstdint>
#include <functional>
#include <string>
#include <thread>

//#733: opening an audio output device by name can block for seconds (a
//CoreAudio device behind a sleeping external monitor took ~10 s to fail inside
//SDL_OpenAudioDevice). The open used to run inline on the emulation thread,
//so the first frames of a game waited for it. This unit owns the open: the
//named-device-then-default fallback, and where the open runs. Host-free (no
//SDL), so core-unit-tests drives it with a fake slow device.
class AsyncAudioDeviceOpen
{
public:
	//Returns the opened device id, or 0 on failure. An empty name means the
	//system default device.
	using OpenFn = std::function<uint32_t(const std::string& deviceName)>;
	using LogFn = std::function<void(const std::string& message)>;

	//Tries the named device first and the default device when it fails. An
	//empty name tries the default device once.
	static uint32_t OpenWithFallback(const std::string& deviceName, const OpenFn& open, const LogFn& log)
	{
		uint32_t deviceId = open(deviceName);
		if(deviceId == 0 && !deviceName.empty()) {
			log("[Audio] Failed opening audio device '" + deviceName + "', will retry with default device.");
			deviceId = open(std::string());
		}
		return deviceId;
	}

	//Runs OpenWithFallback on a worker thread and returns at once. Must not be
	//called while an open is pending; take the result first.
	void Start(const std::string& deviceName, OpenFn open, LogFn log)
	{
		Join();
		_done = false;
		_pending = true;
		_worker = std::thread([this, deviceName, open = std::move(open), log = std::move(log)]() {
			_result = OpenWithFallback(deviceName, open, log);
			_done.store(true, std::memory_order_release);
		});
	}

	~AsyncAudioDeviceOpen()
	{
		Wait();
	}

	bool IsPending() const { return _pending; }

	//Non-blocking. True, with the opened id (0 when every open failed), once
	//the pending open finished; false while it is still running or when
	//nothing was started.
	bool TryTake(uint32_t& deviceId)
	{
		if(!_pending || !_done.load(std::memory_order_acquire)) {
			return false;
		}
		Join();
		_pending = false;
		deviceId = _result;
		return true;
	}

	//Blocks until a pending open finished. The result stays for TryTake.
	void Wait()
	{
		Join();
	}

private:
	void Join()
	{
		if(_worker.joinable()) {
			_worker.join();
		}
	}

	std::thread _worker;
	std::atomic<bool> _done{false};
	bool _pending = false;
	uint32_t _result = 0;
};
