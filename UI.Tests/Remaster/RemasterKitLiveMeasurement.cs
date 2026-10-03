using System;
using System.Diagnostics;
using System.Linq;
using Mesen.Logic;
using Xunit;
using Xunit.Abstractions;

namespace Mesen.Tests.Remaster
{
	//G.7 measurement, opt-in: MESENAI_G7_PROJECT=<a project folder whose kit
	//`mep_project.py kit` just wrote>. A freshly prepared kit has nothing
	//painted, so the probe must call no tile painted (no false badge), and it
	//prints the per-category counts and the probe's cost.
	public sealed class RemasterKitLiveMeasurement
	{
		private readonly ITestOutputHelper _out;

		public RemasterKitLiveMeasurement(ITestOutputHelper output)
		{
			_out = output;
		}

		[Fact]
		public void A_fresh_real_kit_reads_every_tile_and_badges_none_as_painted()
		{
			string project = Environment.GetEnvironmentVariable("MESENAI_G7_PROJECT") ?? "";
			if(project.Length == 0) {
				return;
			}
			Stopwatch clock = Stopwatch.StartNew();
			RemasterKit kit = RemasterKitReader.Read(project);
			long readMs = clock.ElapsedMilliseconds;
			clock.Restart();
			RemasterPaintResult[] paint = kit.Tiles.Select(t => RemasterProvenance.Paint(t, RemasterPaintProbe.Compare)).ToArray();
			long paintMs = clock.ElapsedMilliseconds;

			foreach(RemasterKitCategory c in Enum.GetValues<RemasterKitCategory>()) {
				_out.WriteLine($"{c}: {kit.Count(c)}");
			}
			_out.WriteLine($"untouched {paint.Count(p => p.State == RemasterPaintState.Untouched)}, painted {paint.Count(p => p.State == RemasterPaintState.Painted)}, unknown {paint.Count(p => p.State == RemasterPaintState.Unknown)}");
			_out.WriteLine($"read {readMs} ms, paint probe {paintMs} ms, problems {kit.Problems.Count}");
			//ADR-0252: the shapes the recordings drew, and the cells painted
			//(a fresh kit paints none, so the count must be 0 or unknown).
			clock.Restart();
			int? shapes = new RemasterShapeCache().Count(RemasterProjectReader.Read(project).Recordings);
			RemasterTileCells[] cells = kit.Tiles.Select(RemasterCellPaint.Measure).ToArray();
			int? painted = RemasterCellPaint.Total(cells);
			_out.WriteLine($"shapes seen {shapes?.ToString() ?? "unknown"}, cells painted {painted?.ToString() ?? "unknown"} over {cells.Count(c => c.Cells != null)} measurable tiles, phases known on {kit.Tiles.Where((t, i) => RemasterCellPaint.Phases(t, cells[i]) != null).Count()}, {clock.ElapsedMilliseconds} ms");
			Assert.True(painted is null or 0, "a fresh kit counts painted cells");
			Assert.Empty(kit.Problems);
			Assert.NotEmpty(kit.Tiles);
			Assert.DoesNotContain(paint, p => p.State == RemasterPaintState.Painted);
		}
	}
}
