using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace Mesen.ViewModels
{
	//#1039 (ADR-0265 section 4, review finding 2 on PR #1057): a cover as the tile
	//keeps it. A body can be 4 MiB and ~1000x1400, drawn at 104x139, so it is
	//decoded down to twice the tile's width (room for a 2x display) on the worker
	//that asked for it and only the small bitmap goes to the UI thread.
	public static class BoxArtBitmap
	{
		public const int TileWidth = 104;
		public const int DecodeWidth = 2 * TileWidth;

		//Null for a file the decoder refuses: that tile stays generic.
		public static Bitmap? Decode(string path)
		{
			try {
				using FileStream stream = File.OpenRead(path);
				return Bitmap.DecodeToWidth(stream, DecodeWidth);
			} catch(Exception) {
				return null;
			}
		}
	}
}
