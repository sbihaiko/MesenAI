using System;
using System.IO;
using System.IO.Compression;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//#860: a wrapper HD pack whose single root-level nested zip inflates past the
	//extraction ceiling must be refused *while that entry is being read*. The old
	//code did ReadEntryBytes(nestedEntry) into a byte[] before any size check, so
	//a decompression bomb was fully materialized - and the only limit,
	//MaxExtractedBytes, then fired later, in WriteUnderRoot, once the bytes were
	//already in memory. The gate now runs on the nested entry's declared size and,
	//because a declared size is attacker-controlled, a SizeCappedStream refuses to
	//hand out more than the cap even when the header lies.
	public class LegacyHdPackZipBombTests
	{
		private const int EightMiB = 8 * 1024 * 1024;
		private const int OneMiB = 1 * 1024 * 1024;

		// --- the capped read the fix relies on -------------------------------

		[Fact]
		public void SizeCappedStream_refuses_a_nested_entry_larger_than_the_cap_while_reading()
		{
			//A wrapper with one root-level nested zip holding 8 MiB - over the tiny
			//cap injected here, but small enough not to stress the machine.
			byte[] wrapperBytes = BuildWrapperZipWithNestedPayload(EightMiB);

			using MemoryStream wrapperMs = new(wrapperBytes);
			using ZipArchive wrapper = new(wrapperMs, ZipArchiveMode.Read);
			ZipArchiveEntry nestedEntry = wrapper.GetEntry("Pack.zip")!;
			using Stream src = nestedEntry.Open();
			using SizeCappedStream capped = new(src, OneMiB);

			Assert.Throws<InvalidDataException>(() => { capped.CopyTo(Stream.Null); });
			//Refused DURING the read, not after: the entry was never drained.
			Assert.True(capped.BytesRead < nestedEntry.Length,
				"the capped stream consumed the whole entry before refusing");
		}

		// --- the install path over a real wrapper zip ------------------------

		[Fact]
		public void ExtractToFolder_nested_wrapper_over_the_cap_is_refused_without_writing_anything()
		{
			byte[] wrapperBytes = BuildWrapperZipWithNestedPayload(EightMiB);

			using MemoryStream wrapperMs = new(wrapperBytes);
			using ZipArchive wrapper = new(wrapperMs, ZipArchiveMode.Read);
			string dest = NewTempDir();
			try {
				bool ok = LegacyHdPackInstall.ExtractToFolder(wrapper, dest, "Some Rom", OneMiB, out string error);
				Assert.False(ok);
				Assert.Contains("refusing", error, StringComparison.Ordinal);
				Assert.Empty(Directory.GetFileSystemEntries(dest));
			} finally {
				Directory.Delete(dest, true);
			}
		}

		[Fact]
		public void ExtractToFolder_nested_wrapper_under_the_cap_still_extracts()
		{
			//The gate must not be over-eager: a wrapper whose nested zip fits the
			//cap still unwraps and writes the pack root.
			byte[] wrapperBytes = BuildWrapperZipWithNestedPack();

			using MemoryStream wrapperMs = new(wrapperBytes);
			using ZipArchive wrapper = new(wrapperMs, ZipArchiveMode.Read);
			string dest = NewTempDir();
			try {
				Assert.True(
					LegacyHdPackInstall.ExtractToFolder(wrapper, dest, "Legend of Zelda, The (USA)", 4 * OneMiB, out string error),
					error);
				Assert.True(File.Exists(Path.Combine(dest, "hires.txt")));
				Assert.True(File.Exists(Path.Combine(dest, "Link.png")));
				Assert.False(File.Exists(Path.Combine(dest, "readme.txt")));
			} finally {
				Directory.Delete(dest, true);
			}
		}

		// --- helpers ---------------------------------------------------------

		private static byte[] BuildWrapperZipWithNestedPayload(int payloadBytes)
		{
			byte[] payload = new byte[payloadBytes];
			using MemoryStream ms = new();
			using(ZipArchive zip = new(ms, ZipArchiveMode.Create, true)) {
				ZipArchiveEntry nested = zip.CreateEntry("Pack.zip");
				using(Stream stream = nested.Open()) {
					stream.Write(payload, 0, payload.Length);
				}
				CreateEntry(zip, "readme.txt", "wrapper");
			}
			return ms.ToArray();
		}

		private static byte[] BuildWrapperZipWithNestedPack()
		{
			byte[] innerBytes;
			using(MemoryStream innerMs = new()) {
				using(ZipArchive inner = new(innerMs, ZipArchiveMode.Create, true)) {
					CreateEntry(inner, "hires.txt", "<scale>4</scale>");
					CreateEntry(inner, "Link.png", "png");
				}
				innerBytes = innerMs.ToArray();
			}

			using MemoryStream ms = new();
			using(ZipArchive zip = new(ms, ZipArchiveMode.Create, true)) {
				ZipArchiveEntry nested = zip.CreateEntry("Pack.zip");
				using(Stream stream = nested.Open()) {
					stream.Write(innerBytes, 0, innerBytes.Length);
				}
				CreateEntry(zip, "readme.txt", "wrapper");
			}
			return ms.ToArray();
		}

		private static void CreateEntry(ZipArchive zip, string name, string content)
		{
			ZipArchiveEntry entry = zip.CreateEntry(name);
			using Stream stream = entry.Open();
			using StreamWriter writer = new(stream);
			writer.Write(content);
		}

		private static string NewTempDir()
		{
			string dest = Path.Combine(Path.GetTempPath(), "mesen-legacy-hd-bomb-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dest);
			return dest;
		}
	}
}
