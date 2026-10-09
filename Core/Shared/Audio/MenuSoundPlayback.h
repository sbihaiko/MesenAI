#pragma once
#include "pch.h"
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

	//configured: whether the device already runs at outputRate/stereo (owned by
	//the caller, set here once the priming write has been issued).
	static void Play(IAudioDevice* device, bool& configured, const int16_t* samples, uint32_t frameCount, uint32_t sourceRate, uint32_t outputRate, uint32_t masterVolume)
	{
		vector<int16_t> rendered = Render(samples, frameCount, sourceRate, outputRate, masterVolume);
		if(rendered.empty()) {
			return;
		}

		if(!configured) {
			//Configure the device in the output format first; the reset this
			//triggers drops only this silent frame, not the blip.
			int16_t silence[2] = {};
			device->PlayBuffer(silence, 1, outputRate, true);
			configured = true;
		}
		device->PlayBuffer(rendered.data(), (uint32_t)(rendered.size() / 2), outputRate, true);
	}
};
