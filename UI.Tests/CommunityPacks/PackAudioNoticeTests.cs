using Mesen.Logic;
using System;
using System.IO;
using Xunit;

namespace Mesen.Tests
{
	//ADR-0240 Option 1 / F6.9: a wired bundled patch whose <bgm>/<sfx> refs do
	//not resolve in the installed pack yields ONE notice; everything else none.
	//M counts distinct referenced files (not <bgm>/<sfx> lines).
	public sealed class PackAudioNoticeTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "pan-" + Guid.NewGuid().ToString("N"));

		public PackAudioNoticeTests() { Directory.CreateDirectory(_root); }
		public void Dispose() { try { Directory.Delete(_root, true); } catch(IOException) { } }

		private void Write(string rel, string text = "x")
		{
			string path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, text);
		}

		private const string Sha = "0123456789ABCDEF0123456789ABCDEF01234567";

		[Fact]
		public void WiredHiresPatch_UnresolvedBgm_NoticeWithCounts()
		{
			Write("textures/hires.txt", "<patch>Music.ips," + Sha + "\n<bgm>1,1,BGM/a.ogg\n<bgm>1,2,BGM/b.ogg\n<bgm>1,3,BGM/c.ogg\n");
			Write("textures/Music.ips");
			Write("textures/BGM/c.ogg");
			Write("textures/tile.png");
			Assert.Equal("audio not generated: 2 of 3 tracks unresolved; supply the `.ogg` files", PackAudioNotice.Evaluate(_root));
			Assert.True(File.Exists(Path.Combine(_root, "textures", "tile.png")));
		}

		[Fact]
		public void WiredPatch_AllRefsResolve_NoNotice()
		{
			Write("textures/hires.txt", "<patch>Music.ips," + Sha + "\n<bgm>1,1,BGM/a.ogg\n<sfx>2,1,SFX/s.ogg\n");
			Write("textures/Music.ips");
			Write("textures/BGM/a.ogg");
			Write("textures/SFX/s.ogg");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void PatchPresentButNotWired_NoNotice()
		{
			Write("textures/hires.txt", "<bgm>1,1,BGM/a.ogg\n");
			Write("textures/Music.ips");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void NoPatch_UnresolvedRefs_NoNotice()
		{
			Write("textures/hires.txt", "<bgm>1,1,BGM/a.ogg\n");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void CrossFolderSameBasename_IsNotWired_NoNotice()
		{
			//textures/hires.txt names a.ips (resolves to textures/a.ips, absent);
			//the unreferenced audio/a.ips must not count as wired.
			Write("textures/hires.txt", "<patch>a.ips," + Sha + "\n");
			Write("audio/hires.txt", "<bgm>1,1,t.ogg\n");
			Write("audio/a.ips");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void HiresPatchWiresOnlyItsOwnFolderPath()
		{
			Write("textures/hires.txt", "<patch>a.ips," + Sha + "\n<bgm>1,1,t.ogg\n");
			Write("textures/a.ips");
			Write("audio/a.ips");
			Assert.Equal("audio not generated: 1 of 1 tracks unresolved; supply the `.ogg` files", PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void PackJsonPatch_WiresExactPathOnly()
		{
			Write("pack.json", "{\"patches\":[{\"file\":\"patches/Rev.bps\"}]}");
			Write("textures/hires.txt", "<bgm>1,1,t.ogg\n");
			Write("other/Rev.bps");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void WiredPatchReferencedButFileAbsent_NoNotice()
		{
			Write("textures/hires.txt", "<patch>Music.ips," + Sha + "\n<bgm>1,1,BGM/a.ogg\n");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void NoAudioSection_NoNotice()
		{
			Write("textures/hires.txt", "<patch>Music.ips," + Sha + "\n<tile>0,aa,bb,cc\n");
			Write("textures/Music.ips");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void WiredViaPackJsonPatches_Notice()
		{
			Write("pack.json", "{\"patches\":[{\"file\":\"patches/Rev.bps\",\"sha1\":\"" + Sha + "\"}]}");
			Write("patches/Rev.bps");
			Write("textures/hires.txt", "<bgm>1,1,BGM/a.ogg\n");
			Assert.Equal("audio not generated: 1 of 1 tracks unresolved; supply the `.ogg` files", PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void DuplicateRefs_CountedOncePerDistinctFile_AndSfxCounted()
		{
			Write("textures/hires.txt",
				"<patch>M.ips," + Sha + "\n<bgm>1,1,Music/town.ogg\n<bgm>1,2,Music/town.ogg\n<bgm>2,1,Music/over.ogg\n<sfx>3,1,SFX/hit.ogg\n<sfx>3,2,SFX/hit.ogg\n");
			Write("textures/M.ips");
			Write("textures/Music/over.ogg");
			Assert.Equal("audio not generated: 2 of 3 tracks unresolved; supply the `.ogg` files", PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void RefWithCommaAndLoopPosition_ParsedLikeTheLoader()
		{
			Write("textures/hires.txt", "<patch>M.ips," + Sha + "\n<bgm>1,1,Boss 1, Mid Boss A.ogg,4410\n");
			Write("textures/M.ips");
			Write("textures/Boss 1, Mid Boss A.ogg");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}

		[Fact]
		public void RefResolvesCaseInsensitively_LikeTheLoader()
		{
			Write("textures/hires.txt", "<patch>M.ips," + Sha + "\n<bgm>1,1,bgm/A.OGG\n");
			Write("textures/M.ips");
			Write("textures/BGM/a.ogg");
			Assert.Null(PackAudioNotice.Evaluate(_root));
		}
	}
}
