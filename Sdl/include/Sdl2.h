#pragma once

#include <stdint.h>
#include <dlfcn.h>

typedef uint16_t SDL_AudioFormat;
typedef uint32_t SDL_AudioDeviceID;

#define SDL_INIT_AUDIO 0x00000010u
#define AUDIO_S16SYS   0x8010 //Assume little endian CPU

//SDL2's SDL_AudioStatus values, which SDL_GetAudioDeviceStatus reports.
#define SDL_AUDIO_STOPPED 0x1010
#define SDL_AUDIO_PLAYING 0x1011
#define SDL_AUDIO_PAUSED  0x1012

typedef void (*SDL_AudioCallback)(void* userdata, uint8_t* stream, int len);

typedef struct SDL_AudioSpec
{
	int freq;
	SDL_AudioFormat format;
	uint8_t channels;
	uint8_t silence;
	uint16_t samples;
	uint16_t padding;
	uint32_t size;
	SDL_AudioCallback callback;
	void* userdata;
} SDL_AudioSpec;

typedef int (*PFN_SDL_InitSubSystem)(uint32_t flags);
typedef void (*PFN_SDL_CloseAudioDevice)(SDL_AudioDeviceID dev);
typedef SDL_AudioDeviceID (*PFN_SDL_OpenAudioDevice)(const char* device, int iscapture, const SDL_AudioSpec* desired, SDL_AudioSpec* obtained, int allowed_changes);
typedef int (*PFN_SDL_GetNumAudioDevices)(int iscapture);
typedef const char* (*PFN_SDL_GetAudioDeviceName)(int index, int iscapture);
typedef void (*PFN_SDL_PauseAudioDevice)(SDL_AudioDeviceID dev, int pause_on);
typedef int (*PFN_SDL_GetAudioDeviceStatus)(SDL_AudioDeviceID dev);

inline PFN_SDL_InitSubSystem ptr_SDL_InitSubSystem = nullptr;
inline PFN_SDL_CloseAudioDevice ptr_SDL_CloseAudioDevice = nullptr;
inline PFN_SDL_OpenAudioDevice ptr_SDL_OpenAudioDevice = nullptr;
inline PFN_SDL_GetNumAudioDevices ptr_SDL_GetNumAudioDevices = nullptr;
inline PFN_SDL_GetAudioDeviceName ptr_SDL_GetAudioDeviceName = nullptr;
inline PFN_SDL_PauseAudioDevice ptr_SDL_PauseAudioDevice = nullptr;
inline PFN_SDL_GetAudioDeviceStatus ptr_SDL_GetAudioDeviceStatus = nullptr;

#define SDL_InitSubSystem ptr_SDL_InitSubSystem
#define SDL_CloseAudioDevice ptr_SDL_CloseAudioDevice
#define SDL_OpenAudioDevice ptr_SDL_OpenAudioDevice
#define SDL_GetNumAudioDevices ptr_SDL_GetNumAudioDevices
#define SDL_GetAudioDeviceName ptr_SDL_GetAudioDeviceName
#define SDL_PauseAudioDevice ptr_SDL_PauseAudioDevice
#define SDL_GetAudioDeviceStatus ptr_SDL_GetAudioDeviceStatus

inline bool LoadSdl(void)
{
	static bool loaded = false;
	if(loaded) {
		return true;
	}

	void* handle = nullptr;

#if defined(__APPLE__) || defined(__MACH__)
	static const char* macPaths[] = {
		"libSDL2-2.0.0.dylib",
		"libSDL2.dylib",
		"SDL2.framework/SDL2",
		"@rpath/SDL2.framework/SDL2",
		"/opt/local/lib/libSDL2.dylib",
		"/opt/local/lib/libSDL2-2.0.dylib",
		"/opt/homebrew/lib/libSDL2.dylib",
		"/usr/local/lib/libSDL2.dylib"
	};

	for(const char* path : macPaths) {
		handle = dlopen(path, RTLD_LAZY | RTLD_LOCAL);
		if(handle) {
			break;
		}
	}
#else
	static const char* linuxPaths[] = {
		"libSDL2-2.0.so.0",
		"libSDL2-2.0.so",
		"libSDL2.so"
	};

	for(const char* path : linuxPaths) {
		handle = dlopen(path, RTLD_LAZY | RTLD_LOCAL);
		if(handle) {
			break;
		}
	}
#endif

	if(!handle) {
		return false;
	}

#define LOAD_SYMBOL(type, name) \
    ptr_##name = (type)dlsym(handle, #name); \
    if (!ptr_##name) { return false; }

	LOAD_SYMBOL(PFN_SDL_InitSubSystem, SDL_InitSubSystem);
	LOAD_SYMBOL(PFN_SDL_CloseAudioDevice, SDL_CloseAudioDevice);
	LOAD_SYMBOL(PFN_SDL_OpenAudioDevice, SDL_OpenAudioDevice);
	LOAD_SYMBOL(PFN_SDL_GetNumAudioDevices, SDL_GetNumAudioDevices);
	LOAD_SYMBOL(PFN_SDL_GetAudioDeviceName, SDL_GetAudioDeviceName);
	LOAD_SYMBOL(PFN_SDL_PauseAudioDevice, SDL_PauseAudioDevice);

#undef LOAD_SYMBOL

	//Optional: the menu sink reads it to tell a device that is gone from one that
	//is merely paused, and a build that does not export it must not cost the game
	//device its audio - which is what a mandatory LOAD_SYMBOL above would do,
	//since every caller of LoadSdl shares this list.
	ptr_SDL_GetAudioDeviceStatus = (PFN_SDL_GetAudioDeviceStatus)dlsym(handle, "SDL_GetAudioDeviceStatus");

	loaded = true;
	return true;
}