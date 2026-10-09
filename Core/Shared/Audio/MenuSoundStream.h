#pragma once
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <functional>
#include <memory>
#include <string>
#include <thread>

//ADR-0270 (issue #1126): the menu blip's own output stream, owned by the host
//audio layer. It has its own device, its own queue and its own thread, and it
//never registers with SoundMixer, never appears in IAudioDevice's contract and
//never touches the emulator's device, ring or pause ownership (D2). The module
//is host-free: the device is a IMenuSoundSink the caller hands in, so
//scripts/core_unit_tests.cpp drives the whole decision against a fake sink, and
//the SDL/WASAPI sinks live with their backends.
//
//The four rules the six defects of PR #1117 bought, in one place:
//  * one producer that only submits (D4) - Submit never opens, pauses, closes or
//    waits on a device, never allocates and never takes a lock;
//  * one owner thread for open / unpause / settle / pause / release (D4);
//  * an open that runs off the caller's thread and is never awaited by it (D9);
//  * a reader that emits silence once its queue is empty, never the last block
//    again (D3) - which is what makes a late pause cost nothing.

//ADR-0270 D3: the device's own reader. The sink's audio callback (SDL) or poll
//loop (WASAPI) pulls frames here, on the device's thread. Safe to call without
//the owner thread, never blocks, never allocates, never touches a device.
class IMenuSoundReader
{
public:
	virtual ~IMenuSoundReader() {}
	virtual void ReadMenuSound(int16_t* out, uint32_t frameCount) = 0;
};

//ADR-0270 D9: one backend's own output device, as the stream's owner thread sees
//it. The sink owns how the open runs - the mechanism is each backend's own
//(Core/Shared/Audio/AsyncAudioDeviceOpen.h for SDL, a worker of its own on
//WASAPI) - and nothing here is ever called from the producer.
class IMenuSoundSink
{
public:
	virtual ~IMenuSoundSink() {}

	//Before the owner thread starts: where this sink's device callback pulls PCM
	//from. Never null.
	virtual void SetReader(IMenuSoundReader* reader) = 0;

	//Starts the device open and returns at once. Called on the owner thread.
	virtual void BeginOpen() = 0;

	//Non-blocking. True once BeginOpen finished, reporting whether a device is
	//open. Owner thread only.
	virtual bool TryTakeOpen(bool& opened) = 0;

	//Owner thread only, and never while another call into this sink is in flight.
	virtual void Unpause() = 0;
	virtual void Pause() = 0;

	//False once the device is gone for good (unplugged, or the open failed): the
	//stream then answers "unavailable" and submits become no-ops (D9).
	virtual bool IsAlive() const = 0;

	//The sink's own buffer length in frames - D8's "+ the sink's own buffer".
	virtual uint32_t BufferFrames() const = 0;

	//D3: a backend whose device pulls instead of pushing (WASAPI has no callback)
	//feeds its own buffer here, on the owner thread. SDL's device calls back into
	//the reader on its own thread instead, so its implementation does nothing.
	virtual void Service() {}

	//Closes the device for the session. Owner thread, last. The sink joins its own
	//open worker (and its own reader) before returning, so nothing touches the
	//stream after Release (D4: the thread that joins before the device is freed).
	virtual void Release() = 0;
};

class MenuSoundStream final : public IMenuSoundReader
{
public:
	//The blip set is rendered at UI/Logic/MenuSounds.cs's SampleRate and the UI is
	//the only producer (D1), so the sink's device opens at this rate. The rate a
	//submit carries is the blip's own and only feeds the settle arithmetic (D8).
	static constexpr uint32_t DeviceSampleRate = 48000;

	//The longest blip is 90 ms (MenuSounds.Render), so one slot of 200 ms at the
	//device rate is bounded and far above any blip the seam can carry.
	static constexpr uint32_t CapacityFrames = DeviceSampleRate / 5;

