#pragma once
#include "pch.h"
#include "Core/Shared/Audio/MenuSoundArming.h"
#include "Core/Shared/Audio/MenuSoundDeviceResolve.h"
#include "Core/Shared/Audio/MenuSoundSinkHealth.h"
#include "Core/Shared/Audio/MenuSoundStream.h"
#include "Core/Shared/MessageManager.h"
#include "Utilities/UTF8Util.h"
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <Functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

//ADR-0270 D9 (issue #1126): the menu-sound sink on the Windows backend that
//ships - a WASAPI shared-mode render client of its own, never the game device's.
//D2 holds the same way it does on SDL: a second device, not a second writer on
//the emulator's ring.
//
//D9's rule binds every backend: the open runs off the UI thread and is never
//awaited by it, and this backend's mechanism is its own - a worker thread, since
//WasapiSoundManager opens inline on whoever asks for it and a menu device must
//never do that to a UI press. DirectSound has no menu sink, so the sink answers
//unavailable rather than sending the blip through an audio API the player did
//not pick.
class WasapiMenuSoundSink final : public IMenuSoundSink
{
public:
	//The one code the liveness latch treats as fatal is WASAPI's own.
	static_assert(MenuSoundSinkHealth::DeviceInvalidated == (int32_t)AUDCLNT_E_DEVICE_INVALIDATED,
		"MenuSoundSinkHealth::DeviceInvalidated mirrors AUDCLNT_E_DEVICE_INVALIDATED");

	//The device and the backend travel in with the sink, read once on the thread
	//that applied the settings. The open worker below reads them from here and
	//never from AudioConfig, which the settings apply path rewrites under it
	//(MenuSoundArming.h).
	explicit WasapiMenuSoundSink(const MenuSoundArming& arming)
		: _arming(arming)
	{
	}

	~WasapiMenuSoundSink() override
	{
		Release();
	}

	void SetReader(IMenuSoundReader* reader) override
	{
		_reader = reader;
	}

	void BeginOpen() override
	{
		//Both the open worker and this thread (the stream's owner thread) call into
		//COM, so each initializes its own apartment; the worker balances its own,
		//and Release balances this one.
		HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
		_comInitialized = SUCCEEDED(hr);

		_openWorker = std::thread([this]() {
			HRESULT workerHr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
			bool workerCom = SUCCEEDED(workerHr);

			_opened = OpenDevice();

			if(workerCom) {
				CoUninitialize();
			}
			_openDone.store(true, std::memory_order_release);
		});
	}

	bool TryTakeOpen(bool& opened) override
	{
		if(!_openDone.load(std::memory_order_acquire)) {
			return false;
		}

		if(_openWorker.joinable()) {
			_openWorker.join();
		}
		opened = _opened;
		return true;
	}

	void Unpause() override
	{
		if(!_audioClient || _playing) {
			return;
		}

		//D9: a device that went away answers this call with
		//AUDCLNT_E_DEVICE_INVALIDATED, which latches the device dead: the owner
		//thread's next IsAlive() then flips the capability back to false.
		_health.ObservedResult((int32_t)_audioClient->Start());
		if(_health.IsAlive()) {
			_playing = true;
		}
	}

	void Pause() override
	{
		if(_audioClient && _playing) {
			//D5: stopping and resetting empties the device's buffer with the queue,
			//so a later blip can never play a stale tail.
			_audioClient->Stop();
			_audioClient->Reset();
			_playing = false;
		}
	}

	bool IsAlive() const override
	{
		//The open device, until one of its calls answers with the invalidated code
		//- which is the only way WASAPI reports that the endpoint left (D9).
		return _audioClient.Get() != nullptr && _health.IsAlive();
	}

	uint32_t BufferFrames() const override
	{
		return _bufferFrameCount != 0 ? _bufferFrameCount : MenuSoundStream::DeviceSampleRate / 20;
	}

