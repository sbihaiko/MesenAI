#pragma once
#include "pch.h"
#include "Utilities/JsonReader.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <ctime>
#include <filesystem>

//ADR-0243 (F12.20): a Remaster project is the ROM's enhancement folder
//(ADR-0049 sibling, or EnhancementPacks/<Game>/ when the ROM folder is
//read-only). Every recording the bootstrap makes is one complete output under
//auto/rec-NNN/ (textures/, audio/); a bare auto/textures/ or auto/audio/
//written before the ADR reads as rec-001 and is never moved. project.json at
//the project root is the machine-written list of recordings - hosts ignore it
//(MEP-v1 §2.1), and a project without one derives the list from folder names
//(scripts/mep_project.py does that half).
//
//Header-only and host-free on purpose: scripts/core_unit_tests.cpp drives the
//id allocation, the decline rule and the manifest format without linking
//MepPackManager (which pulls in Emulator and the consoles).
namespace RemasterProject
{
	constexpr const char* RecordingPrefix = "rec-";
	constexpr const char* ManifestFileName = "project.json";

	//"rec-012" -> 12. Anything that is not "rec-" followed by digits only, or
	//that parses as 0, is not a recording folder and returns 0.
	inline int ParseRecordingNumber(const string& name)
	{
		const size_t prefixLength = 4;
		if(name.size() <= prefixLength || name.compare(0, prefixLength, RecordingPrefix) != 0 || name.size() > prefixLength + 6) {
			return 0;
		}
		int value = 0;
		for(size_t i = prefixLength; i < name.size(); i++) {
			if(name[i] < '0' || name[i] > '9') {
				return 0;
			}
			value = value * 10 + (name[i] - '0');
		}
		return value;
	}

	inline string FormatRecordingId(int number)
	{
		char buffer[32];
		snprintf(buffer, sizeof(buffer), "rec-%03d", number);
		return buffer;
	}

	//True for the section folders a bare (pre-ADR-0243) recording leaves
	//directly under auto/: that layout is rec-001.
	inline bool IsBareRecordingSection(const string& name)
	{
		return name == "textures" || name == "audio";
	}

	//The id the next recording gets, from the names of auto/'s children: one
	//past the highest rec-NNN, a bare auto/textures or auto/audio counting as
	//rec-001. A gap left by a deleted recording is never refilled, so an id
	//names one recording for the life of the project.
	inline string NextRecordingId(const vector<string>& autoChildren)
	{
		int highest = 0;
		for(const string& name : autoChildren) {
			highest = std::max(highest, IsBareRecordingSection(name) ? 1 : ParseRecordingNumber(name));
		}
		return FormatRecordingId(highest + 1);
	}

	//Names of the directories directly under autoRoot ("" entries never).
	inline vector<string> ListChildFolders(const string& autoRoot)
	{
		vector<string> names;
		std::error_code ec;
		for(std::filesystem::directory_iterator it(std::filesystem::u8path(autoRoot), ec), end; !ec && it != end; it.increment(ec)) {
			if(it->is_directory(ec)) {
				names.push_back(it->path().filename().u8string());
			}
		}
		return names;
	}

	//The rec-NNN folder names under autoRoot, newest (highest id) first.
	inline vector<string> ListRecordingsNewestFirst(const string& autoRoot)
	{
		vector<std::pair<int, string>> found;
		for(const string& name : ListChildFolders(autoRoot)) {
			int number = ParseRecordingNumber(name);
			if(number > 0) {
				found.emplace_back(number, name);
			}
		}
		std::sort(found.begin(), found.end(), [](const auto& a, const auto& b) { return a.first > b.first; });
		vector<string> names;
		for(auto& entry : found) {
			names.push_back(std::move(entry.second));
		}
		return names;
	}

	//--- The decline rule (ADR-0243 Decision 3, issue #142) -----------------

	//Who dresses one section of the ROM right now: nobody, the project being
	//recorded into, or another pack.
	struct LayerOwner
	{
		bool Present = false;
		bool OwnProject = false;
	};

	struct RecordingPlan
	{
		bool NeedTextures = false;
		bool NeedAudio = false;
		bool Declined() const { return !NeedTextures && !NeedAudio; }
	};

	//A winning section belongs to the project when its container *is* the
	//project folder and its human layer, if any, is the project's own mep/
	//(ADR-0147). An auto-only section of the project is its own earlier
	//recordings. A human layer at the sibling root (the pre-ADR-0147 layout)
	//is treated as someone's pack and keeps declining: the guard errs on the
	//side of never recording over art the project did not make.
	inline bool IsOwnProjectSection(bool containerIsProject, bool hasHuman, const string& humanPath)
	{
		if(!containerIsProject) {
			return false;
		}
		return !hasHuman || humanPath.compare(0, 4, "mep/") == 0;
	}

	//Which halves a recording writes. A foreign pack (community, loose
	//HdPacks/<rom>/, a legacy sibling layout) still declines its section - the
	//bootstrap is a first draft, never an override of a pack someone already
	//has (#142). The project's own mep/ and its earlier recordings do not
	//count: without that exemption every second recording would be refused.
	inline RecordingPlan PlanRecording(LayerOwner textures, bool looseHdPackExists, LayerOwner audio, bool audioSupported)
	{
		RecordingPlan plan;
		plan.NeedTextures = (!textures.Present || textures.OwnProject) && !looseHdPackExists;
		plan.NeedAudio = audioSupported && (!audio.Present || audio.OwnProject);
		return plan;
	}

	//--- project.json (ADR-0243 Q2) -----------------------------------------

	inline bool IsKnownSource(const string& source)
	{
		return source == "play" || source == "tas" || source == "ai" || source == "script";
	}

