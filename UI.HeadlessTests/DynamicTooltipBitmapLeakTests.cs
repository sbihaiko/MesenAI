using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mesen.Debugger.Controls;
using Mesen.Utilities;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #861: a tooltip picture wraps its source in a DynamicCroppedBitmap, which
//subscribes to the source bitmap's Invalidated event. Nothing ever unsubscribed,
//so the source kept every DynamicCroppedBitmap ever built alive. Hovering across
//tiles replaces the TooltipPictureEntry each time (TileViewerViewModel ->
//TooltipEntries.AddPicture) and one more bitmap leaks per tile.
public class DynamicTooltipBitmapLeakTests
{
	//CroppedBitmap only accepts an IBitmap as its source, so the counting source
	//is a real (headless) bitmap that only adds the listener counter - the app's
	//own DynamicBitmap shape, without the render data the test never draws.
	private sealed class CountingBitmap : WriteableBitmap, IDynamicBitmap
	{
		private EventHandler? _invalidated;

		public int HandlerCount { get; private set; }

		public CountingBitmap() : base(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul)
		{
		}

		public event EventHandler? Invalidated
		{
			add { _invalidated += value; HandlerCount++; }
			remove { _invalidated -= value; HandlerCount--; }
		}

		public void Invalidate() => _invalidated?.Invoke(this, EventArgs.Empty);
	}

	[AvaloniaFact]
	public void Replacing_the_tooltip_picture_releases_the_previous_source_subscription()
	{
		CountingBitmap firstTile = new CountingBitmap();
		CountingBitmap secondTile = new CountingBitmap();

		TooltipEntries entries = new TooltipEntries();
		entries.StartUpdate();
		entries.AddPicture("Tile", firstTile, 6, new PixelRect(0, 0, 8, 8));
		Assert.Equal(1, firstTile.HandlerCount);

		//The pointer moves onto another tile: TileViewerViewModel replaces the
		//entry's picture with a fresh TooltipPictureEntry (TooltipEntries.AddPicture).
		entries.StartUpdate();
		entries.AddPicture("Tile", secondTile, 6, new PixelRect(8, 0, 8, 8));

		Assert.Equal(0, firstTile.HandlerCount);
		Assert.Equal(1, secondTile.HandlerCount);
	}

	//The trigger itself: TileViewerViewModel reuses one ViewerBitmap and crops a
	//different rect per tile, so each hover builds a fresh cropped bitmap over the
	//same source. Before the fix each one stayed subscribed - the count grew with
	//every tile visited.
	[AvaloniaFact]
	public void Repeated_hovers_over_one_source_do_not_accumulate_subscriptions()
	{
		CountingBitmap viewerBitmap = new CountingBitmap();
		TooltipEntries entries = new TooltipEntries();

		for(int tile = 0; tile < 5; tile++) {
			entries.StartUpdate();
			entries.AddPicture("Tile", viewerBitmap, 6, new PixelRect(tile * 8, 0, 8, 8));
		}

		Assert.Equal(1, viewerBitmap.HandlerCount);
	}
}