	using AudioEnabledFn = std::function<bool()>;
	using MasterVolumeFn = std::function<uint32_t()>;
	using GameRunningFn = std::function<bool()>;
	using LogFn = std::function<void(const std::string&)>;

	MenuSoundStream(std::unique_ptr<IMenuSoundSink> sink, AudioEnabledFn audioEnabled, MasterVolumeFn masterVolume, GameRunningFn gameRunning, LogFn log);
	~MenuSoundStream();

	MenuSoundStream(const MenuSoundStream&) = delete;
	MenuSoundStream& operator=(const MenuSoundStream&) = delete;

	//Arms the stream: the owner thread starts and the open begins (D9). Returns at
	//once and is never awaited - the caller is the UI thread.
	void Start();

	//Stops the owner thread and releases the device on it. Idempotent.
	void Stop();

	//D9: true only while the stream is up. Answerable from any thread at any time,
	//without opening anything - the settings sheet asks it to decide whether the
	//Menu sounds row exists at all.
	bool IsAvailable() const { return _available.load(std::memory_order_acquire); }

	//D4: the one producer entry point. Allocation-free, lock-free, bounded, and it
	//returns at once. False when there is nothing to do about the blip: no stream,
	//audio off, a malformed blip, or one already in flight (D5).
	bool Submit(const int16_t* pcm, uint32_t frameCount, uint32_t sampleRate);

	//D3: the reader's half. Fills exactly frameCount interleaved-stereo frames and
	//emits silence once the queue is empty. Device thread only, one reader.
	void ReadMenuSound(int16_t* out, uint32_t frameCount) override;

private:
	//True while the blip published by _submitSeq == seq is still in flight: the
	//reader has not drained it (D5) and the owner has not cleared it. Both halves
	//are needed - a cleared blip must not block the next submit, and a drained one
	//must let the settle wait be restarted with a full queue (D8).
	bool InFlight(uint32_t seq) const
	{
		return _consumeSeq.load(std::memory_order_acquire) < seq
			&& _clearedSeq.load(std::memory_order_acquire) < seq;
	}

	//D8: the blip's own duration at its own rate, plus the sink's own buffer worth
	//of the same clock. Never a fixed constant and never a guess about latency.
	std::chrono::steady_clock::time_point DrainDeadline(uint32_t frames, uint32_t rate) const;

	void OwnerLoop();

	std::unique_ptr<IMenuSoundSink> _sink;
	AudioEnabledFn _audioEnabled;
	MasterVolumeFn _masterVolume;
	GameRunningFn _gameRunning;
	LogFn _log;

	//_submitSeq is the producer's alone, _consumeSeq the reader's, _clearedSeq the
	//owner thread's: one writer each, which is what keeps the producer lock-free.
	std::atomic<uint32_t> _submitSeq{0};
	std::atomic<uint32_t> _consumeSeq{0};
	std::atomic<uint32_t> _clearedSeq{0};
	std::atomic<bool> _available{false};

	//Two slots, alternating by sequence parity: the producer only writes the slot
	//the reader is not reading, because a blip is published (D5) before the next
	//one can be accepted.
	int16_t _slot[2][CapacityFrames * 2];
	uint32_t _slotFrames[2] = {};
	uint32_t _slotRate[2] = {};

	//Reader-owned.
	uint32_t _readSeq = 0;
	uint32_t _cursor = 0;

	std::thread _thread;
	bool _started = false;
	std::atomic<bool> _running{false};
	bool _unpaused = false;
};

inline MenuSoundStream::MenuSoundStream(std::unique_ptr<IMenuSoundSink> sink, AudioEnabledFn audioEnabled, MasterVolumeFn masterVolume, GameRunningFn gameRunning, LogFn log)
	: _sink(std::move(sink)), _audioEnabled(std::move(audioEnabled)), _masterVolume(std::move(masterVolume)), _gameRunning(std::move(gameRunning)), _log(std::move(log))
{
	std::memset(_slot, 0, sizeof(_slot));
	_sink->SetReader(this);
}

