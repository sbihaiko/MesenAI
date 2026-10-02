#pragma once
#include "Shared/MessageManager.h"
#include "Utilities/FolderUtilities.h"
#include <string>

//ADR-0237 / PRD slice P.8: what a renderer does when the configured shader
//preset changes. Header-only and free of Metal/librashader types, so the
//policy is unit-testable on any host (scripts/core_unit_tests.cpp drives it
//with a fake presenter). MacOSMetalRenderer::UpdateShader is the caller.
//
//TPresenter needs: bool SetShader(path, params), void ClearShader(),
//void UpdateShaderParams(params) and const std::string& LastError().
//
//Rules:
//  1. Same preset file as before: only the parameters move. A preset that
//     failed to load is not retried here, so a parameter change never
//     reports the same failure twice.
//  2. Empty preset file: the shader is cleared.
//  3. Any other file: the preset is loaded. On failure the renderer keeps
//     presenting unfiltered; the full reason goes to the log and an OSD
//     message names the preset file and a short reason (#585).

//#585: the short reason the on-screen message carries. The presenter's error
//reads "<librashader call> failed: <librashader's text>"; the call name means
//nothing to a player, and librashader's text can run to several lines (a
//shader compiler log). Keeps the first line of the text, capped in length -
//the whole error still goes to the log.
inline std::string ShaderFailureReason(const std::string& error)
{
	static constexpr size_t MaxReasonLength = 120;
	static const std::string apiSuffix = " failed: ";

	std::string reason = error;
	size_t api = reason.find(apiSuffix);
	if(api != std::string::npos && reason.find(' ') == api) {
		reason = reason.substr(api + apiSuffix.size());
	}
	size_t lineEnd = reason.find_first_of("\r\n");
	if(lineEnd != std::string::npos) {
		reason = reason.substr(0, lineEnd);
	}
	size_t first = reason.find_first_not_of(" \t");
	if(first == std::string::npos) {
		return "";
	}
	reason = reason.substr(first, reason.find_last_not_of(" \t") - first + 1);
	if(reason.size() > MaxReasonLength) {
		//Never cut inside a UTF-8 sequence (a preset path can be non-ASCII).
		size_t cut = MaxReasonLength - 3;
		while(cut > 0 && ((unsigned char)reason[cut] & 0xC0) == 0x80) {
			cut--;
		}
		reason = reason.substr(0, cut) + "...";
	}
	return reason;
}

template<typename TPresenter, typename TParams>
void ApplyShaderPreset(TPresenter& presenter, const std::string& previousFile, const std::string& newFile, const TParams& params)
{
	if(newFile == previousFile) {
		presenter.UpdateShaderParams(params);
		return;
	}
	if(newFile.empty()) {
		presenter.ClearShader();
		return;
	}
	if(!presenter.SetShader(newFile, params)) {
		MessageManager::Log("[librashader] " + presenter.LastError());
		//#585: otherwise the only sign is a picture that silently stays
		//unfiltered. One message per load attempt (rule 1 never retries).
		MessageManager::DisplayMessage("Shaders", "ShaderLoadFailed", FolderUtilities::GetFilename(newFile, true), ShaderFailureReason(presenter.LastError()));
	}
}

//What a renderer does after every Present(). TPresenter also needs
//bool TakeFrameError(std::string&) and bool TakeShaderDropped().
//  - #593: a frame the chain failed on was presented unfiltered. Reported
//    once per failure episode (Core/Shared/Video/ShaderFrameFailures.h): the
//    full error to the log, the preset file and a short reason on screen.
//  - #584: a dropped chain (a GPU fault, or #593's failure limit) goes to the
//    log only. The renderer does not reload it until the configured preset
//    changes, so it is not dropped again on the next frame.
template<typename TPresenter>
void ReportShaderFrameProblems(TPresenter& presenter, const std::string& shaderFile)
{
	std::string error;
	if(presenter.TakeFrameError(error)) {
		MessageManager::Log("[librashader] " + error);
		MessageManager::DisplayMessage("Shaders", "ShaderFrameFailed", FolderUtilities::GetFilename(shaderFile, true), ShaderFailureReason(error));
	}
	if(presenter.TakeShaderDropped()) {
		MessageManager::Log("[librashader] " + presenter.LastError());
	}
}
