#include "Common.h"
#include "Core/Shared/EnhancementPacks/MepContentId.h"
#include "Core/Shared/EnhancementPacks/MepPackManager.h"
#include "Core/Shared/EnhancementPacks/MepRecipeInstaller.h"
#include "Core/NES/NesConsole.h"
#include "Core/Shared/Emulator.h"
#include "Utilities/StringUtilities.h"

extern unique_ptr<Emulator> _emu;

//F6.4b - client-side MEP-recipe-v1 auto-install (ADR-0138 clarifications
//4/37/38). Sibling file to EmuApiWrapper.cpp (already at its 200-line
//per-file guardrail - see project AGENTS.md) rather than a new addition
//there; this is the only export in the file, so it stays well under the
//guardrail on its own.
//
//This export is a thin marshaling wrapper around the already-shipped F6.4a
//offline installer (MepRecipeInstaller::Install, Core/Shared/EnhancementPacks/
//MepRecipeInstaller.h) - no install/patch/rewrite logic is duplicated here.
//The catalog fetch, host-allowlist download and dep resolution that produce
//the paths passed in below are the UI-side network-facing slice (F6.4b's
//other tasks); this DLL boundary never touches the network itself.

//depPathsBlob: dep-id -> local-path map, encoded as newline-separated
//"depId\tlocalPath" rows (the same tab-separated-row-per-line convention
//MepPackManager::GetPackListText/GetMepPackList already use elsewhere in
//this DLL) rather than a char** array, for which this DLL has no existing
//marshaling precedent. A null/empty blob means no deps supplied.
static unordered_map<string, string> ParseMepRecipeDepPaths(const char* depPathsBlob)
{
	unordered_map<string, string> depPaths;
	if(!depPathsBlob || depPathsBlob[0] == 0) {
		return depPaths;
	}

	for(string& row : StringUtilities::Split(depPathsBlob, '\n')) {
		size_t tabPos = row.find('\t');
		if(tabPos == string::npos || tabPos == 0) {
			continue;
		}
		depPaths[row.substr(0, tabPos)] = row.substr(tabPos + 1);
	}
	return depPaths;
}

extern "C"
{
	//outResult: encoded as newline-separated rows - row 0 is "1"/"0" for
	//success/failure (redundant with the bool return value, kept for
	//callers that only read the buffer), row 1 is the error message (empty
	//on success), and any remaining rows are MepRecipeInstallResult::
	//Withheld entries (only present on a successful install where
	//policy.apply_patch_only_if_complete withheld part of the recipe,
	//MEP-recipe-v1 section 6).
	DllExport bool __stdcall InstallMepRecipe(const char* recipeJson, const char* primaryPath, const char* depPathsBlob, const char* romName, const char* outFolder, char* outResult, uint32_t maxResultLength)
	{
		unordered_map<string, string> depPaths = ParseMepRecipeDepPaths(depPathsBlob);

		MepRecipeInstallResult result;
		bool success = MepRecipeInstaller::Install(
			recipeJson ? recipeJson : "",
			primaryPath ? primaryPath : "",
			depPaths,
			romName ? romName : "",
			outFolder ? outFolder : "",
			result
		);

		string resultText = (success ? "1" : "0") + string("\n") + result.Error;
		for(string& withheld : result.Withheld) {
			resultText += "\n" + withheld;
		}
		StringUtilities::CopyToBuffer(resultText, outResult, maxResultLength);

		return success;
	}

	//ADR-0147: hex content_id of a real directory tree (MepContentId::
	//ComputeFolder), used by the UI to detect local edits to an installed mep/
	//pack by comparing against the baseline recorded at install time. Throws
	//no exception; an unreadable folder (or one that computes to "") returns
	//an empty buffer.
	DllExport void __stdcall GetMepContentId(const char* folder, char* outBuffer, uint32_t maxLength)
	{
		string result = MepContentId::ComputeFolder(folder ? folder : "");
		StringUtilities::CopyToBuffer(result, outBuffer, maxLength);
	}

	//P.1-local (ADR-0206 §3): the local-container content_id cache refresh.
	//Blocking and I/O-heavy by design (it walks and hashes the local packs
	//whose fingerprint moved) - the client calls it on a background thread
	//after a game loads, never from the ROM-load path, and picks the result up
	//on the next load. Static: it reads the packs folder, so it shares no state
	//with the running emulator. Returns how many containers were re-hashed
	//(0 = the cache was already current, so the caller has nothing to
	//re-evaluate).
	DllExport int32_t __stdcall RefreshMepLocalIdentities()
	{
		return MepPackManager::RefreshLocalIdentityCache().Recomputed;
	}

	//F12.3 (ADR-0212): ask the loaded NES pack to re-decode the images an
	//artist repainted on disk. Returns immediately - the request is served on
	//the emulation thread at the next frame boundary (ADR-0212 section 3), so
	//this is safe to call from the UI thread. Returns false when no NES console
	//is loaded; there is nothing to reload then. The outcome (how many images,
	//how long, anything refused) goes to the [MEP] log, not through the return
	//value, because the answer is not known yet when this returns.
	DllExport bool __stdcall RequestMepImageReload()
	{
		auto console = _emu->GetConsole();
		if(NesConsole* nes = dynamic_cast<NesConsole*>(console.get())) {
			nes->RequestHdPackImageReload();
			return true;
		}
		return false;
	}
}
