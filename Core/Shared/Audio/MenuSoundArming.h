#pragma once
#include <string>

//ADR-0270 D2/D9 (issue #1126): what the menu stream is armed on - which output
//device the player picked, and which backend. The backend is the C#-facing enum
//as an int (Core/Shared/SettingTypes.h's AudioBackendType: Default, Wasapi,
//DirectSound, Sdl2), kept an int here because every user of this header is
//host-free; EmuApiWrapper.cpp static_asserts that the two still agree.
//
//It exists as its own unit because the value crosses a thread boundary: the
//settings are read once, on the thread that applies them, and the sink carries
//the result with it.
//
//A sink must never read AudioConfig on its own threads instead.
//EmuSettings::SetAudioConfig assigns the whole config first - which briefly
//points AudioConfig::AudioDevice at the marshaled C# buffer - and only then
//repoints the field at its own string, reassigned on every apply
//(Core/Shared/EmuSettings.cpp:234-241). A sink thread that read that field
//inside an apply could dereference the buffer being replaced, which is the
//use-after-free AC2 forbids, or open the device the player just left.
class MenuSoundArming
{
public:
	//Core/Shared/SettingTypes.h's AudioBackendType::Default: "the backend this
	//platform ships" - Wasapi on Windows, SDL on macOS and Linux. The config
	//holds it until the player picks one, so it is the value an untouched install
	//carries rather than an absence.
	static constexpr int DefaultBackend = 0;

	//The default endpoint has two spellings and they are the same endpoint. SDL
	//opens it from a null name (SdlSoundManager), and on Windows the config holds
	//what the player picked off the enumeration - where the default endpoint is
	//the literal "Default" (WasapiSoundManager::GetAvailableDevices) - while a
	//config that never picked one holds "". WasapiSoundManager::SetAudioDevice
	//maps both to the empty endpoint id before resolving the default one.
	static bool IsDefaultDevice(const std::string& device) { return device.empty() || device == "Default"; }

	std::string Device;
	int Backend = 0;

	//D2/D9 (#1153): the same choice spelled two ways is the same arming. Applying
	//"" and then "Default" names one endpoint, so re-arming over it would tear a
	//working device down and open it again - and the second open runs while the
	//first press after launch wants its blip (AC1).
	bool operator==(const MenuSoundArming& other) const
	{
		return Backend == other.Backend
			&& (Device == other.Device || (IsDefaultDevice(Device) && IsDefaultDevice(other.Device)));
	}
	bool operator!=(const MenuSoundArming& other) const { return !(*this == other); }
};
