using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mesen.Logic;

//ADR-0246 §4 / ADR-0237 (amended non-goal): the named looks bundled with the
//app. UI/Dependencies/Shaders/Looks/looks.json lists every look and every file
//of it with its license, source and sha256; the folder is extracted with the
//other dependencies into <home>/Shaders/Looks. Host-free (ADR-0123): Parse and
//Entries feed Settings › Look, Validate is what UI.Tests runs on the real
//bytes. Adding a look is a code change - there is no catalog browser and no
//download path.
public sealed record NamedLookFile(string Path, string Sha256, string License, string Source);

public sealed record NamedLook(string Id, string Name, string Preset, IReadOnlyList<NamedLookFile> Files);

public static class NamedLookManifest
{
	public const string FileName = "looks.json";
	public const string FolderName = "Looks";
	public const int Format = 1;

	//Licenses a file may carry and still ship inside a GPL-3.0 app.
	public static readonly IReadOnlyList<string> Gpl3CompatibleLicenses = new[] {
		"GPL-3.0-or-later", "GPL-3.0-only", "GPL-2.0-or-later",
		"LGPL-2.1-or-later", "LGPL-3.0-or-later",
		"MIT", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "CC0-1.0", "Public-Domain"
	};

	//A source is either a GitHub blob URL pinned to a full commit sha, or a file
	//written in this repository (which says what upstream file it mirrors).
	private static readonly Regex PinnedGitHubBlob = new(@"^https://github\.com/[^/]+/[^/]+/blob/[0-9a-f]{40}/\S+$");
	private const string AuthoredHere = "authored in this repository";

	public static IReadOnlyList<NamedLook> Parse(string json)
	{
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if(!root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != Format) {
				return Array.Empty<NamedLook>();
			}
			List<NamedLook> looks = new();
			foreach(JsonElement look in root.GetProperty("looks").EnumerateArray()) {
				List<NamedLookFile> files = new();
				foreach(JsonElement file in look.GetProperty("files").EnumerateArray()) {
					files.Add(new NamedLookFile(Str(file, "path"), Str(file, "sha256"), Str(file, "license"), Str(file, "source")));
				}
				looks.Add(new NamedLook(Str(look, "id"), Str(look, "name"), Str(look, "preset"), files));
			}
			return looks;
		} catch(Exception ex) when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) {
			return Array.Empty<NamedLook>();
		}
	}

	//The looks as Settings › Look offers them, with each preset's absolute path
	//under the extracted folder.
	public static IReadOnlyList<NamedLookEntry> Entries(IReadOnlyList<NamedLook> looks, string looksFolder)
	{
		return looks.Select(l => new NamedLookEntry(l.Id, l.Name, System.IO.Path.Combine(looksFolder, l.Preset.Replace('/', System.IO.Path.DirectorySeparatorChar)))).ToList();
	}

	//Problems with the manifest against the folder's bytes; empty when it can
	//ship. readFile takes a manifest-relative path ('/'-separated) and returns
	//null for a missing file; filesOnDisk lists every file in the folder the
	//same way.
	public static IReadOnlyList<string> Validate(IReadOnlyList<NamedLook> looks, Func<string, byte[]?> readFile, IEnumerable<string> filesOnDisk)
	{
		List<string> problems = new();
		if(looks.Count < 2 || looks.Count > 3) {
			problems.Add($"the bundled list is two or three looks (ADR-0246 §4), found {looks.Count}");
		}
		HashSet<string> listed = new(StringComparer.Ordinal) { FileName };
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach(NamedLook look in looks) {
			if(look.Id == "" || !ids.Add(look.Id)) {
				problems.Add($"look id '{look.Id}' is empty or repeated");
			}
			if(look.Name == "") {
				problems.Add($"look '{look.Id}' has no name");
			}
			if(!look.Files.Any(f => f.Path == look.Preset) || !look.Preset.EndsWith(".slangp", StringComparison.Ordinal)) {
				problems.Add($"look '{look.Id}': preset '{look.Preset}' is not one of its listed .slangp files");
			}
			foreach(NamedLookFile file in look.Files) {
				listed.Add(file.Path);
				byte[]? bytes = readFile(file.Path);
				if(bytes == null) {
					problems.Add($"{file.Path}: listed but missing");
				} else if(!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.Sha256, StringComparison.OrdinalIgnoreCase)) {
					problems.Add($"{file.Path}: sha256 does not match the manifest");
				}
				if(!Gpl3CompatibleLicenses.Contains(file.License)) {
					problems.Add($"{file.Path}: license '{file.License}' is not a GPL-3.0-compatible license");
				}
				if(!PinnedGitHubBlob.IsMatch(file.Source) && !file.Source.StartsWith(AuthoredHere, StringComparison.Ordinal)) {
					problems.Add($"{file.Path}: source '{file.Source}' is not pinned to a commit");
				}
			}
		}
		foreach(string onDisk in filesOnDisk) {
			if(!listed.Contains(onDisk)) {
				problems.Add($"{onDisk}: in the looks folder but not in {FileName}");
			}
		}
		return problems;
	}

	private static string Str(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
	}
}
