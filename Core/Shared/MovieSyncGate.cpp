#include "pch.h"
#include "Shared/MovieSyncGate.h"
#include <algorithm>
#include <sstream>

namespace
{
	std::string Trim(const std::string& s)
	{
		size_t a = s.find_first_not_of(" \t\r\n");
		if(a == std::string::npos) {
			return "";
		}
		size_t b = s.find_last_not_of(" \t\r\n");
		return s.substr(a, b - a + 1);
	}

	std::vector<std::string> Split(const std::string& s, char sep)
	{
		std::vector<std::string> parts;
		std::string current;
		std::istringstream in(s);
		while(std::getline(in, current, sep)) {
			parts.push_back(current);
		}
		if(!s.empty() && s.back() == sep) {
			parts.push_back("");
		}
		return parts;
	}

	bool ParseU32(const std::string& text, uint32_t& out)
	{
		std::string t = Trim(text);
		if(t.empty()) {
			return false;
		}
		for(char c : t) {
			if(c < '0' || c > '9') {
				return false;
			}
		}
		out = (uint32_t)strtoul(t.c_str(), nullptr, 10);
		return true;
	}
}

std::string MovieSyncGate::RuleName(MovieSyncRule rule)
{
	switch(rule) {
		case MovieSyncRule::NeverDecreases: return "never-decreases";
		case MovieSyncRule::NeverIncreases: return "never-increases";
		case MovieSyncRule::NeverBelow: return "never-below";
		case MovieSyncRule::NeverEquals: return "never-equals";
	}
	return "?";
}

bool MovieSyncGate::ParseWatch(const std::string& spec, MovieSyncWatch& out, std::string& error)
{
	error.clear();
	out = MovieSyncWatch();

	std::vector<std::string> parts = Split(spec, ':');
	if(parts.size() < 2 || parts.size() > 3) {
		error = "expected <hexaddr>:<rule>[=<n>][:<label>], got \"" + spec + "\"";
		return false;
	}

	std::string addrText = Trim(parts[0]);
	if(addrText.empty() || addrText.size() > 4) {
		error = "address \"" + addrText + "\" is not 1-4 hex digits";
		return false;
	}
	uint32_t addr = 0;
	for(char c : addrText) {
		int digit;
		if(c >= '0' && c <= '9') {
			digit = c - '0';
		} else if(c >= 'a' && c <= 'f') {
			digit = c - 'a' + 10;
		} else if(c >= 'A' && c <= 'F') {
			digit = c - 'A' + 10;
		} else {
			error = "address \"" + addrText + "\" is not hexadecimal";
			return false;
		}
		addr = addr * 16 + (uint32_t)digit;
	}
	//ADR-0184's rule, for its reason: only the NES internal RAM is a watchable
	//game variable. Anything at or above $0800 is a mirror, a register or the
	//cartridge, and a "counter" read there is not the game's counter.
	if(addr >= 0x0800) {
		error = "address $" + addrText + " is not below $0800 - only the NES internal RAM is watchable (ADR-0184)";
		return false;
	}
	out.Address = (uint16_t)addr;

	std::string ruleText = Trim(parts[1]);
	std::string operandText;
	size_t eq = ruleText.find('=');
	if(eq != std::string::npos) {
		operandText = Trim(ruleText.substr(eq + 1));
		ruleText = Trim(ruleText.substr(0, eq));
	}
	if(ruleText == "never-decreases") {
		out.Rule = MovieSyncRule::NeverDecreases;
	} else if(ruleText == "never-increases") {
		out.Rule = MovieSyncRule::NeverIncreases;
	} else if(ruleText == "never-below") {
		out.Rule = MovieSyncRule::NeverBelow;
	} else if(ruleText == "never-equals") {
		out.Rule = MovieSyncRule::NeverEquals;
	} else {
		error = "unknown rule \"" + ruleText + "\" - expected never-decreases, never-increases, never-below or never-equals";
		return false;
	}

	bool needsOperand = out.Rule == MovieSyncRule::NeverBelow || out.Rule == MovieSyncRule::NeverEquals;
	if(needsOperand) {
		uint32_t operand = 0;
		if(!ParseU32(operandText, operand) || operand > 255) {
			error = "rule " + RuleName(out.Rule) + " needs \"=<n>\" with n in 0..255";
			return false;
		}
		out.Operand = (int32_t)operand;
	} else if(!operandText.empty()) {
		error = "rule " + RuleName(out.Rule) + " takes no \"=<n>\" operand";
		return false;
	}

	out.Label = parts.size() == 3 ? Trim(parts[2]) : "";
	if(out.Label.empty()) {
		char buffer[16];
		snprintf(buffer, sizeof(buffer), "%04X", out.Address);
		out.Label = std::string("ram") + buffer;
	}
	//The label becomes a CSV column name; a separator in it would silently
	//re-shape the trace.
	if(out.Label.find(',') != std::string::npos || out.Label.find(':') != std::string::npos) {
		error = "label \"" + out.Label + "\" must not contain ',' or ':'";
		return false;
	}
	return true;
}

