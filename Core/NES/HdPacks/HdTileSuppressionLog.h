#pragma once
//Issue #328. HdNesPack::GetPixels draws the two `<background>` layers whose
//priority is 20 or above (HdNesPack.h's BehindFgSpritesPriority and
//ForegroundPriority) AFTER the `<tile>` rule, so on a screen a recorded
//`<background>` matches, the screen PNG is the pixels that reach the display
//and every `<tile>` rule under it is invisible. Both halves of that are
//intended - ADR-0050 writes one priority-20 background per captured screen
//precisely so the screen replaces the tiles, and ADR-0156 states the layer
//order as the load-bearing fact behind screen residency - but until issue #328
//nothing anywhere said so, and a pack could build green, lint clean and carry
//a painted key at the right crop in `hires.txt` while rendering none of it.
//
//This is the pure, host-free half of the diagnostic that says so: a per-load
//ledger that keeps the first MaxDistinctMessages signatures it is shown and
//counts the ones past the budget, so the renderer writes one line per distinct
//`<background>` instead of one line per screen pixel it covers. The renderer
//holds one of these for the life of the pack (HdNesPack is rebuilt on every
//pack load), which is the same per-load scope HdPackErrorDedupe has.
//
//Header-only on purpose, for the same reason HdPackErrorDedupe.h is:
//HdNesPack.cpp is not on the Makefile's CUTSRC list, so logic that has to be
//unit-tested cannot live in it.
#include <cstddef>
#include <string>
#include <unordered_set>

class HdTileSuppressionLog
{
public:
	//One signature per `<background>` entry, so this caps the number of
	//*backgrounds named*, not the number of tiles or pixels they cover - the
	//15 recorded screens of a typical pack fit under it with room to spare. A
	//pack that needs more lines than this has made its point after the first
	//few; the ones past the budget are counted and reported in one summary
	//line, never silently dropped.
	static constexpr size_t MaxDistinctMessages = 16;

	//True when the caller should write the line for this signature now: only on
	//the first sighting, and only while the budget lasts.
	bool ShouldLog(const std::string& signature)
	{
		if(_seen.find(signature) != _seen.end()) {
			return false;
		}
		if(_seen.size() >= MaxDistinctMessages) {
			_unretained++;
			return false;
		}
		_seen.insert(signature);
		return true;
	}

	size_t GetDistinctCount() const { return _seen.size(); }

	//Signatures that arrived after the distinct-signature budget was spent.
	//These are the only lines this ledger loses track of, and the count is what
	//the summary line reports.
	uint32_t GetUnretainedCount() const { return _unretained; }

	bool HasAny() const { return !_seen.empty(); }

private:
	std::unordered_set<std::string> _seen;
	uint32_t _unretained = 0;
};
