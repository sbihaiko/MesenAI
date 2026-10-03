using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.7 (PRD Part B §13.5.3 W-R1 zone ②, W-R5): the tile browser. The kit is
	//read by RemasterKitReader, the popover's facts by RemasterProvenance and
	//RemasterTileFacts (UI/Logic); this maps them to strings. The twin
	//comparison runs off the UI thread and is cached per file stamp, so a
	//Refresh never decodes a PNG it already compared.
	public partial class RemasterWorkspaceViewModel
	{
		private readonly RemasterPaintCache _paintCache = new();
		private RemasterKit _kit = RemasterKit.Empty;
		private RemasterKitCategory? _category;
		private int _tilesGeneration;

		[ObservableProperty] public partial List<RemasterCategoryChip> TileCategories { get; private set; } = new();
		[ObservableProperty] public partial List<RemasterTileRow> Tiles { get; private set; } = new();
		[ObservableProperty] public partial bool HasTiles { get; private set; }
		[ObservableProperty] public partial bool IsPatchedProject { get; private set; }
		[ObservableProperty] public partial string KitProblemText { get; private set; } = "";

		//Opens a tile's PNG with the OS default (RemasterFileOpener); a test
		//swaps it to see the path.
		public Func<string, bool> OpenFile { get; set; } = RemasterFileOpener.Open;

		//Completes when the twin comparisons the last refresh asked for are in.
		public Task TilesSettled { get; private set; } = Task.CompletedTask;

		public RemasterKitCategory? SelectedCategory => _category;

		private void RefreshTiles()
		{
			string folder = _project?.Folder ?? "";
			_kit = folder.Length > 0 ? RemasterKitReader.Read(folder) : RemasterKit.Empty;
			IsPatchedProject = folder.Length > 0 && RemasterProvenance.PaintsPatchedGame(folder);
			KitProblemText = string.Join(" · ", _kit.Problems);
			_category = RemasterTileFacts.Pick(_kit, _category);
			HasTiles = _category != null;
			if(HasTiles) {
				PaintText = ResourceHelper.GetMessage("RemasterKitReady");
			}
			BuildTileRows();
		}

		public void SelectCategory(RemasterKitCategory category)
		{
			if(_kit.Count(category) == 0) {
				return;
			}
			_category = category;
			BuildTileRows();
		}

		public bool OpenTile(RemasterTileRow row)
		{
			return OpenFile(row.OpenPath);
		}

		private void BuildTileRows()
		{
			int generation = ++_tilesGeneration;
			TileCategories = RemasterTileFacts.Shown(_kit).Select(c => new RemasterCategoryChip(c,
				ResourceHelper.GetMessage("RemasterCategory" + c, _kit.Count(c)), c == _category)).ToList();
			List<RemasterKitTile> shown = _kit.Tiles.Where(t => t.Category == _category).ToList();
			List<RemasterKitTile> pending = new();
			List<RemasterTileRow> rows = new(shown.Count);
			//A row whose tile and files are unchanged is kept, so a refresh does
			//not decode its thumbnail again or close its open popover.
			Dictionary<string, RemasterTileRow> previous = new(StringComparer.Ordinal);
			foreach(RemasterTileRow r in Tiles) {
				previous.TryAdd(r.Tile.ImagePath, r);
			}
			foreach(RemasterKitTile t in shown) {
				RemasterPaintResult? paint = null;
				if(_paintCache.TryGet(t, out RemasterPaintResult hit)) {
					paint = hit;
				} else {
					pending.Add(t);
				}
				string stamp = RemasterPaintCache.Stamp(t);
				rows.Add(previous.TryGetValue(t.ImagePath, out RemasterTileRow? old) && old.Tile == t && old.Stamp == stamp && Equals(old.Paint, paint)
					? old : RemasterTileRow.From(t, paint, stamp));
			}
			if(!rows.SequenceEqual(Tiles, ReferenceEqualityComparer.Instance)) {
				Tiles = rows;
			}
			if(pending.Count == 0) {
				TilesSettled = Task.CompletedTask;
				return;
			}
			TaskCompletionSource settled = new();
			TilesSettled = settled.Task;
			Task.Run(() => {
				foreach(RemasterKitTile t in pending) {
					_paintCache.Get(t, RemasterPaintProbe.Compare);
				}
			}).ContinueWith(_ => Dispatcher.UIThread.Post(() => {
				if(generation == _tilesGeneration) {
					BuildTileRows();
				}
				settled.TrySetResult();
			}), TaskScheduler.Default);
		}
	}

	public sealed record RemasterCategoryChip(RemasterKitCategory Category, string Text, bool IsSelected);

	//One tile of zone ② and its W-R5 popover.
	public sealed class RemasterTileRow
	{
		private Bitmap? _thumbnail;
		private bool _thumbnailRead;

		public required RemasterKitTile Tile { get; init; }
		public required string Caption { get; init; }
		public required string CountText { get; init; }
		//✎ painted, ⚠ not all of it was seen in play.
		public required string Badges { get; init; }
		public required bool IsPainted { get; init; }
		public required bool Warns { get; init; }
		public required string Header { get; init; }
		public required List<string> Lines { get; init; }
		public required string ToolTipText { get; init; }
		public required string OpenPath { get; init; }
		public required string Stamp { get; init; }
		public required RemasterPaintResult? Paint { get; init; }

		public Bitmap? Thumbnail
		{
			get
			{
				if(!_thumbnailRead) {
					_thumbnailRead = true;
					string path = RemasterTileFacts.ThumbnailPath(Tile);
					try {
						using FileStream s = File.OpenRead(path);
						_thumbnail = Bitmap.DecodeToHeight(s, 64, BitmapInterpolationMode.None);
					} catch(Exception) {
						//A picture the decoder refuses still has its caption and popover.
						_thumbnail = null;
					}
				}
				return _thumbnail;
			}
		}

		//paint == null: the comparison is still running; the popover says so.
		public static RemasterTileRow From(RemasterKitTile tile, RemasterPaintResult? paint, string stamp)
		{
			(RemasterTileCountKind kind, int n) = RemasterTileFacts.CountOf(tile);
			string count = kind switch {
				RemasterTileCountKind.Phases => ResourceHelper.GetMessage(n == 1 ? "RemasterTilePhase" : "RemasterTilePhases", n),
				RemasterTileCountKind.Cells => ResourceHelper.GetMessage(n == 1 ? "RemasterTileCell" : "RemasterTileCells", n),
				_ => "",
			};
			IReadOnlyList<RemasterProvenanceLine> facts = RemasterProvenance.Lines(tile, paint ?? RemasterPaintResult.Unknown(RemasterPaintUnknown.None));
			List<string> lines = facts.Select(l => paint == null && IsPaintLine(l.Kind) ? ResourceHelper.GetMessage("RemasterProvenancePaintChecking") : Sentence(l)).ToList();
			bool painted = paint?.State == RemasterPaintState.Painted;
			bool warns = RemasterTileFacts.Warns(facts);

			RemasterTileSource source = RemasterTileFacts.SourceOf(tile);
			string from = source switch {
				RemasterTileSource.Recording => ResourceHelper.GetMessage("RemasterTileFromRecording", RemasterKitReader.RecordingLabel(tile.RecordingId)),
				RemasterTileSource.ImportedPack => ResourceHelper.GetMessage("RemasterTileFromImport"),
				_ => ResourceHelper.GetMessage("RemasterTileFromEveryRecording"),
			};
			string header = string.Join(" · ", new[] { "\"" + tile.Caption + "\"", count, from }.Where(p => p.Length > 0));
			return new RemasterTileRow {
				Tile = tile,
				Caption = tile.Caption,
				CountText = count,
				Badges = (painted ? "✎" : "") + (warns ? "⚠" : ""),
				IsPainted = painted,
				Warns = warns,
				Header = header,
				Lines = lines,
				ToolTipText = string.Join(Environment.NewLine, new[] { header, tile.Title }.Concat(lines).Distinct()),
				OpenPath = RemasterTileFacts.OpenPath(tile),
				Stamp = stamp,
				Paint = paint,
			};
		}

		private static bool IsPaintLine(RemasterProvenanceKind kind)
		{
			return kind is RemasterProvenanceKind.Painted or RemasterProvenanceKind.NotPainted or RemasterProvenanceKind.PaintUnknown;
		}

		private static string Sentence(RemasterProvenanceLine l)
		{
			return l.Kind switch {
				RemasterProvenanceKind.CellsSeen or RemasterProvenanceKind.CellsFilled or RemasterProvenanceKind.CellsEmpty
					=> ResourceHelper.GetMessage("RemasterProvenance" + l.Kind, l.Count, l.Of),
				RemasterProvenanceKind.PaintUnknown => ResourceHelper.GetMessage("RemasterProvenancePaintUnknown" + l.Why),
				_ => ResourceHelper.GetMessage("RemasterProvenance" + l.Kind),
			};
		}
	}
}
