using System;
using System.IO;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Mep
{
	//ADR-0147: which folder under a container root holds the pack - <root>/mep
	//when that is the human layer, the container root otherwise. The rule mirrors
	//MepPackManager::HasSiblingMepPack.
	public sealed class MepPackLayerTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "mpl-" + Guid.NewGuid().ToString("N"));

		public MepPackLayerTests() { Directory.CreateDirectory(_root); }
		public void Dispose() { try { Directory.Delete(_root, true); } catch(IOException) { } }

		private void Write(string rel)
		{
			string path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, "{}");
		}

		[Fact]
		public void Pack_json_makes_mep_the_pack_folder()
		{
			Write("mep/pack.json");
			Assert.True(MepPackLayer.IsMepLayer(_root));
			Assert.Equal(Path.Combine(_root, "mep"), MepPackLayer.Resolve(_root));
		}

		[Theory]
		[InlineData("mep/textures/hires.txt")]
		[InlineData("mep/audio/fingerprints.json")]
		[InlineData("mep/synth/preset.cfg")]
		[InlineData("mep/border/border.png")]
		[InlineData("mep/hires.txt")]
		[InlineData("mep/border.png")]
		public void A_convention_probe_makes_mep_the_pack_folder(string probe)
		{
			Write(probe);
			Assert.Equal(Path.Combine(_root, "mep"), MepPackLayer.Resolve(_root));
		}

		//A mep/ folder alone is not the human layer - only with content the core
		//would read.
		[Fact]
		public void An_empty_mep_folder_keeps_the_container_root()
		{
			Directory.CreateDirectory(Path.Combine(_root, "mep"));
			Assert.False(MepPackLayer.IsMepLayer(_root));
			Assert.Equal(_root, MepPackLayer.Resolve(_root));
		}

		//The legacy sibling layout and a central EnhancementPacks/<container>.
		[Fact]
		public void No_mep_folder_keeps_the_container_root()
		{
			Write("textures/hires.txt");
			Assert.Equal(_root, MepPackLayer.Resolve(_root));
		}

		[Fact]
		public void No_container_root_resolves_to_empty()
		{
			Assert.Equal("", MepPackLayer.Resolve(""));
			Assert.Equal("", MepPackLayer.Resolve(null));
		}

		//A container root that does not exist (yet) is returned as it is: the
		//caller decides what to do with a missing folder.
		[Fact]
		public void A_missing_container_root_is_returned_unchanged()
		{
			string absent = Path.Combine(_root, "absent");
			Assert.Equal(absent, MepPackLayer.Resolve(absent));
		}
	}
}
