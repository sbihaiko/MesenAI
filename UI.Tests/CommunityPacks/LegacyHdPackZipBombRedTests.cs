using System;
using System.IO;
using System.IO.Compression;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//#860 honest RED, kept beside the fixed behaviour so a reviewer can compare them.
	//
	//PreFixReadEntryBytes below is a VERBATIM copy of the method #860 removed from
	//UI/Logic/LegacyHdPackInstall.cs:
	//    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
	//    {
	//        using Stream src = entry.Open();
	//        using MemoryStream ms = new MemoryStream();
	//        src.CopyTo(ms);
	//        return ms.ToArray();
	//    }
	//Pre_fix_read_materializes_the_whole_nested_archive drives that copy on an 8 MiB
	//wrapper bomb and pins what the old path really did: it held the entire archive
	//as a byte[] with no cap. The captured RED is this same drive with the assertion
	//inverted to
	//    Assert.True(materialized.Length < nested.Length,
	//        "the whole nested archive was materialized before any size gate: "...);
	//which failed (assertion, not a build error) with
	//    the whole nested archive was materialized before any size gate: 8388608 of 8388608 bytes
	//Fixed_path_refuses_the_bomb_before_it_is_materialized points the install itself
	//at the fixed overload and demands the refusal.
	public class LegacyHdPackZipBombRedTests
	{
		private const int EightMiB = 8 * 1024 * 1024;
		private const int OneMiB = 1 * 1024 * 1024;

		[Fact]
		public void Pre_fix_read_materializes_the_whole_nested_archive()
		{
			byte[] wrapperBytes = BuildWrapperZipWithNestedPayload(EightMiB);
			using MemoryStream wrapperMs = new(wrapperBytes);
			using ZipArchive wrapper = new(wrapperMs, ZipArchiveMode.Read);
			ZipArchiveEntry nested = wrapper.GetEntry("Pack.zip")!;

			byte[] materialized = PreFixReadEntryBytes(nested);

			//The old path buffered every byte of the (attacker-sized) nested archive.
			//Invert this assertion to reproduce the captured RED.
			Assert.Equal(nested.Length, materialized.Length);
		}

		[Fact]
		public void Fixed_path_refuses_the_bomb_before_it_is_materialized()
		{
			byte[] wrapperBytes = BuildWrapperZipWithNestedPayload(EightMiB);
			using MemoryStream wrapperMs = new(wrapperBytes);
			using ZipArchive wrapper = new(wrapperMs, ZipArchiveMode.Read);

			string dest = NewTempDir();
			try {
				bool ok = LegacyHdPackInstall.ExtractToFolder(wrapper, dest, "Some Rom", OneMiB, out string error);
				Assert.False(ok, "a nested bomb over the cap must be refused");
				Assert.Contains("refusing", error, StringComparison.Ordinal);
				Assert.Empty(Directory.GetFileSystemEntries(dest));
			} finally {
				Directory.Delete(dest, true);
			}
		}

		//Verbatim copy of the removed pre-fix read (#860).
		private static byte[] PreFixReadEntryBytes(ZipArchiveEntry entry)
		{
			using Stream src = entry.Open();
			using MemoryStream ms = new MemoryStream();
			src.CopyTo(ms);
			return ms.ToArray();
		}

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

		private static void CreateEntry(ZipArchive zip, string name, string content)
		{
			ZipArchiveEntry entry = zip.CreateEntry(name);
			using Stream stream = entry.Open();
			using StreamWriter writer = new(stream);
			writer.Write(content);
		}

		private static string NewTempDir()
		{
			string dest = Path.Combine(Path.GetTempPath(), "mesen-legacy-hd-red-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dest);
			return dest;
		}
	}
}