	void Service() override
	{
		if(!_playing || !_audioClient || !_renderClient || _reader == nullptr) {
			return;
		}

		//D9: either of these calls answers with AUDCLNT_E_DEVICE_INVALIDATED once
		//the endpoint is gone, and that is where a device that disappeared
		//mid-session is noticed - nothing else reports it.
		UINT32 padding = 0;
		HRESULT hr = _audioClient->GetCurrentPadding(&padding);
		_health.ObservedResult((int32_t)hr);
		if(FAILED(hr) || padding >= _bufferFrameCount) {
			return;
		}

		UINT32 frames = _bufferFrameCount - padding;
		uint8_t* data = nullptr;
		hr = _renderClient->GetBuffer(frames, &data);
		_health.ObservedResult((int32_t)hr);
		if(FAILED(hr)) {
			return;
		}

		//D3: the reader fills what it has and silence after that; the device is
		//never handed a block of the game's audio to loop.
		_reader->ReadMenuSound((int16_t*)data, frames);
		_renderClient->ReleaseBuffer(frames, 0);
	}

	void Release() override
	{
		//D4: the thread that joins comes before the device is freed - the open
		//worker first, then the device. Service() runs on the owner thread, so
		//there is no reader thread to join here.
		//
		//#1153: this join therefore happens on the stream's owner thread and never
		//on its caller. A re-arm takes a stream whose worker is still inside
		//Activate/Initialize and detaches that owner thread instead of joining it
		//(MenuSoundStream::Stop); the worker finishes here, on the thread whose
		//exit path this is, and only then are _audioClient and _renderClient reset
		//- resetting them while the worker still held them is the use-after-free
		//D4's ordering exists to prevent.
		if(_openWorker.joinable()) {
			_openWorker.join();
		}

		Pause();
		_renderClient.Reset();
		_audioClient.Reset();
		_bufferFrameCount = 0;
		_opened = false;
		_openDone.store(false, std::memory_order_release);

		if(_comInitialized) {
			//Balanced on the thread that called CoInitializeEx: the owner thread,
			//whose exit path is the caller of Release.
			CoUninitialize();
			_comInitialized = false;
		}
	}

private:
	bool OpenDevice()
	{
		//D9: the sink follows the backend the game device would use, and the factory
		//answers "no menu sink" for the backend that has none rather than building
		//this one. Should it ever be built for one anyway, the open fails here
		//instead of sending the blip through an audio API the player did not pick.
		if(_arming.Backend == DirectSoundBackend) {
			return false;
		}

		ComPtr<IMMDeviceEnumerator> enumerator;
		HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
		if(FAILED(hr)) {
			return false;
		}

		//#1153: AudioConfig::AudioDevice holds the endpoint's description, never
		//its id, so the name is converted the way WasapiSoundManager::SetAudioDevice
		//converts it before it opens the game's device. Handing the description
		//straight to GetDevice asked for an endpoint by an id that is really a
		//friendly name, which fails for every device - "Default" included - and
		//left the blips on the default endpoint while the game played elsewhere.
		std::string endpointId = MenuSoundDeviceResolve::ToEndpointId(
			_arming.Device,
			[&enumerator](const std::string& deviceName) { return FindEndpointId(enumerator.Get(), deviceName); });

		//D2: the output device the player already picked, resolved exactly the way
		//the game device resolves it (WasapiSoundManager::Initialize) - the
		//configured endpoint by its id, and the default endpoint when they picked
		//none, or when the one they picked is no longer there. Always taking the
		//default endpoint would put the game on the player's device and the blips
		//on another one.
		ComPtr<IMMDevice> device;
		bool resolved = MenuSoundDeviceResolve::Resolve(
			endpointId,
			[&enumerator, &device](const std::string& deviceId) {
				std::wstring wideDeviceId = utf8::utf8::decode(deviceId);
				return SUCCEEDED(enumerator->GetDevice(wideDeviceId.c_str(), &device)) && device.Get() != nullptr;
			},
			[&enumerator, &device]() {
				return SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &device)) && device.Get() != nullptr;
			});

		if(!resolved) {
			return false;
		}

		if(FAILED(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&_audioClient))) {
			_audioClient.Reset();
			return false;
		}

		WAVEFORMATEX format = {};
		format.wFormatTag = WAVE_FORMAT_PCM;
		format.nChannels = 2;
		format.nSamplesPerSec = MenuSoundStream::DeviceSampleRate;
		format.wBitsPerSample = 16;
		format.nBlockAlign = (format.wBitsPerSample / 8) * format.nChannels;
		format.nAvgBytesPerSec = format.nSamplesPerSec * format.nBlockAlign;

		//D8: a UI tick wants an immediate answer, so this device gets a small fixed
		//buffer rather than the game's AudioLatency.
		REFERENCE_TIME duration = (REFERENCE_TIME)BufferMs * 10000;
		hr = _audioClient->Initialize(
			AUDCLNT_SHAREMODE_SHARED,
			AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
			duration,
			0,
			&format,
			nullptr);
		if(FAILED(hr)) {
			_audioClient.Reset();
			return false;
		}

		if(FAILED(_audioClient->GetBufferSize(&_bufferFrameCount))) {
			_audioClient.Reset();
			return false;
		}

		if(FAILED(_audioClient->GetService(__uuidof(IAudioRenderClient), (void**)&_renderClient))) {
			_renderClient.Reset();
			_audioClient.Reset();
			return false;
		}

		return true;
	}

	//#1153: the endpoint id behind a description, enumerated the way
	//WasapiSoundManager::GetAvailableDeviceInfo enumerates it - the same
	//PKEY_Device_FriendlyName the game device matches on, so both halves of the
	//app agree on which device a name names. An empty answer means the name is
	//not in the enumeration, which the game device treats as "the configured one
	//is gone" and resolves to the default endpoint.
	static std::string FindEndpointId(IMMDeviceEnumerator* enumerator, const std::string& deviceName)
	{
		if(enumerator == nullptr) {
			return std::string();
		}

		ComPtr<IMMDeviceCollection> collection;
		if(FAILED(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &collection))) {
			return std::string();
		}

		UINT count = 0;
		collection->GetCount(&count);

		for(UINT i = 0; i < count; i++) {
			ComPtr<IMMDevice> device;
			if(FAILED(collection->Item(i, &device))) {
				continue;
			}

			ComPtr<IPropertyStore> props;
			if(FAILED(device->OpenPropertyStore(STGM_READ, &props))) {
				continue;
			}

			PROPVARIANT varName;
			PropVariantInit(&varName);
			if(FAILED(props->GetValue(PKEY_Device_FriendlyName, &varName))) {
				PropVariantClear(&varName);
				continue;
			}

			bool matches = varName.vt == VT_LPWSTR && utf8::utf8::encode(varName.pwszVal) == deviceName;
			PropVariantClear(&varName);
			if(!matches) {
				continue;
			}

			LPWSTR deviceId = nullptr;
			if(FAILED(device->GetId(&deviceId)) || deviceId == nullptr) {
				return std::string();
			}
			std::string id = utf8::utf8::encode(deviceId);
			CoTaskMemFree(deviceId);
			return id;
		}

		return std::string();
	}

	//The device is opened in shared mode with the audio engine's own period as its
	//floor, so this is a request, not a guarantee.
	static constexpr int BufferMs = 50;

	//Core/Shared/SettingTypes.h's AudioBackendType::DirectSound, as the int the
	//host-free MenuSoundArming carries; EmuApiWrapper.cpp static_asserts the pair.
	static constexpr int DirectSoundBackend = 2;

	//The device the player picked and the backend they picked, carried in from the
	//arming rather than read off the live settings.
	MenuSoundArming _arming;
	IMenuSoundReader* _reader = nullptr;

	std::thread _openWorker;
	std::atomic<bool> _openDone{false};
	bool _opened = false;
	bool _comInitialized = false;
	bool _playing = false;

	ComPtr<IAudioClient> _audioClient;
	ComPtr<IAudioRenderClient> _renderClient;
	UINT32 _bufferFrameCount = 0;
	//D9: one-way, fed by the device's own call results.
	MenuSoundSinkHealth _health;
};
