using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Look
{
	//ADR-0246 §4 / ADR-0237 (amended non-goal): two or three named looks are
	//bundled, each file with a GPL-3.0-compatible license, its source and its
	//sha256 recorded in UI/Dependencies/Shaders/Looks/looks.json. These check
	//the real manifest against the real bytes, and the validator itself.
	public class NamedLookManifestTests
	{
		private static string LooksFolder()
		{
			DirectoryInfo? dir = new(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			if(dir == null) {
				throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
			}
			return Path.Combine(dir.FullName, "UI", "Dependencies", "Shaders", "Looks");
		}

		private static IReadOnlyList<string> ValidateFolder(string folder, IReadOnlyList<NamedLook> looks)
		{
			IEnumerable<string> onDisk = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
				.Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'));
			return NamedLookManifest.Validate(looks, rel => {
				string path = Path.Combine(folder, rel);
				return File.Exists(path) ? File.ReadAllBytes(path) : null;
			}, onDisk);
		}

		[Fact]
		public void The_bundled_manifest_matches_the_bundled_bytes()
		{
			string folder = LooksFolder();
			IReadOnlyList<NamedLook> looks = NamedLookManifest.Parse(File.ReadAllText(Path.Combine(folder, NamedLookManifest.FileName)));
			Assert.Empty(ValidateFolder(folder, looks));
			Assert.Equal(new[] { "CRT TV", "Handheld LCD" }, looks.Select(l => l.Name));
		}

		[Fact]
		public void Entries_resolve_the_preset_under_the_looks_folder()
		{
			IReadOnlyList<NamedLook> looks = NamedLookManifest.Parse(File.ReadAllText(Path.Combine(LooksFolder(), NamedLookManifest.FileName)));
			IReadOnlyList<NamedLookEntry> entries = NamedLookManifest.Entries(looks, "/home/Shaders/Looks");
			Assert.Equal(Path.Combine("/home/Shaders/Looks", "crt", "crt-geom.slangp"), entries[0].PresetPath);
			Assert.Equal("crt-tv", entries[0].Id);
		}

		private static string Manifest(string fileJson, string preset = "a.slangp", int looks = 2)
		{
			string one(int i) => "{\"id\":\"l" + i + "\",\"name\":\"L" + i + "\",\"preset\":\"" + preset + "\",\"files\":[" + fileJson + "]}";
			return "{\"format\":1,\"looks\":[" + string.Join(",", Enumerable.Range(0, looks).Select(one)) + "]}";
		}

		private static string File1(string sha, string license = "GPL-2.0-or-later", string source = "https://github.com/libretro/slang-shaders/blob/0b3ff0b240f82f4cb5e5d0117f8f2080e2764cf7/a.slangp")
		{
			return "{\"path\":\"a.slangp\",\"sha256\":\"" + sha + "\",\"license\":\"" + license + "\",\"source\":\"" + source + "\"}";
		}

		private static readonly byte[] Bytes = { 1, 2, 3 };
		private static string Sha => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();

		private static IReadOnlyList<string> Check(string json, IEnumerable<string>? onDisk = null)
		{
			return NamedLookManifest.Validate(NamedLookManifest.Parse(json), rel => rel == "a.slangp" ? Bytes : null, onDisk ?? new[] { "a.slangp", NamedLookManifest.FileName });
		}

		[Fact]
		public void A_clean_manifest_validates()
		{
			Assert.Empty(Check(Manifest(File1(Sha))));
		}

		[Fact]
		public void A_wrong_hash_is_refused()
		{
			Assert.Contains(Check(Manifest(File1(new string('0', 64)))), p => p.Contains("sha256"));
		}

		[Theory]
		[InlineData("GPL-2.0-only")]
		[InlineData("CC-BY-NC-4.0")]
		[InlineData("")]
		public void A_license_that_is_not_GPL_3_compatible_is_refused(string license)
		{
			Assert.Contains(Check(Manifest(File1(Sha, license: license))), p => p.Contains("license"));
		}

		[Theory]
		[InlineData("https://github.com/libretro/slang-shaders/blob/master/a.slangp")]
		[InlineData("https://example.com/a.slangp")]
		[InlineData("")]
		public void A_source_that_is_not_pinned_is_refused(string source)
		{
			Assert.Contains(Check(Manifest(File1(Sha, source: source))), p => p.Contains("source"));
		}

		[Fact]
		public void An_unlisted_file_in_the_folder_is_refused()
		{
			Assert.Contains(Check(Manifest(File1(Sha)), new[] { "a.slangp", "stray.slang", NamedLookManifest.FileName }), p => p.Contains("stray.slang"));
		}

		[Fact]
		public void A_preset_that_is_not_a_listed_file_is_refused()
		{
			Assert.Contains(Check(Manifest(File1(Sha), preset: "b.slangp")), p => p.Contains("preset"));
		}

		[Theory]
		[InlineData(1)]
		[InlineData(4)]
		public void The_list_is_two_or_three_looks(int count)
		{
			Assert.Contains(Check(Manifest(File1(Sha), looks: count)), p => p.Contains("two or three"));
		}

		[Fact]
		public void Malformed_json_reads_as_no_looks()
		{
			Assert.Empty(NamedLookManifest.Parse("{not json"));
			Assert.Empty(NamedLookManifest.Parse("{\"format\":2,\"looks\":[]}"));
		}
	}
}
