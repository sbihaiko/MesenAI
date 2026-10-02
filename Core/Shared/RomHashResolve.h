#pragma once
#include "pch.h"

//Resolves a ROM hash from the loaded console, with a fallback for consoles that do not
//report one. Header-only and templated so it can be unit tested without the Emulator (#599).
//No console loaded -> empty string; the fallback is not evaluated.
template<typename TConsole, typename THashType, typename TFallback>
string ResolveRomHash(const shared_ptr<TConsole>& console, THashType type, TFallback fallback)
{
	if(!console) {
		return "";
	}

	string hash = console->GetHash(type);
	if(hash.size()) {
		return hash;
	}
	return fallback(type);
}
