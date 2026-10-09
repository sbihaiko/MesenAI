#pragma once

#include "pch.h"

struct AudioStatistics
{
	double AverageLatency = 0;
	uint32_t BufferUnderrunEventCount = 0;
	uint32_t BufferSize = 0;
};

class IAudioDevice
{
public:
	virtual ~IAudioDevice() {}
	virtual void PlayBuffer(int16_t* soundBuffer, uint32_t bufferSize, uint32_t sampleRate, bool isStereo) = 0;
	virtual void Stop() = 0;
	virtual void Pause() = 0;
	virtual void ProcessEndOfFrame() = 0;
	//Blocks until a reconfigure started by PlayBuffer (a device that opens in the
	//background) has finished, so the next PlayBuffer is not dropped.
	virtual void WaitUntilReady() {}

	virtual string GetAvailableDevices() = 0;
	virtual void SetAudioDevice(string deviceName) = 0;

	virtual AudioStatistics GetStatistics() = 0;
};