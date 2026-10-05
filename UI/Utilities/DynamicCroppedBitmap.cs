using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace Mesen.Utilities
{
	//Issue #861: subscribing to the source's Invalidated event pins this bitmap
	//for as long as the source lives, so the stream of tooltip pictures built while
	//hovering across tiles used to keep the viewer bitmap's delegate list - and
	//every bitmap it pointed at - alive forever.
	public class DynamicCroppedBitmap : CroppedBitmap, IDynamicBitmap
	{
		public new event EventHandler? Invalidated;

		private bool _detached;

		static DynamicCroppedBitmap()
		{
			SourceProperty.Changed.AddClassHandler<DynamicCroppedBitmap>((x, e) => {
				if(e.OldValue is IDynamicBitmap oldSource) {
					oldSource.Invalidated -= x.OnSourceInvalidated;
				}

				if(!x._detached && x.Source is IDynamicBitmap newSource) {
					newSource.Invalidated += x.OnSourceInvalidated;
				}

				x.Invalidate();
			});
		}

		public DynamicCroppedBitmap(IImage source, PixelRect sourceRect) : base(source, sourceRect)
		{
		}

		public void Invalidate()
		{
			Invalidated?.Invoke(this, EventArgs.Empty);
		}

		//Drops the subscription to the source bitmap. This is deliberately not
		//IDisposable.Dispose: CroppedBitmap.Dispose() disposes the source bitmap,
		//which the tooltip only borrows and must not tear down.
		public void Detach()
		{
			if(_detached) {
				return;
			}

			_detached = true;
			if(Source is IDynamicBitmap source) {
				source.Invalidated -= OnSourceInvalidated;
			}
		}

		private void OnSourceInvalidated(object? sender, EventArgs e)
		{
			Invalidate();
		}
	}
}
