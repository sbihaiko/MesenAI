#pragma once
//Issue #302. HdPackLoader used to log one line per *occurrence* of a
//malformed reference, so a condition name used by 2 400 <tile> rules was
//reported 2 400 times. MessageManager's in-memory log is a 1 000-entry FIFO
//with front eviction, so one such pack (Metroid, 8 234 lines) evicted every
//other message of the session.
//
//This is the pure, host-free half of the fix: a per-load ledger that decides
//whether a message is being seen for the first time, and remembers how often
//each one came back so the loader can write a single summary line per
//distinct problem at the end of the parse. It deliberately knows nothing
//about MessageManager or the line-number prefix - the key is the message
//text alone, since the same problem appears on a different manifest line
//every time.
//
//Header-only on purpose: HdPackLoader.cpp is not on the Makefile's CUTSRC
//list, so logic that has to be unit-tested cannot live in it.
#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

class HdPackErrorDedupe
{
public:
	//Cap on the number of *distinct* messages retained for one load. Without
	//it a pathological pack with 100 000 distinct errors would reproduce the
	//very flood this class exists to stop, by another route, and the map
	//would grow without bound while parsing. 256 is far above what any real
	//pack produces (the worst one measured has 28) and still bounds both the
	//memory held during a load and the summary written at the end of it.
	//Occurrences past the cap are counted, not retained, and the loader
	//reports that count in one line.
	static constexpr size_t MaxDistinctMessages = 256;

	//True when the caller should log this occurrence now. That is only the
	//first sighting of a message, and only while the distinct-message budget
	//lasts; every repeat is counted and suppressed.
	bool ShouldLog(const std::string& message)
	{
		auto entry = _counts.find(message);
		if(entry != _counts.end()) {
			entry->second++;
			return false;
		}

		if(_counts.size() >= MaxDistinctMessages) {
			_unretained++;
			return false;
		}

		_counts.emplace(message, (uint32_t)1);
		_order.push_back(message);
		return true;
	}

	//The messages seen more than once, in first-sighting order, paired with
	//their true occurrence count. Messages seen exactly once are left out:
	//they were already logged verbatim and need no summary.
	std::vector<std::pair<std::string, uint32_t>> GetRepeated() const
	{
		std::vector<std::pair<std::string, uint32_t>> repeated;
		for(const std::string& message : _order) {
			auto entry = _counts.find(message);
			if(entry != _counts.end() && entry->second > 1) {
				repeated.emplace_back(message, entry->second);
			}
		}
		return repeated;
	}

	//Occurrences of messages that arrived after the distinct-message budget
	//was spent. These are the only errors this class loses track of.
	uint32_t GetUnretainedCount() const { return _unretained; }

	size_t GetDistinctCount() const { return _counts.size(); }

	//Called at the start of every load: the ledger is per load, not per
	//process, so a second pack reports its own errors in full.
	void Reset()
	{
		_counts.clear();
		_order.clear();
		_unretained = 0;
	}

private:
	std::unordered_map<std::string, uint32_t> _counts;
	std::vector<std::string> _order;
	uint32_t _unretained = 0;
};
