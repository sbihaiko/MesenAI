using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Utilities
{
	//The rule NativeDependencyExtractor exists to hold: an extracted native
	//library is never rewritten in place. The app ships its core inside
	//Dependencies.zip and unpacks it into the per-user home folder on every
	//launch, so two installs that share that folder - a development build and the
	//installed one, which carry different cores - each overwrite the other's copy.
	//By then the other instance has the library mapped, and rewriting the bytes
	//under a mapped Mach-O kills it: measured 2026-10-05 as a SIGKILL with
	//termination namespace CODESIGNING, indicator "Invalid Page", faulting thread
	//in dyld dlopen_from.
	//
	//These tests run the real extractor against a real archive in a temp folder,
	//which is why the walk lives in UI/Logic rather than in UI/Utilities (ADR-0123,
	//see UI.Tests/AGENTS.md).
	public class NativeDependencyExtractorTests
	{
		//Both archives stamp their members the same, so the only thing that
		//differs between them is the content - which is what the extractor is
		//supposed to notice.
		private static readonly DateTimeOffset Stamp = new(2026, 1, 2, 3, 4, 6, TimeSpan.Zero);

		private static string NewTempDir()
		{
			string dir = Path.Combine(Path.GetTempPath(), "mesen-nativedep-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static MemoryStream Zip(params (string Name, string Content)[] members)
		{
			MemoryStream stream = new();
			using(ZipArchive zip = new(stream, ZipArchiveMode.Create, true)) {
				foreach((string name, string content) in members) {
					ZipArchiveEntry entry = zip.CreateEntry(name);
					entry.LastWriteTime = Stamp;
					using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
					writer.Write(content);
				}
			}
			stream.Position = 0;
			return stream;
		}

		//The property the whole file is about, and it takes both assertions to say
		//it: the path must name the new library, and the instance that already
		//holds the old one must still be reading its own bytes. Together those are
		//"the replacement landed a different file" - which is what protects a
		//process that has the library mapped.
		//
		//Measured before the fix, and the measurement is why both assertions are
		//here: the path still read "OLD". .NET opens the destination of an in-place
		//extraction with FileShare.None, so the write failed, and the per-member
		//catch swallowed it - a stale core and nothing said. A fix that only
		//widened the share mode would flip the first assertion and fail the second,
		//because the held reader is on the same inode.
		[Fact]
		public void Replacing_a_library_leaves_the_instance_that_holds_it_on_its_own_bytes()
		{
			string dest = NewTempDir();
			try {
				NativeDependencyExtractor.Extract(Zip(("MesenCore.dylib", "OLD")), dest);
				string path = Path.Combine(dest, "MesenCore.dylib");

				//The instance that is already running has this library open, which
				//is what a mapped image is from the filesystem's side.
				using FileStream held = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

				//A second install extracts its own core into the same home folder.
				NativeDependencyExtractor.Extract(Zip(("MesenCore.dylib", "NEW-AND-LONGER")), dest);

				Assert.Equal("NEW-AND-LONGER", File.ReadAllText(path));

				using StreamReader stillOpen = new(held);
				Assert.Equal("OLD", stillOpen.ReadToEnd());
			} finally {
				Directory.Delete(dest, true);
			}
		}

		//Whatever a replacement writes through must not survive it: a home folder
		//that accumulates one leftover per launch is its own bug, and the core is
		//found by scanning beside the executable.
		[Fact]
		public void An_extraction_leaves_nothing_but_the_member_behind()
		{
			string dest = NewTempDir();
			try {
				NativeDependencyExtractor.Extract(Zip(("MesenCore.dylib", "OLD")), dest);
				NativeDependencyExtractor.Extract(Zip(("MesenCore.dylib", "NEW-AND-LONGER")), dest);

				Assert.Equal(
					new[] { "MesenCore.dylib" },
					Directory.GetFiles(dest).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
			} finally {
				Directory.Delete(dest, true);
			}
		}

		//A member whose directory does not exist yet is created (the Satellaview
		//data in the real archive lives in a nested folder), and a member under
		//"Internal" is skipped entirely - it is not shipped.
		[Fact]
		public void A_nested_member_is_written_and_an_internal_one_is_not()
		{
			string dest = NewTempDir();
			try {
				NativeDependencyExtractor.Extract(Zip(
					("Satellaview/bsx.bin", "DATA"),
					("Internal/private.txt", "SECRET")), dest);

				Assert.Equal("DATA", File.ReadAllText(Path.Combine(dest, "Satellaview", "bsx.bin")));
				Assert.False(File.Exists(Path.Combine(dest, "Internal", "private.txt")));
			} finally {
				Directory.Delete(dest, true);
			}
		}

		//#892: a build done from Visual Studio does not embed the core, so the debug
		//branch copies it out of the bin folder into the home folder - the folder
		//Program.DllImportResolver loads from, by exact name
		//(Path.Combine(ConfigManager.HomeFolder, "MesenCore.dylib") on macOS, and the
		//same shape elsewhere). The names it looks for must therefore be the names
		//that resolver loads; a stem glued to an extension without its dot looks
		//like a file name and is not one, and the whole fallback silently does
		//nothing.
		[Fact]
		public void The_debug_copy_looks_for_the_names_the_resolver_loads()
		{
			Assert.Contains("MesenCore.dll", NativeDependencyExtractor.DebugCoreFileNames);
			Assert.Contains("MesenCore.so", NativeDependencyExtractor.DebugCoreFileNames);
			Assert.Contains("MesenCore.dylib", NativeDependencyExtractor.DebugCoreFileNames);

			//Every entry is the core's name plus a real extension.
			Assert.All(NativeDependencyExtractor.DebugCoreFileNames,
				name => Assert.Equal("MesenCore", Path.GetFileNameWithoutExtension(name)));
		}

		//An extracted .bin that is already on disk is kept: it is user data the
		//Satellaview writes, not something to re-extract over.
		[Fact]
		public void A_bin_that_is_already_on_disk_is_left_alone()
		{
			string dest = NewTempDir();
			try {
				Directory.CreateDirectory(Path.Combine(dest, "Satellaview"));
				File.WriteAllText(Path.Combine(dest, "Satellaview", "bsx.bin"), "USER");

				NativeDependencyExtractor.Extract(Zip(("Satellaview/bsx.bin", "SHIPPED")), dest);

				Assert.Equal("USER", File.ReadAllText(Path.Combine(dest, "Satellaview", "bsx.bin")));
			} finally {
				Directory.Delete(dest, true);
			}
		}
	}
}