std::string MovieSyncGate::FormatTraceHeader(const std::vector<MovieSyncWatch>& watches)
{
	std::string header = "frame,moviePlaying,tilesSeen,tilesWithArt,screensSeen";
	for(const MovieSyncWatch& watch : watches) {
		header += ",watch:" + watch.Label;
	}
	return header + "\n";
}

std::string MovieSyncGate::FormatTraceRow(const MovieSyncSample& sample)
{
	std::string row = std::to_string(sample.Frame) + "," + (sample.MoviePlaying ? "1" : "0") + "," +
		std::to_string(sample.TilesSeen) + "," + std::to_string(sample.TilesWithArt) + "," +
		std::to_string(sample.ScreensSeen);
	for(uint8_t value : sample.WatchValues) {
		row += "," + std::to_string((uint32_t)value);
	}
	return row + "\n";
}

bool MovieSyncGate::ParseTrace(const std::string& text, std::vector<MovieSyncSample>& outSamples,
	std::vector<std::string>& outWatchLabels, std::string& error)
{
	outSamples.clear();
	outWatchLabels.clear();
	error.clear();

	std::istringstream in(text);
	std::string line;
	bool haveHeader = false;
	size_t columns = 0;
	uint32_t lineNumber = 0;
	while(std::getline(in, line)) {
		lineNumber++;
		std::string trimmed = Trim(line);
		if(trimmed.empty() || trimmed[0] == '#') {
			continue;
		}
		std::vector<std::string> fields = Split(trimmed, ',');
		if(!haveHeader) {
			if(fields.size() < 5 || Trim(fields[0]) != "frame") {
				error = "line " + std::to_string(lineNumber) + ": not a sync trace header";
				return false;
			}
			for(size_t i = 5; i < fields.size(); i++) {
				std::string name = Trim(fields[i]);
				if(name.rfind("watch:", 0) != 0) {
					error = "line " + std::to_string(lineNumber) + ": column \"" + name + "\" is not a watch column";
					return false;
				}
				outWatchLabels.push_back(name.substr(6));
			}
			columns = fields.size();
			haveHeader = true;
			continue;
		}
		if(fields.size() != columns) {
			error = "line " + std::to_string(lineNumber) + ": expected " + std::to_string(columns) +
				" fields, got " + std::to_string(fields.size());
			return false;
		}
		MovieSyncSample sample;
		uint32_t value = 0;
		if(!ParseU32(fields[0], sample.Frame) || !ParseU32(fields[1], value)) {
			error = "line " + std::to_string(lineNumber) + ": frame and moviePlaying must be numbers";
			return false;
		}
		sample.MoviePlaying = value != 0;
		if(!ParseU32(fields[2], sample.TilesSeen) || !ParseU32(fields[3], sample.TilesWithArt) ||
			!ParseU32(fields[4], sample.ScreensSeen)) {
			error = "line " + std::to_string(lineNumber) + ": the three counters must be numbers";
			return false;
		}
		for(size_t i = 5; i < fields.size(); i++) {
			if(!ParseU32(fields[i], value) || value > 255) {
				error = "line " + std::to_string(lineNumber) + ": watch value must be a byte";
				return false;
			}
			sample.WatchValues.push_back((uint8_t)value);
		}
		if(!outSamples.empty() && sample.Frame < outSamples.back().Frame) {
			error = "line " + std::to_string(lineNumber) + ": frames must not go backwards";
			return false;
		}
		outSamples.push_back(sample);
	}
	if(!haveHeader) {
		error = "the trace has no header line";
		return false;
	}
	return true;
}

namespace
{
	//The baseline's counter at the last baseline sample no later than `frame`.
	//Both runs are deterministic from power-on and sampled on the same cadence,
	//so this is a matched-frame read and not an interpolation; when the two
	//cadences differ it degrades to "the baseline's value as of that frame",
	//which is the conservative direction (it understates the baseline, so it
	//can only make the movie run look better, never worse).
	uint32_t BaselineAt(const std::vector<MovieSyncSample>& baseline, uint32_t frame, bool& found)
	{
		found = false;
		uint32_t value = 0;
		for(const MovieSyncSample& sample : baseline) {
			if(sample.Frame > frame) {
				break;
			}
			value = sample.TilesSeen;
			found = true;
		}
		return value;
	}
}