inline MenuSoundStream::~MenuSoundStream()
{
	Stop();
}

inline void MenuSoundStream::Start()
{
	if(_started) {
		return;
	}

	_started = true;
	_running.store(true, std::memory_order_release);
	_thread = std::thread([this]() { OwnerLoop(); });
}

inline void MenuSoundStream::Stop()
{
	if(!_started) {
		return;
	}

	_started = false;
	//The owner thread's exit path releases the device, so by the time this returns
	//the sink has joined its own open worker and its own reader.
	_running.store(false, std::memory_order_release);
	if(_thread.joinable()) {
		_thread.join();
	}
	_available.store(false, std::memory_order_release);
}

inline bool MenuSoundStream::Submit(const int16_t* pcm, uint32_t frameCount, uint32_t sampleRate)
{
	if(pcm == nullptr || frameCount == 0 || frameCount > CapacityFrames || sampleRate == 0) {
		return false;
	}

	//D6: while the audio is off the app is silent, game and menu alike, so the
	//blip is refused outright and nothing is queued.
	if(!IsAvailable() || !_audioEnabled()) {
		return false;
	}

	//D5: one blip at a time, and a submit while one is queued or playing is
	//dropped, not stacked. A blip that already drained leaves the settle wait
	//running, so this is the "restart it with a full queue" case of D8.
	if(InFlight(_submitSeq)) {
		return false;
	}

	//Parity: blip k lives in slot k & 1, and the reader reads slot (submitSeq - 1)
	//& 1, so a write never lands on the slot being read.
	uint32_t slot = _submitSeq & 1;
	std::memcpy(_slot[slot], pcm, (size_t)frameCount * 2 * sizeof(int16_t));
	_slotFrames[slot] = frameCount;
	_slotRate[slot] = sampleRate;

	//Published last, with a release store: the reader sees the frames and the rate
	//with the sequence that names this blip.
	_submitSeq++;
	return true;
}

inline void MenuSoundStream::ReadMenuSound(int16_t* out, uint32_t frameCount)
{
	if(out == nullptr || frameCount == 0) {
		return;
	}

	uint32_t submitted = _submitSeq.load(std::memory_order_acquire);
	uint32_t outIndex = 0;

	//D5: a blip is written frame by frame from its start, never resumed from the
	//middle - the cursor is reset the moment a different blip is current.
	if(InFlight(submitted)) {
		if(_readSeq != submitted) {
			_readSeq = submitted;
			_cursor = 0;
		}

		const int16_t* slot = _slot[(submitted - 1) & 1];
		uint32_t slotFrames = _slotFrames[(submitted - 1) & 1];
		while(outIndex < frameCount && _cursor < slotFrames) {
			out[outIndex * 2] = slot[_cursor * 2];
			out[outIndex * 2 + 1] = slot[_cursor * 2 + 1];
			_cursor++;
			outIndex++;
		}

		if(_cursor >= slotFrames) {
			//The whole blip is out: the queue is empty, so the producer may submit
			//again and the owner's settle wait can be restarted (D5/D8).
			_consumeSeq.store(submitted, std::memory_order_release);
		}
	}

	//D3: what is not queued is silence. The reader never repeats its last block,
	//so a pause that lands late outputs nothing and a pause that lands early costs
	//at most the tail of one blip.
	if(outIndex < frameCount) {
		std::memset(out + (size_t)outIndex * 2, 0, (size_t)(frameCount - outIndex) * 2 * sizeof(int16_t));
	}

	//D6: master volume, the same arithmetic the game path uses, applied only while
	//it is below 100, and read at fill time from the config the audio layer owns.
	uint32_t volume = _masterVolume();
	if(volume < 100) {
		for(uint32_t i = 0; i < frameCount * 2; i++) {
			out[i] = (int16_t)((int32_t)out[i] * (int32_t)volume / 100);
		}
	}
}

