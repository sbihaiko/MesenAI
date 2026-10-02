using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mesen.Logic
{
	//ADR-0240 Option 1 / PRD F6.9: after an install, a pack whose audio is
	//redeemed by a WIRED bundled ROM patch (ADR-0144, ADR-0148) but whose
	//<bgm>/<sfx> refs do not resolve to files in the installed pack gets ONE
	//non-fatal notice. Nothing is generated, spawned or applied; the install
	//stays Installed. Host-free (BCL only) so UI.Tests covers it; the
	//UI/Services coordinator calls Evaluate on the finished install folder.
	//
	//"Wired" mirrors scripts/mep_lint.py scan_bundled_patches: a .ips/.bps
	//that is present in the pack AND referenced by a `<patch>` line of a
	//hires.txt (resolved against that hires.txt's folder, as the loader does)
	//or a pack.json `patches[]` entry (pack-root-relative): exact pack-relative
	//path, case-insensitive, no basename fallback (HdPackLoader::
	//ProcessPatchTag has none). An unreferenced patch redeems nothing.
	//
	//"Unresolved" mirrors the loader (HdPackLoader::ProcessBgmTag/
	//ProcessSfxTag -> CheckFile): the ref, relative to the folder of the
	//hires.txt that names it, is looked up as written, then
	//case-insensitively (ResolvePackRelativePath). `<bgm>` filenames may
	//contain commas (re-joined with ", ") and a trailing all-digit token on a
	//4+ token `<bgm>` line is the loop position, not part of the name.
	//
	//M = distinct referenced files (resolved path, case-insensitive), not
	//lines: Zelda II-style packs list one file under several album/track ids.
	//Missing/Total: distinct referenced audio files that do not resolve, and
	//all of them. HasWiredPatch: a bundled .ips/.bps is wired (the meaning
	//above). The notice is due only when both hold (ADR-0240 Option 1).
	public sealed record PackAudioScan(int Missing, int Total, bool HasWiredPatch)
	{
		public bool ShowsNotice => Missing > 0 && HasWiredPatch;
	}

	public static class PackAudioNotice
	{
		//hires.txt locations of a MEP pack folder (MEP-v1 §2: sections
		//textures/ and audio/, plus a bare root hires.txt).
		private static readonly string[] HiresFolders = { "textures", "audio", "" };

		//Returns the notice text, or null when there is nothing to report.
		public static string? Evaluate(string packRoot)
		{
			PackAudioScan? scan = Scan(packRoot);
			if(scan == null || !scan.ShowsNotice) {
				return null;
			}
			return "audio not generated: " + scan.Missing + " of " + scan.Total + " tracks unresolved; supply the `.ogg` files";
		}

		//G.4 (W-P6): the same scan as Evaluate, as counts, so the pack detail
		//sheet can say it in plain words and show the Patch chip. Null when the
		//folder cannot be read (best effort, like Evaluate).
		public static PackAudioScan? Scan(string packRoot)
		{
			try {
				return ScanCore(packRoot);
			} catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) {
				//Best effort: a notice must never turn a finished install into a failure.
				return null;
			}
		}

		private static PackAudioScan ScanCore(string packRoot)
		{
			HashSet<string> references = new(StringComparer.OrdinalIgnoreCase); //pack-relative paths
			HashSet<string> tracks = new(StringComparer.OrdinalIgnoreCase);
			HashSet<string> unresolved = new(StringComparer.OrdinalIgnoreCase);

			foreach(string folder in HiresFolders) {
				string dir = folder.Length == 0 ? packRoot : Path.Combine(packRoot, folder);
				string hires = Path.Combine(dir, "hires.txt");
				if(!File.Exists(hires)) {
					continue;
				}
				string prefix = folder.Length == 0 ? "" : folder + "/";
				HashSet<string>? filesInDir = null;
				foreach(string raw in File.ReadLines(hires)) {
					string line = raw.Trim();
					if(line.StartsWith("<patch>", StringComparison.Ordinal)) {
						AddPatchReference(references, prefix, line.Substring("<patch>".Length));
					} else if(line.StartsWith("<bgm>", StringComparison.Ordinal) || line.StartsWith("<sfx>", StringComparison.Ordinal)) {
						string? name = ParseAudioFilename(line.Substring(5), line.StartsWith("<bgm>", StringComparison.Ordinal));
						if(name == null) {
							continue;
						}
						filesInDir ??= ListFiles(dir);
						string key = (prefix + name.Replace('\\', '/')).ToLowerInvariant();
						if(tracks.Add(key) && !filesInDir.Contains(name.Replace('\\', '/').ToLowerInvariant())) {
							unresolved.Add(key);
						}
					}
				}
			}

			string packJson = Path.Combine(packRoot, "pack.json");
			if(File.Exists(packJson)) {
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(packJson));
				if(doc.RootElement.ValueKind == JsonValueKind.Object &&
					doc.RootElement.TryGetProperty("patches", out JsonElement patches) && patches.ValueKind == JsonValueKind.Array) {
					foreach(JsonElement patch in patches.EnumerateArray()) {
						if(patch.ValueKind == JsonValueKind.Object && patch.TryGetProperty("file", out JsonElement file) &&
							file.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(file.GetString())) {
							AddReference(references, file.GetString()!.Trim());
						}
					}
				}
			}

			return new PackAudioScan(unresolved.Count, tracks.Count, HasWiredPatch(packRoot, references));
		}

		//<patch>file.ips,<sha1> - the sha1 is the LAST comma field and the
		//filename may itself contain commas (lint: `^(.*),\s*[0-9A-Fa-f]{40}\s*$`).
		private static void AddPatchReference(HashSet<string> references, string prefix, string parameters)
		{
			int comma = parameters.LastIndexOf(',');
			if(comma < 0) {
				return;
			}
			string sha = parameters.Substring(comma + 1).Trim();
			if(sha.Length != 40) {
				return;
			}
			foreach(char c in sha) {
				if(!Uri.IsHexDigit(c)) {
					return;
				}
			}
			string file = parameters.Substring(0, comma).Trim();
			if(file.Length > 0) {
				AddReference(references, prefix + file);
			}
		}

		private static void AddReference(HashSet<string> references, string packRelative)
		{
			references.Add(packRelative.Replace('\\', '/'));
		}

		private static bool HasWiredPatch(string packRoot, HashSet<string> references)
		{
			foreach(string path in Directory.EnumerateFiles(packRoot, "*", SearchOption.AllDirectories)) {
				if(!path.EndsWith(".ips", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".bps", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				string rel = Path.GetRelativePath(packRoot, path).Replace('\\', '/');
				if(references.Contains(rel)) {
					return true;
				}
			}
			return false;
		}

		//Lower-cased '/'-relative paths of every file under dir (the loader
		//indexes the pack folder the same way for its case-insensitive fallback).
		private static HashSet<string> ListFiles(string dir)
		{
			HashSet<string> files = new(StringComparer.Ordinal);
			foreach(string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
				files.Add(Path.GetRelativePath(dir, path).Replace('\\', '/').ToLowerInvariant());
			}
			return files;
		}

		//album,track,filename[,loopPosition] -> filename, or null when malformed.
		private static string? ParseAudioFilename(string parameters, bool isBgm)
		{
			string[] tokens = parameters.Split(',');
			for(int i = 0; i < tokens.Length; i++) {
				tokens[i] = tokens[i].Trim();
			}
			if(tokens.Length < 3) {
				return null;
			}
			int end = tokens.Length;
			if(isBgm && tokens.Length > 3 && IsAllDigits(tokens[^1])) {
				end--;
			}
			string name = string.Join(", ", tokens, 2, end - 2);
			return name.Length == 0 ? null : name;
		}

		private static bool IsAllDigits(string text)
		{
			if(text.Length == 0) {
				return false;
			}
			foreach(char c in text) {
				if(c < '0' || c > '9') {
					return false;
				}
			}
			return true;
		}
	}
}