std::vector<MovieSyncFinding> MovieSyncGate::Evaluate(const std::vector<MovieSyncSample>& movieRun,
	const std::vector<MovieSyncSample>& baseline, const std::vector<MovieSyncWatch>& watches,
	const MovieSyncParams& params)
{
	std::vector<MovieSyncFinding> findings;
	if(movieRun.empty()) {
		findings.push_back({ "empty-trace", true, 0, "the movie-driven run produced no sync trace at all" });
		return findings;
	}

	//--- Rule 1: the declared RAM invariants --------------------------------
	//Only while the movie is driving the pad. After the movie's input runs out
	//the console is nobody's playthrough and losing a life there proves
	//nothing; before the first sample there is no previous value to compare.
	for(size_t w = 0; w < watches.size(); w++) {
		const MovieSyncWatch& watch = watches[w];
		bool havePrevious = false;
		uint32_t previous = 0;
		char address[16];
		snprintf(address, sizeof(address), "$%04X", watch.Address);
		for(const MovieSyncSample& sample : movieRun) {
			if(w >= sample.WatchValues.size()) {
				continue;
			}
			uint32_t value = sample.WatchValues[w];
			if(!sample.MoviePlaying) {
				havePrevious = false;
				continue;
			}
			bool violated = false;
			std::string what;
			switch(watch.Rule) {
				case MovieSyncRule::NeverDecreases:
					violated = havePrevious && value < previous;
					what = std::to_string(previous) + " -> " + std::to_string(value);
					break;
				case MovieSyncRule::NeverIncreases:
					violated = havePrevious && value > previous;
					what = std::to_string(previous) + " -> " + std::to_string(value);
					break;
				case MovieSyncRule::NeverBelow:
					violated = (int32_t)value < watch.Operand;
					what = std::to_string(value) + " < " + std::to_string(watch.Operand);
					break;
				case MovieSyncRule::NeverEquals:
					violated = (int32_t)value == watch.Operand;
					what = std::to_string(value) + " == " + std::to_string(watch.Operand);
					break;
			}
			if(violated) {
				findings.push_back({ "watch-violated", true, sample.Frame,
					"the declared invariant \"" + watch.Label + "\" (" + address + ", " + RuleName(watch.Rule) +
					") broke at frame " + std::to_string(sample.Frame) + ": " + what +
					" while the movie was still driving the pad - the run diverged from the playthrough the movie describes (ADR-0185 sec. 4, issue #201)" });
				break; //one finding per watch; the first frame is the evidence
			}
			previous = value;
			havePrevious = true;
		}
	}

	//--- Rule 2: the movie's input ran dry where its rows say it should -----
	if(params.ExpectedMovieEndFrame > 0) {
		bool everPlayed = false;
		bool stopped = false;
		uint32_t stopFrame = 0;
		for(const MovieSyncSample& sample : movieRun) {
			if(sample.MoviePlaying) {
				everPlayed = true;
			} else if(everPlayed && !stopped) {
				stopped = true;
				stopFrame = sample.Frame;
			}
		}
		if(!everPlayed) {
			findings.push_back({ "movie-stopped-early", true, 0,
				"no sample in the trace has the movie playing - the Core never ran it" });
		} else if(stopped && stopFrame < params.ExpectedMovieEndFrame) {
			findings.push_back({ "movie-stopped-early", true, stopFrame,
				"the movie stopped by frame " + std::to_string(stopFrame) + ", before frame " +
				std::to_string(params.ExpectedMovieEndFrame) + " where its own row count says its input runs dry - "
				"something other than the input running out ended it" });
		}
	}

	if(baseline.empty()) {
		findings.push_back({ "no-baseline", false, 0,
			"no movie-less baseline trace was given: the total and prefix comparisons of ADR-0185 sec. 4 were not run" });
		return findings;
	}

	//--- Rule 3a: the original total, at the last matched frame -------------
	uint32_t lastFrame = movieRun.back().Frame;
	bool haveBaseline = false;
	uint32_t baselineTotal = BaselineAt(baseline, lastFrame, haveBaseline);
	if(!haveBaseline) {
		findings.push_back({ "no-baseline", false, 0,
			"the baseline trace starts after the movie run ended - nothing to compare at a matched frame" });
		return findings;
	}
	if(movieRun.back().TilesSeen <= baselineTotal) {
		findings.push_back({ "no-gain-over-baseline", true, lastFrame,
			"at frame " + std::to_string(lastFrame) + " the movie-driven run has " +
			std::to_string(movieRun.back().TilesSeen) + " distinct tile shapes against the movie-less run's " +
			std::to_string(baselineTotal) + " - a run that does not beat the baseline has desynced, whatever the logs say" });
	}

	//--- Rule 3b: the same counter, read at every sampled frame -------------
	//The total is one number: the run's distinct-shape count at the last frame.
	//The trace is that same number at every sampled frame, for the same two
	//runs and the same cost, and it is the extra information that localises a
	//partial desync. A run that is really being driven keeps meeting material
	//the game has not drawn yet; a run that has died into GAME OVER and the
	//attract loop is back in the state class the movie-less console occupies
	//and stops meeting any. The total cannot see that, because the shapes the
	//good prefix already banked stay in it forever - it is the END of the curve,
	//and the evidence is in the curve's SHAPE.
	//
	//Measured on the issue #201 Castlevania run: the movie-driven counter is
	//547 at frame 3601 and 559 at frame 30601 - twelve new shapes in 27000
	//frames, against 513 in the first 1800 - while the movie played every one
	//of its 36788 rows.
	//
	//Never fatal. A good long run also stops learning once it has seen the
	//game's whole vocabulary, and a gate that failed those would be switched
	//off within a week, which is worse than having none. This names a window
	//and a frame to look at; the declared invariant of rule 1 is what fails a
	//run.
	uint32_t windowStartFrame = 0;
	uint32_t windowStartTiles = 0;
	bool haveWindowStart = false;
	uint32_t stalledWindows = 0;
	uint32_t stallBeganAt = 0;
	uint32_t stallTiles = 0;
	bool reported = false;
	//The lead over the baseline, and the frame it was largest at. On a run that
	//tracks its movie to the end this peak is at or near the last frame; a
	//partial desync freezes it early and never recovers, which is the same
	//evidence said with the baseline in it.
	int64_t peakLead = 0;
	uint32_t peakLeadFrame = 0;
	bool havePeak = false;
	uint32_t lastPlayingFrame = 0;
	int64_t lastPlayingLead = 0;

	for(const MovieSyncSample& sample : movieRun) {
		if(!sample.MoviePlaying) {
			//Past the movie's end the run is nobody's playthrough; a plateau
			//there is expected and says nothing.
			break;
		}
		bool ok = false;
		int64_t lead = (int64_t)sample.TilesSeen - (int64_t)BaselineAt(baseline, sample.Frame, ok);
		//A warm-up the lead is not read over. The two runs do not sample the
		//same frame numbers at the start - a movie power-cycles the console, so
		//the movie-driven trace begins at frame 1 and the movie-less one at
		//frame 3 - and over the first seconds, when both counters are climbing
		//by hundreds, a two-frame lag reads as a lead of 243 that is nothing
		//but the lag. Measured on the Castlevania run: peak "lead" at frame 61
		//before this guard, at frame 3601 after it, which is the window the run
		//actually diverged in.
		if(ok && sample.Frame >= params.StallWindowFrames) {
			if(!havePeak || lead > peakLead) {
				peakLead = lead;
				peakLeadFrame = sample.Frame;
				havePeak = true;
			}
			lastPlayingFrame = sample.Frame;
			lastPlayingLead = lead;
		}
		if(!haveWindowStart) {
			windowStartFrame = sample.Frame;
			windowStartTiles = sample.TilesSeen;
			haveWindowStart = true;
			continue;
		}
		if(sample.Frame - windowStartFrame < params.StallWindowFrames) {
			continue;
		}
		if(sample.TilesSeen == windowStartTiles) {
			if(stalledWindows == 0) {
				stallBeganAt = windowStartFrame;
				stallTiles = windowStartTiles;
			}
			stalledWindows++;
			if(stalledWindows >= params.StallWindows && !reported) {
				reported = true;
				findings.push_back({ "prefix-stall", false, stallBeganAt,
					"from frame " + std::to_string(stallBeganAt) + " to frame " + std::to_string(sample.Frame) +
					" the movie-driven run drew no tile shape it had not already seen (" + std::to_string(stallTiles) +
					" shapes throughout) while the movie was still driving the pad. Either the run has seen everything the "
					"game draws, or it stopped being the movie's playthrough before frame " + std::to_string(stallBeganAt) +
					". Check a screenshot there; this alone does not fail the run" });
			}
		} else {
			stalledWindows = 0;
		}
		windowStartFrame = sample.Frame;
		windowStartTiles = sample.TilesSeen;
	}

	if(havePeak && peakLeadFrame < lastPlayingFrame && lastPlayingLead < peakLead) {
		findings.push_back({ "lead-peaked-early", false, peakLeadFrame,
			"the movie-driven run's lead over the movie-less baseline was largest at frame " + std::to_string(peakLeadFrame) +
			" (" + std::to_string(peakLead) + " shapes) and only " + std::to_string(lastPlayingLead) + " at frame " +
			std::to_string(lastPlayingFrame) + ", the last frame the movie was driving: after that peak the run stopped "
			"out-learning a console nobody was playing. The total of ADR-0185 sec. 4 is this number at the last frame alone, "
			"which is why it cannot see where the run stopped tracking its movie" });
	}

	return findings;
}

bool MovieSyncGate::IsFatal(const std::vector<MovieSyncFinding>& findings)
{
	for(const MovieSyncFinding& finding : findings) {
		if(finding.Fatal) {
			return true;
		}
	}
	return false;
}
