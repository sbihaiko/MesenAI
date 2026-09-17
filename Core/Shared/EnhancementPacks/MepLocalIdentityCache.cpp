#include "pch.h"
#include "Shared/EnhancementPacks/MepLocalIdentityCache.h"
#include "Shared/EnhancementPacks/MepContentId.h"
#include "Shared/EnhancementPacks/MepPack.h"
#include "Shared/EnhancementPacks/MepZipExtract.h"
#include "Utilities/FolderUtilities.h"
#include "Utilities/JsonReader.h"
#include "Utilities/sha256.h"
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iterator>

namespace fs = std::filesystem;

namespace
{
	constexpr const char* kCacheFileName = "content-ids.json";

	string EscapeJson(const string& value)
	{
		string out;
		out.reserve(value.size() + 8);
		for(char c : value) {
			switch(c) {
				case '"': out += "\\\""; break;
				case '\\': out += "\\\\"; break;
				case '\n': out += "\\n"; break;
				case '\r': out += "\\r"; break;
				case '\t': out += "\\t"; break;
				default:
					if((unsigned char)c < 0x20) {
						char buffer[8];
						snprintf(buffer, sizeof(buffer), "\\u%04x", (unsigned char)c);
						out += buffer;
					} else {
						out.push_back(c);
					}
					break;
			}
		}
		return out;
	}

	string Member(const string& key, const string& value)
	{
		return "\"" + key + "\":\"" + EscapeJson(value) + "\"";
	}

	bool MatchesPath(const string& a, const string& b)
	{
#ifdef _WIN32
		return StringUtilities::ToLower(a) == StringUtilities::ToLower(b);
#else
		return a == b;
#endif
	}

	//One line per regular file: the relative '/'-path, its size and its
	//last-write time. Excluded exactly like the content_id it guards.
	string StatManifest(const string& folder)
	{
		vector<string> lines;
		std::error_code ec;
		for(fs::recursive_directory_iterator it(fs::u8path(folder), ec), end; it != end; it.increment(ec)) {
			if(ec) {
				return "";
			}
			const fs::directory_entry& entry = *it;
			if(!entry.is_regular_file(ec)) {
				continue;
			}
			string rel = fs::relative(entry.path(), fs::u8path(folder), ec).generic_u8string();
			if(ec || rel.empty()) {
				return "";
			}
			if(MepContentId::IsHostControlFile(rel) || MepContentId::IsExcludedPath(rel)) {
				continue;
			}
			uintmax_t size = entry.file_size(ec);
			if(ec) {
				return "";
			}
			auto mtime = entry.last_write_time(ec).time_since_epoch().count();
			if(ec) {
				return "";
			}
			lines.push_back(rel + "\t" + std::to_string(size) + "\t" + std::to_string((long long)mtime));
		}
		std::sort(lines.begin(), lines.end());
		string manifest;
		for(const string& line : lines) {
			manifest += line;
			manifest.push_back('\n');
		}
		return manifest;
	}
}

string MepLocalIdentityCache::GetCacheFilePath(const string& packsFolder)
{
	return FolderUtilities::CombinePath(FolderUtilities::CombinePath(packsFolder, ".cache"), kCacheFileName);
}

string MepLocalIdentityCache::ComputeContainerStamp(const string& folder)
{
	std::error_code ec;
	if(folder.empty()) {
		return "";
	}
	fs::path path = fs::u8path(folder);
	if(!fs::is_directory(path, ec) || ec) {
		return "";
	}
	//The root's own size is meaningless for a directory; the mtime plus the
	//entry count is what moves when the container itself is replaced, and it
	//stays one syscall-ish probe (the same shape as the zip stamp).
	uintmax_t entries = 0;
	for(fs::directory_iterator it(path, ec), end; !ec && it != end; it.increment(ec)) {
		entries++;
	}
	if(ec) {
		return "";
	}
	auto mtime = fs::last_write_time(path, ec).time_since_epoch().count();
	if(ec) {
		return "";
	}
	string probe = std::to_string((long long)mtime) + ":" + std::to_string(entries);
	return SHA256::GetHash((const uint8_t*)probe.data(), probe.size());
}

string MepLocalIdentityCache::ComputeTreeFingerprint(const string& folder)
{
	if(folder.empty()) {
		return "";
	}
	std::error_code ec;
	if(!fs::is_directory(fs::u8path(folder), ec) || ec) {
		return "";
	}
	string manifest = StatManifest(folder);
	if(manifest.empty()) {
		return "";
	}
	return SHA256::GetHash((const uint8_t*)manifest.data(), manifest.size());
}