	//durationSeconds is the recording's own emulated length (#612). The
	//console's frame counter is not a clock the recording owns: a save state
	//restores the counter it was saved at, so a recording started on load and
	//then fed a state minted at frame 1362 would otherwise count from power-on.
	struct RecordingClock
	{
		uint32_t StartFrame = 0;
		uint64_t BankedFrames = 0;

		void Start(uint32_t frame)
		{
			StartFrame = frame;
			BankedFrames = 0;
		}

		//The counter jumped from `before` to `after` (a state load): bank what
		//was played so far and count on from the restored frame
		void Rebase(uint32_t before, uint32_t after)
		{
			BankedFrames = ElapsedFrames(before);
			StartFrame = after;
		}

		uint64_t ElapsedFrames(uint32_t now) const
		{
			return BankedFrames + (now >= StartFrame ? now - StartFrame : 0);
		}
	};

	//Emulated seconds, rounded to a hundredth: the frame count is the run's
	//truth, the wall clock is not (a headless run is faster than real time)
	inline double DurationSecondsFor(uint64_t frames, double fps)
	{
		return fps > 0 ? std::round(frames / fps * 100.0) / 100.0 : 0;
	}

	struct Recording
	{
		string Id;
		string RecordedAt;
		string Source;
		double DurationSeconds = 0;
		string Note;
	};

	struct Manifest
	{
		string Name;
		vector<Recording> Recordings;
	};

	inline void UpsertRecording(Manifest& manifest, const Recording& recording)
	{
		for(Recording& existing : manifest.Recordings) {
			if(existing.Id == recording.Id) {
				existing = recording;
				return;
			}
		}
		manifest.Recordings.push_back(recording);
		std::sort(manifest.Recordings.begin(), manifest.Recordings.end(), [](const Recording& a, const Recording& b) {
			int na = ParseRecordingNumber(a.Id), nb = ParseRecordingNumber(b.Id);
			return na != nb ? na < nb : a.Id < b.Id;
		});
	}

	inline string EscapeJsonString(const string& value)
	{
		string out = "\"";
		for(unsigned char c : value) {
			switch(c) {
				case '"': out += "\\\""; break;
				case '\\': out += "\\\\"; break;
				case '\n': out += "\\n"; break;
				case '\r': out += "\\r"; break;
				case '\t': out += "\\t"; break;
				default:
					if(c < 0x20) {
						char buffer[8];
						snprintf(buffer, sizeof(buffer), "\\u%04x", c);
						out += buffer;
					} else {
						out += (char)c;
					}
					break;
			}
		}
		return out + "\"";
	}

	//Fewest fixed decimals (up to six) that read back as the same double, so a
	//duration never drifts across a read-modify-write of the manifest and
	//reads as "30" or "61.25", never "3e+01"; %.17g past that.
	inline string FormatNumber(double value)
	{
		char buffer[64];
		for(int decimals = 0; decimals <= 6; decimals++) {
			snprintf(buffer, sizeof(buffer), "%.*f", decimals, value);
			if(strtod(buffer, nullptr) == value) {
				return buffer;
			}
		}
		snprintf(buffer, sizeof(buffer), "%.17g", value);
		return buffer;
	}

	inline string FormatManifest(const Manifest& manifest)
	{
		string out = "{\n  \"name\": " + EscapeJsonString(manifest.Name) + ",\n  \"recordings\": [";
		for(size_t i = 0; i < manifest.Recordings.size(); i++) {
			const Recording& r = manifest.Recordings[i];
			out += i == 0 ? "\n" : ",\n";
			out += "    {\"id\": " + EscapeJsonString(r.Id) + ", \"recordedAt\": " + EscapeJsonString(r.RecordedAt) +
				", \"source\": " + EscapeJsonString(r.Source) + ", \"durationSeconds\": " + FormatNumber(r.DurationSeconds) +
				", \"note\": " + EscapeJsonString(r.Note) + "}";
		}
		out += manifest.Recordings.empty() ? "]\n}\n" : "\n  ]\n}\n";
		return out;
	}

	//False (and "out" untouched) when the text is not a JSON object. Entries
	//without a string id are skipped; unknown fields are ignored.
	inline bool ParseManifest(const string& text, Manifest& out)
	{
		JsonValue root;
		JsonReader reader;
		if(!reader.Parse(text, root) || !root.IsObject()) {
			return false;
		}
		Manifest parsed;
		parsed.Name = root.GetString("name");
		const JsonValue* list = root.Get("recordings");
		if(list && list->IsArray()) {
			for(const JsonValue& item : list->GetArray()) {
				if(!item.IsObject() || item.GetString("id").empty()) {
					continue;
				}
				Recording r;
				r.Id = item.GetString("id");
				r.RecordedAt = item.GetString("recordedAt");
				r.Source = item.GetString("source");
				r.Note = item.GetString("note");
				const JsonValue* duration = item.Get("durationSeconds");
				r.DurationSeconds = duration && duration->IsNumber() ? duration->GetNumber() : 0;
				UpsertRecording(parsed, r);
			}
		}
		out = std::move(parsed);
		return true;
	}

	//ISO-8601 UTC, second precision ("2026-10-02T12:00:00Z").
	inline string FormatUtcTimestamp(time_t when)
	{
		std::tm utc = {};
#ifdef _MSC_VER
		gmtime_s(&utc, &when);
#else
		gmtime_r(&when, &utc);
#endif
		char buffer[32];
		strftime(buffer, sizeof(buffer), "%Y-%m-%dT%H:%M:%SZ", &utc);
		return buffer;
	}
}
