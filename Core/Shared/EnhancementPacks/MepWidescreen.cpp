#include "pch.h"
#include "Shared/EnhancementPacks/MepWidescreen.h"
#include "Shared/EnhancementPacks/MepPack.h"
#include "Utilities/JsonReader.h"

//MEP-v1 §5.5 (ADR-0253 §3, slice W.3): the `<widescreen>` section manifest.
//Host-free and filesystem-free - it parses text and the caller does the I/O -
//so scripts/core_unit_tests.cpp drives it directly. Path safety is the same
//rule the container itself uses (MEP-v1 §2.3), so it is delegated to
//MepPack::NormalizeRelativePath instead of being spelled a second time.

namespace
{
	constexpr int32_t kSupportedVersion = 1;

	//Every image field is a section-relative .png reference: safe to join onto
	//the section folder (no '..', no absolute, no control characters) and the
	//kind of file the loader can decode.
	bool ReadImagePath(const JsonValue& parent, const char* key, string& out, string& error)
	{
		const JsonValue* value = parent.Get(key);
		if(!value) {
			out.clear();
			return true;
		}
		if(!value->IsString()) {
			error = string("field '") + key + "' must be a string";
			return false;
		}
		const string& raw = value->GetString();
		if(!MepWidescreen::IsValidImagePath(raw)) {
			error = string("field '") + key + "': '" + raw + "' is not a safe relative .png path";
			return false;
		}
		string normalized;
		if(!MepPack::NormalizeRelativePath(raw, normalized)) {
			error = string("field '") + key + "': unsafe path '" + raw + "'";
			return false;
		}
		out = normalized;
		return true;
	}
}

bool MepWidescreen::IsValidImagePath(const string& path)
{
	if(path.empty()) {
		return false;
	}
	//Case-insensitive ".png" suffix
	if(path.size() < 4) {
		return false;
	}
	string ext = path.substr(path.size() - 4);
	for(char& c : ext) {
		c = (char)tolower((unsigned char)c);
	}
	if(ext != ".png") {
		return false;
	}
	string normalized;
	return MepPack::NormalizeRelativePath(path, normalized);
}

bool MepWidescreen::Parse(const string& json, MepWidescreen& out, string& error)
{
	out = MepWidescreen();

	JsonReader reader;
	JsonValue root;
	if(!reader.Parse(json, root) || !root.IsObject()) {
		error = "manifest must be a JSON object";
		return false;
	}

	const JsonValue* version = root.Get("version");
	if(!version || !version->IsNumber() || (int32_t)version->GetNumber() != kSupportedVersion) {
		error = "field 'version' must be 1";
		return false;
	}

	if(!ReadImagePath(root, "left", out.Left, error) || !ReadImagePath(root, "right", out.Right, error)) {
		return false;
	}

	const JsonValue* screens = root.Get("screens");
	if(screens) {
		if(!screens->IsArray()) {
			error = "field 'screens' must be an array";
			return false;
		}
		for(const JsonValue& entry : screens->GetArray()) {
			if(!entry.IsObject()) {
				error = "every 'screens' entry must be an object";
				return false;
			}
			const JsonValue* id = entry.Get("id");
			if(!id || !id->IsNumber() || id->GetNumber() < 0 || id->GetNumber() != (double)(int64_t)id->GetNumber()) {
				error = "every 'screens' entry needs a non-negative integer 'id'";
				return false;
			}
			MepWidescreenScreen screen;
			screen.Id = (int32_t)id->GetNumber();
			for(const MepWidescreenScreen& seen : out.Screens) {
				if(seen.Id == screen.Id) {
					error = "duplicated screen id " + std::to_string(screen.Id);
					return false;
				}
			}
			if(!ReadImagePath(entry, "left", screen.Left, error) || !ReadImagePath(entry, "right", screen.Right, error)) {
				error = "screen " + std::to_string(screen.Id) + ": " + error;
				return false;
			}
			if(screen.Left.empty() && screen.Right.empty()) {
				error = "screen " + std::to_string(screen.Id) + " names no image";
				return false;
			}
			out.Screens.push_back(std::move(screen));
		}
	}

	if(!out.HasAnyArt()) {
		error = "the section names no image on either side";
		return false;
	}
	return true;
}

const MepWidescreenScreen* MepWidescreen::FindScreen(int32_t screenId) const
{
	if(screenId == DefaultScreenId) {
		return nullptr;
	}
	for(const MepWidescreenScreen& screen : Screens) {
		if(screen.Id == screenId) {
			return &screen;
		}
	}
	return nullptr;
}

string MepWidescreen::GetSidePath(int32_t screenId, bool left) const
{
	const MepWidescreenScreen* screen = FindScreen(screenId);
	if(screen) {
		const string& override = left ? screen->Left : screen->Right;
		if(!override.empty()) {
			return override;
		}
	}
	return left ? Left : Right;
}