MepLocalIdentityCache::RefreshResult MepLocalIdentityCache::RefreshFolder(const string& packsFolder)
{
	RefreshResult result;
	string cacheRoot = FolderUtilities::CombinePath(packsFolder, ".cache");
	MepLocalIdentityCache cache;
	cache.Load(GetCacheFilePath(packsFolder));

	vector<string> live;
	std::error_code ec;
	for(fs::directory_iterator it(fs::u8path(packsFolder), ec), end; !ec && it != end; it.increment(ec)) {
		const fs::directory_entry& entry = *it;
		string name = entry.path().filename().u8string();
		if(name.empty() || name[0] == '.') {
			continue; //includes .cache
		}

		string root;
		if(entry.is_directory(ec)) {
			root = entry.path().u8string();
		} else if(entry.is_regular_file(ec) && StringUtilities::ToLower(FolderUtilities::GetExtension(name)) == ".zip") {
			//A zip is fingerprinted on its extraction, like the load does, and a
			//zip this machine never extracted has nothing to identify yet.
			string extracted = FolderUtilities::CombinePath(cacheRoot, FolderUtilities::GetFilename(name, false));
			string stamp, rootPrefix;
			if(!fs::is_directory(fs::u8path(extracted), ec) ||
				!MepZipExtract::ReadStamp(FolderUtilities::CombinePath(extracted, ".mep-source"), stamp, rootPrefix)) {
				continue;
			}
			root = extracted;
			if(!rootPrefix.empty()) {
				string combined;
				if(MepPack::NormalizeRelativePath(rootPrefix, combined) && !combined.empty()) {
					root = FolderUtilities::CombinePath(extracted, combined);
				}
			}
		} else {
			continue;
		}

		result.Scanned++;
		live.push_back(root);
		string fingerprint = ComputeTreeFingerprint(root);
		if(fingerprint.empty()) {
			continue; //unreadable tree: keep whatever was cached, try next time
		}
		const Entry* cached = cache.Find(root);
		if(cached != nullptr && cached->TreeFingerprint == fingerprint && !cached->ContentId.empty()) {
			continue;
		}
		string contentId = MepContentId::ComputeFolder(root);
		if(contentId.empty()) {
			continue;
		}
		Entry refreshed;
		refreshed.ContainerPath = root;
		refreshed.ContainerStamp = ComputeContainerStamp(root);
		refreshed.TreeFingerprint = fingerprint;
		refreshed.ContentId = contentId;
		cache.Set(refreshed);
		result.Recomputed++;
	}

	result.Pruned = cache.Prune(live);
	cache.Save(GetCacheFilePath(packsFolder));
	return result;
}

void MepLocalIdentityCache::Load(const string& cacheFilePath)
{
	_entries.clear();
	string text;
	{
		std::ifstream in(cacheFilePath, std::ios::in | std::ios::binary);
		if(!in) {
			return;
		}
		text.assign(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
	}
	JsonValue root;
	JsonReader reader;
	if(!reader.Parse(text, root) || !root.IsObject()) {
		return; //corrupt cache: rebuild from nothing, never fail the caller
	}
	const JsonValue* containers = root.Get("containers");
	if(containers == nullptr || !containers->IsArray()) {
		return;
	}
	for(const JsonValue& item : containers->GetArray()) {
		if(!item.IsObject()) {
			continue;
		}
		Entry entry;
		entry.ContainerPath = item.GetString("path");
		entry.ContainerStamp = item.GetString("container_stamp");
		entry.TreeFingerprint = item.GetString("tree_fingerprint");
		entry.ContentId = item.GetString("content_id");
		if(!entry.ContainerPath.empty() && !entry.ContentId.empty()) {
			_entries.push_back(std::move(entry));
		}
	}
}

bool MepLocalIdentityCache::Save(const string& cacheFilePath) const
{
	string json = "{\"version\":1,\"containers\":[";
	for(size_t i = 0; i < _entries.size(); i++) {
		const Entry& entry = _entries[i];
		if(i > 0) {
			json += ",";
		}
		json += "{" + Member("path", entry.ContainerPath) + "," + Member("container_stamp", entry.ContainerStamp) + "," +
			Member("tree_fingerprint", entry.TreeFingerprint) + "," + Member("content_id", entry.ContentId) + "}";
	}
	json += "]}";

	std::error_code ec;
	fs::create_directories(fs::u8path(FolderUtilities::GetFolderName(cacheFilePath)), ec);
	//Temp file + rename: a half-written cache must never be read back as a
	//short but valid document by the next load.
	string tempPath = cacheFilePath + ".tmp";
	{
		std::ofstream file(tempPath, std::ios::out | std::ios::binary | std::ios::trunc);
		if(!file) {
			return false;
		}
		file << json;
		if(!file) {
			return false;
		}
	}
	ec.clear();
#ifdef _WIN32
	fs::remove(fs::u8path(cacheFilePath), ec); //rename refuses an existing target
	ec.clear();
#endif
	fs::rename(fs::u8path(tempPath), fs::u8path(cacheFilePath), ec);
	return !ec;
}

const MepLocalIdentityCache::Entry* MepLocalIdentityCache::Find(const string& containerPath) const
{
	for(const Entry& entry : _entries) {
		if(MatchesPath(entry.ContainerPath, containerPath)) {
			return &entry;
		}
	}
	return nullptr;
}

void MepLocalIdentityCache::Set(const Entry& entry)
{
	for(Entry& existing : _entries) {
		if(MatchesPath(existing.ContainerPath, entry.ContainerPath)) {
			existing = entry;
			return;
		}
	}
	_entries.push_back(entry);
}

int MepLocalIdentityCache::Prune(const vector<string>& live)
{
	int removed = 0;
	vector<Entry> kept;
	kept.reserve(_entries.size());
	for(const Entry& entry : _entries) {
		bool alive = false;
		for(const string& path : live) {
			if(MatchesPath(entry.ContainerPath, path)) {
				alive = true;
				break;
			}
		}
		if(alive) {
			kept.push_back(entry);
		} else {
			removed++;
		}
	}
	_entries = std::move(kept);
	return removed;
}
