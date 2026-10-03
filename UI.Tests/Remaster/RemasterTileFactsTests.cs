using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.7 (W-R1 zone ②): the strip's chips, a tile's count and source, what a
	//click opens, and the paint cache that notices a save.
	public sealed class RemasterTileFactsTests
	{
		private static readonly byte[] Red = { 255, 0, 0, 255 };
		private static readonly byte[] Blue = { 0, 0, 255, 255 };

		[Fact]
		public void The_strip_shows_only_categories_with_tiles_and_keeps_the_users_pick()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKit kit = RemasterKitReader.Read(f.Project);
			Assert.Equal(new[] { RemasterKitCategory.Figures, RemasterKitCategory.Scenery, RemasterKitCategory.PatternPages }, RemasterTileFacts.Shown(kit).ToArray());
			Assert.Equal(RemasterKitCategory.Figures, RemasterTileFacts.Pick(kit, null));
			Assert.Equal(RemasterKitCategory.PatternPages, RemasterTileFacts.Pick(kit, RemasterKitCategory.PatternPages));
			Assert.Equal(RemasterKitCategory.Figures, RemasterTileFacts.Pick(kit, RemasterKitCategory.StageMaps));
			Assert.Null(RemasterTileFacts.Pick(RemasterKit.Empty, RemasterKitCategory.Figures));
		}

		[Fact]
		public void A_figure_counts_phases_opens_its_composed_view_and_names_its_recording()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKit kit = RemasterKitReader.Read(f.Project);
			RemasterKitTile run = kit.Tiles[0];
			Assert.Equal((RemasterTileCountKind.Phases, 6), RemasterTileFacts.CountOf(run));
			Assert.Equal(run.FigurePath, RemasterTileFacts.OpenPath(run));
			Assert.Equal(RemasterTileSource.Recording, RemasterTileFacts.SourceOf(run));

			RemasterKitTile pose = kit.Tiles[1];
			Assert.Equal((RemasterTileCountKind.Cells, 1), RemasterTileFacts.CountOf(pose));
			Assert.Equal(pose.ImagePath, RemasterTileFacts.OpenPath(pose));

			RemasterKitTile page = kit.Tiles.First(t => t.Category == RemasterKitCategory.PatternPages);
			Assert.Equal(RemasterTileSource.EveryRecording, RemasterTileFacts.SourceOf(page));
			Assert.True(RemasterTileFacts.Warns(RemasterProvenance.Lines(page, RemasterPaintResult.Unknown(RemasterPaintUnknown.NoPrePaintCopy))));
			Assert.False(RemasterTileFacts.Warns(RemasterProvenance.Lines(run, RemasterPaintResult.Untouched)));
		}

		[Fact]
		public void The_cache_answers_from_memory_until_the_artist_saves_the_picture()
		{
			using KitFixture f = new();
			f.WriteStandardKit();
			RemasterKitTile obj = RemasterKitReader.Read(f.Project).Tiles.First(t => t.Unit == "object");
			RemasterPaintCache cache = new();
			int compares = 0;
			RemasterPaintResult Count(string a, string b)
			{
				compares++;
				return RemasterPaintProbe.Compare(a, b);
			}

			Assert.False(cache.TryGet(obj, out _));
			Assert.Equal(RemasterPaintState.Untouched, cache.Get(obj, Count).State);
			Assert.Equal(RemasterPaintState.Untouched, cache.Get(obj, Count).State);
			Assert.Equal(1, compares);

			//A save changes the size (and mtime): compared again, now painted.
			f.Paint("kit/rec-001/sheets/usr005.png", 8, 8, Red, Blue);
			File.SetLastWriteTimeUtc(obj.ImagePath, System.DateTime.UtcNow.AddMinutes(1));
			Assert.Equal(RemasterPaintState.Painted, cache.Get(obj, Count).State);
			Assert.Equal(2, compares);
		}
	}
}
