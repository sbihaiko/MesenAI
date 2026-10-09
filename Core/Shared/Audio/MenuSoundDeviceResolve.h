#pragma once
#include <functional>
#include <string>

//ADR-0270 D2/D9 (issue #1126): which endpoint the menu device opens.
//WasapiSoundManager::Initialize resolves the game device as "the configured
//device when the player picked one - looked up by its own id - and the default
//endpoint when they did not, or when the configured one is no longer there". D2
//sends the blip out through "the output device the player already picked", so the
//menu device resolves it the same way rather than always taking the default
//endpoint, which would put the game on the player's device and the blips on
//another one.
//
//#1153: and it takes the same two steps the game device takes to get there.
//AudioConfig::AudioDevice holds a *description* - "Default", or the friendly name
//WasapiSoundManager::GetAvailableDevices enumerates - and never an endpoint id,
//which is what IMMDeviceEnumerator::GetDevice wants; passing the description
//straight to GetDevice fails for every device including "Default", so the sink
//silently fell back to the default endpoint and a player who had picked another
//device heard the game there and the blips elsewhere. ToEndpointId is the
//conversion WasapiSoundManager::SetAudioDevice performs, and Resolve is the
//endpoint choice that follows it.
//
//Host-free: the enumerator calls are handed in, so
//scripts/core_unit_tests.cpp drives both steps without the Windows headers.
class MenuSoundDeviceResolve
{
public:
	//The string WasapiSoundManager::GetAvailableDevices() lists for the default
	//endpoint, and one of the two ways a config can spell "the default one".
	static constexpr const char* DefaultDeviceName = "Default";

	//True when the config named the default endpoint rather than a device: an
	//empty name is how every config that never picked one holds it, and "Default"
	//is what the Windows enumeration writes back when the player takes the first
	//entry (AudioConfigViewModel::UpdateAudioDevices).
	static bool IsDefaultDeviceName(const std::string& deviceName)
	{
		return deviceName.empty() || deviceName == DefaultDeviceName;
	}

	//True when the call produced an endpoint.
	using GetDeviceByIdFn = std::function<bool(const std::string& deviceId)>;
	using GetDefaultDeviceFn = std::function<bool()>;

	//The endpoint id for a configured description, and an empty id for the
	//default endpoint - the id WasapiSoundManager::SetAudioDevice ends up with
	//before it calls GetDevice. A description that is not in the enumeration has
	//no id either, which is the game device's "the configured one is gone" case.
	using GetIdForNameFn = std::function<std::string(const std::string& deviceName)>;

	static std::string ToEndpointId(const std::string& configuredDeviceName, const GetIdForNameFn& idForName)
	{
		if(IsDefaultDeviceName(configuredDeviceName)) {
			return std::string();
		}
		return idForName(configuredDeviceName);
	}

	static bool Resolve(const std::string& configuredDeviceId, const GetDeviceByIdFn& getById, const GetDefaultDeviceFn& getDefault)
	{
		if(!configuredDeviceId.empty() && getById(configuredDeviceId)) {
			return true;
		}
		return getDefault();
	}
};
