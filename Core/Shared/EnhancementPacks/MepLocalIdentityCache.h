#pragma once
#include "pch.h"

//ADR-0206 (P.1-local): the cache that gives a *stamp-less* local container the
//ADR-0139 content_id it would otherwise never have, so PRD Part B §5's
//local <-> catalog merge can fire (a hand-dropped copy of a catalog pack
//currently stays a second, competing `local:<container>` choice forever).
//
//The constraint that shapes everything here is PRD §3.3 rule 4 / §10: HD trees
//run to hundreds of MB, so the tree is **never** hashed on the synchronous
//ROM-load path. Two layers, two costs:
//
//  * the load reads `content-ids.json` and accepts an entry when the
//    container's own one-`stat` stamp still matches (no walk, no bytes);
//  * a background refresh, off the load path, recomputes each container's
//    stat-manifest *fingerprint* and re-hashes only the trees that moved.
//
//The fingerprint is what makes a change to a nested file visible: editing
//textures/foo.png changes that file's size/mtime line even though the
//container directory's own mtime never moves. It is a fingerprint, not a
//content hash - a rewrite that preserves size *and* mtime is not detected
//(ADR-0206 §2 states the limit); deleting the cache file is always safe and
//degrades to `local:<container>` until the next refresh.
class MepLocalIdentityCache
{
public:
	struct Entry
	{
		string ContainerPath;   //absolute root of the discovered pack
		string ContainerStamp;  //ComputeContainerStamp at write time
		string TreeFingerprint; //ComputeTreeFingerprint at write time
		string ContentId;       //ADR-0139 tree hash of the same tree
	};

	//The cache file for a packs folder: <packsFolder>/.cache/content-ids.json
	//(the same hidden folder ADR-0138 §4 uses for downloads and the pack scan
	//already skips).
	static string GetCacheFilePath(const string& packsFolder);

	//SHA-256 of the container root's own "<size>:<mtime>" (the shape
	//MepZipExtract's zip stamp uses), "" when the root cannot be stat'd. One
	//syscall: this is the load-path staleness check.
	static string ComputeContainerStamp(const string& folder);

	//SHA-256 over the tree's canonical stat manifest: one "<relpath>\t<size>
	//\t<mtime>" line per regular file, sorted byte-wise by that line, using the
	//same '/' relative paths, the same ADR-0139 exclusions and the same host
	//control-file skips as MepContentId::ComputeFolder - so a reinstall or a
	//re-extraction is not an edit, and a file that is not content cannot
	//invalidate the cache either. Never reads a file's bytes. "" when the tree
	//cannot be walked.
	static string ComputeTreeFingerprint(const string& folder);

	struct RefreshResult
	{
		int Scanned = 0;    //local containers found in the packs folder
		int Recomputed = 0; //fingerprint moved (or first sight): re-hashed
		int Pruned = 0;     //cache entries whose container is gone
	};

	//ADR-0206 §3/§6, the byte-reading half of the cache: walk `packsFolder`
	//(directories, plus the extractions of the .zip containers), revalidate
	//every local container's fingerprint and re-hash only the trees that
	//moved, then rewrite the cache file. Off the ROM-load path by contract -
	//the client runs it on a background thread after a game loads. Takes the
	//packs folder as a parameter so the unit tests can drive it against a
	//temporary tree instead of the user's own EnhancementPacks folder.
	static RefreshResult RefreshFolder(const string& packsFolder);

	//Missing, unreadable or corrupt file -> an empty cache; never throws.
	void Load(const string& cacheFilePath);
	//Atomic (temp file + rename); false when nothing could be written.
	bool Save(const string& cacheFilePath) const;

	//A container's entry, or nullptr. Path comparison is case-insensitive on
	//Windows and exact elsewhere.
	const Entry* Find(const string& containerPath) const;
	void Set(const Entry& entry);
	//Drops every entry whose ContainerPath is not in `live`; returns how many
	//went.
	int Prune(const vector<string>& live);

	int Count() const { return (int)_entries.size(); }

private:
	vector<Entry> _entries;
};
