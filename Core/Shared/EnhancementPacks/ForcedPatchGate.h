#pragma once
#include <string>
#include <unordered_set>
#include <utility>

//#732 (ADR-0044's ApplyPatchOnHashMismatch override): what the override did on
//the current load, and the one way out of it. A pack's <patch>/patches[] made
//for another revision of the game can freeze this one, so:
//- the load that forced a patch records its file (Applied), which the UI reads
//  after GameLoaded to tell the player in plain words;
//- the player can ask to reload the running game without it (SuppressFor).
//  That is a per-ROM, per-session choice: the setting itself is never
//  changed, and other games keep the override.
//Host-free (no Emulator, no lock): MepPackManager owns one and guards it with
//its own _stateLock; scripts/core_unit_tests.cpp drives it directly.
class ForcedPatchGate
{
private:
	std::string _applied;
	std::unordered_set<std::string> _suppressed;

public:
	//True when the override may force a patch on this ROM now: the setting is
	//on and the player has not asked to play this ROM without it.
	bool Allows(bool overrideOn, const std::string& romSha1) const
	{
		return overrideOn && _suppressed.find(romSha1) == _suppressed.end();
	}

	//The forced patch actually applied on this load (file name or path).
	void NoteApplied(const std::string& patchFile)
	{
		_applied = patchFile;
	}

	//Empty when this load forced nothing.
	std::string Applied() const
	{
		return _applied;
	}

	//A new load starts with nothing forced; a suppression is kept.
	void ResetForLoad()
	{
		_applied.clear();
	}

	//#694: a load that failed puts the running game's state back.
	void RestoreApplied(std::string applied)
	{
		_applied = std::move(applied);
	}

	//The player asked to play this ROM without the forced patch. Only the ROM
	//the current load forced a patch on can be suppressed: false (and nothing
	//stored) when nothing was forced or the hash is unknown.
	bool SuppressFor(const std::string& romSha1)
	{
		if(_applied.empty() || romSha1.empty()) {
			return false;
		}
		_suppressed.insert(romSha1);
		return true;
	}
};