inline std::chrono::steady_clock::time_point MenuSoundStream::DrainDeadline(uint32_t frames, uint32_t rate) const
{
	uint64_t micros = 0;
	if(rate > 0) {
		micros = ((uint64_t)frames + _sink->BufferFrames()) * 1000000ull / rate;
	}
	return std::chrono::steady_clock::now() + std::chrono::microseconds(micros);
}

inline void MenuSoundStream::OwnerLoop()
{
	//D9: arming is never lazy and never on the UI thread. BeginOpen returns at
	//once, so this thread starts the open and polls for it; no lock is taken at
	//any point, and nothing here blocks a producer.
	_sink->BeginOpen();
	bool opened = false;
	while(_running.load(std::memory_order_acquire) && !_sink->TryTakeOpen(opened)) {
		std::this_thread::sleep_for(std::chrono::milliseconds(1));
	}

	if(!opened) {
		//A failed open - or a stop that landed mid-open - leaves the capability
		//false, and the Menu sounds row stays hidden (D9). One line for the
		//session, never one per press.
		_log("[Audio] Menu sounds are unavailable: the menu output device could not be opened.");
		_sink->Release();
		return;
	}

	_available.store(true, std::memory_order_release);

	while(_running.load(std::memory_order_acquire)) {
		uint32_t submitted = _submitSeq.load(std::memory_order_acquire);
		if(!InFlight(submitted)) {
			if(!_sink->IsAlive()) {
				//The device died mid-session: the capability flips back to false and
				//submits become no-ops (D9).
				break;
			}
			std::this_thread::sleep_for(std::chrono::milliseconds(1));
			continue;
		}

		//D7: the stream's own cut, on the same predicate the caller's gate uses -
		//while the emulator runs unpaused it does not start a queued blip. A blip
		//that already started is not cut, since stopping mid-waveform steps the
		//signal.
		if(_gameRunning()) {
			_clearedSeq.store(submitted, std::memory_order_release);
			continue;
		}

		if(!_unpaused) {
			_sink->Unpause();
			_unpaused = true;
		}

		//D8: unpaused for the blip, and paused again only once the queue is empty
		//and the queued frames have had time to drain. A blip submitted while this
		//runs restarts the wait with a full queue - it can only have been accepted
		//because the queue drained, so what restarts is the wait, not the sound.
		auto deadline = DrainDeadline(_slotFrames[(submitted - 1) & 1], _slotRate[(submitted - 1) & 1]);
		while(_running.load(std::memory_order_acquire)) {
			uint32_t latest = _submitSeq.load(std::memory_order_acquire);
			if(latest != submitted) {
				submitted = latest;
				if(_gameRunning()) {
					//The game started running mid-wait: the blip that arrived is
					//cleared rather than started (D7), and the wait ends so the
					//device parks instead of outputting silence.
					break;
				}
				deadline = DrainDeadline(_slotFrames[(submitted - 1) & 1], _slotRate[(submitted - 1) & 1]);
			}

			//A pull backend feeds its own device from here, on this thread; SDL's
			//push callback does nothing in this call.
			_sink->Service();

			if(std::chrono::steady_clock::now() >= deadline) {
				break;
			}
			std::this_thread::sleep_for(std::chrono::milliseconds(1));
		}

		//The queue is emptied whenever the stream pauses, stops or is closed, so a
		//later blip can never play a stale tail (D5). The device stays open for the
		//session and parks paused - never opened and closed per blip, and never left
		//running to output silence (D8).
		_clearedSeq.store(submitted, std::memory_order_release);
		_sink->Pause();
		_unpaused = false;
	}

	if(_unpaused) {
		_sink->Pause();
		_unpaused = false;
	}

	_available.store(false, std::memory_order_release);
	_sink->Release();
}
