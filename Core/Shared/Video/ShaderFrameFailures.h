#pragma once
#include <cstdint>
#include <string>

//Issue #593: when the shader filter chain fails on a frame (librashader's
//frame call returns an error) the presenter shows that frame unfiltered and
//goes on. This decides when such failures are reported and when the chain is
//given up on. Header-only and free of Metal/librashader types, so the policy
//is unit-testable on any host (scripts/core_unit_tests.cpp); MetalPresenter
//owns one.
//
//Rules:
//  1. An episode starts at a failure while none is open. It is reported once
//     (Take), with that first failure's error; later failures in it are not.
//  2. An episode ends after Limit frames in a row render, or when the chain
//     is reloaded or cleared (ChainChanged). A short recovery does not end
//     it, so a chain that flaps between failing and rendering puts one
//     message on screen, not one every other frame.
//  3. The Limit-th failure of an episode (in a row or not) asks for the chain
//     to be dropped, as a GPU fault drops it (#586): a chain that keeps
//     failing only costs a call per frame and a picture that flickers between
//     filtered and unfiltered. Not on the first failure, unlike a GPU fault:
//     a frame error is caught on the CPU before anything reaches the GPU, it
//     cannot poison the queue, and the failures librashader can return here
//     (Metal refusing a texture or an encoder) may be transient.
struct ShaderFrameFailures
{
	//One second at 60 Hz.
	static constexpr uint32_t Limit = 60;

	bool InEpisode = false;
	uint32_t Failures = 0;
	uint32_t Successes = 0;
	bool Pending = false;
	std::string Error;

	//Returns true when the chain should be dropped (rule 3).
	bool Failed(const std::string& error)
	{
		if(!InEpisode) {
			InEpisode = true;
			Failures = 0;
			Pending = true;
			Error = error;
		}
		Successes = 0;
		return ++Failures >= Limit;
	}

	void Succeeded()
	{
		if(InEpisode && ++Successes >= Limit) {
			InEpisode = false;
		}
	}

	//A reloaded or cleared chain starts clean. A report still pending is kept:
	//the failure happened and the caller has not seen it yet.
	void ChainChanged()
	{
		InEpisode = false;
		Failures = 0;
		Successes = 0;
	}

	//True once per episode, with the error that opened it.
	bool Take(std::string& error)
	{
		if(!Pending) {
			return false;
		}
		Pending = false;
		error = Error;
		return true;
	}
};
