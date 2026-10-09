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
//Host-free: the two enumerator calls are handed in, so
//scripts/core_unit_tests.cpp drives the choice without the Windows headers.
class MenuSoundDeviceResolve
{
public:
	//True when the call produced an endpoint.
	using GetDeviceByIdFn = std::function<bool(const std::string& deviceId)>;
	using GetDefaultDeviceFn = std::function<bool()>;

	static bool Resolve(const std::string& configuredDeviceId, const GetDeviceByIdFn& getById, const GetDefaultDeviceFn& getDefault)
	{
		if(!configuredDeviceId.empty() && getById(configuredDeviceId)) {
			return true;
		}
		return getDefault();
	}
};
