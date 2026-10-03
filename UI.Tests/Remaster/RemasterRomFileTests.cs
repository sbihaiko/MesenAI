using System;
using System.Collections.Generic;
using System.IO;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//#689: the Remaster jobs (kit, build, import) read --rom as an iNES file.
	//A game opened from a .zip/.7z is the archive's inner file, so the jobs get
	//that file written out beside the temp folder, never the archive.
	public sealed class RemasterRomFileTests : IDisposable
	{
		private readonly string _temp = Path.Combine(Path.GetTempPath(), "mesen-romfile-" + Guid.NewGuid().ToString("N"));

		public RemasterRomFileTests() => Directory.CreateDirectory(_temp);

		public void Dispose()
		{
			try {
				Directory.Delete(_temp, true);
			} catch(IOException) {
			}
		}

		[Fact]
		public void A_plain_rom_file_is_given_as_it_is()
		{
			List<string> extracted = new();
			string path = RemasterRomFile.ForJobs("/roms/Contra (USA).nes", "", "/roms/Contra (USA).nes", _temp, (from, to) => { extracted.Add(from); return true; });

			Assert.Equal("/roms/Contra (USA).nes", path);
			Assert.Empty(extracted);
		}

		[Fact]
		public void An_archive_gives_its_inner_rom_written_out_under_the_temp_folder()
		{
			string resource = "/roms/Contra (USA).zip\x1" + "dump/Contra (USA).nes";
			List<string> from = new();
			string path = RemasterRomFile.ForJobs("/roms/Contra (USA).zip", "dump/Contra (USA).nes", resource, _temp, (res, to) => {
				from.Add(res);
				File.WriteAllBytes(to, new byte[] { (byte)'N', (byte)'E', (byte)'S', 0x1A });
				return true;
			});

			Assert.Equal(new[] { resource }, from);
			Assert.Equal("Contra (USA).nes", Path.GetFileName(path));
			Assert.StartsWith(Path.Combine(_temp, RemasterRomFile.FolderName), path);
			Assert.Equal(new byte[] { (byte)'N', (byte)'E', (byte)'S', 0x1A }, File.ReadAllBytes(path));
			//No partial file is left beside it.
			Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
		}

		[Fact]
		public void Two_archives_with_the_same_inner_name_do_not_share_a_file()
		{
			string a = RemasterRomFile.ForJobs("/a/game.zip", "game.nes", "/a/game.zip\x1game.nes", _temp, (_, to) => { File.WriteAllText(to, "a"); return true; });
			string b = RemasterRomFile.ForJobs("/b/game.7z", "game.nes", "/b/game.7z\x1game.nes", _temp, (_, to) => { File.WriteAllText(to, "b"); return true; });

			Assert.NotEqual(a, b);
			Assert.Equal("a", File.ReadAllText(a));
			Assert.Equal("b", File.ReadAllText(b));
		}

		//A later open of the same archive rewrites the file (the archive may
		//have changed); a job that already opened the old one keeps reading it.
		[Fact]
		public void Opening_the_same_archive_again_replaces_the_file()
		{
			string first = RemasterRomFile.ForJobs("/a/game.zip", "game.nes", "/a/game.zip\x1game.nes", _temp, (_, to) => { File.WriteAllText(to, "old"); return true; });
			string second = RemasterRomFile.ForJobs("/a/game.zip", "game.nes", "/a/game.zip\x1game.nes", _temp, (_, to) => { File.WriteAllText(to, "new"); return true; });

			Assert.Equal(first, second);
			Assert.Equal("new", File.ReadAllText(second));
		}

		//The core could not read the archive after all: the jobs get the
		//archive, as before, and fail on it with their own message.
		[Fact]
		public void An_extraction_that_fails_gives_the_archive()
		{
			string path = RemasterRomFile.ForJobs("/a/game.zip", "game.nes", "/a/game.zip\x1game.nes", _temp, (_, _) => false);

			Assert.Equal("/a/game.zip", path);
		}
	}
}
