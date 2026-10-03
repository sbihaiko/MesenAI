using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Mesen.Interop
{
	//The in-memory frame capture (InteropDLL/EmuApiWrapperHeadless.cpp, F9.15):
	//the frame the emulator is showing, after the video filter - so an HD pack's
	//larger frame, the NTSC/scale filters and the screen rotation are in it,
	//exactly like a saved screenshot. Play's pause surfaces use it for the
	//frozen frame behind the scrim (PlayFrozenFrame).
	public static class FrameCaptureApi
	{
		private const string DllPath = EmuApi.DllName;

		[DllImport(DllPath)][return: MarshalAs(UnmanagedType.I1)] private static extern bool HeadlessCaptureFrame(out UInt32 width, out UInt32 height, out UInt32 frameNumber, out UInt32 pixelCount);
		[DllImport(DllPath)] private static extern UInt32 HeadlessReadCapturedPixels([Out] UInt32[] pixels, UInt32 maxPixels);
		[DllImport(DllPath)] public static extern UInt32 HeadlessGetFrameCount();

		//The current frame as an opaque bitmap, or null when nothing has been
		//decoded yet. Pixels come as 0xAARRGGBB, i.e. BGRA bytes on every
		//platform the app ships on (little-endian).
		public static WriteableBitmap? CaptureFrame()
		{
			if(!HeadlessCaptureFrame(out UInt32 width, out UInt32 height, out _, out UInt32 pixelCount) || pixelCount != width * height) {
				return null;
			}
			UInt32[] pixels = new UInt32[pixelCount];
			if(HeadlessReadCapturedPixels(pixels, pixelCount) != pixelCount) {
				return null;
			}

			WriteableBitmap bitmap = new(new PixelSize((int)width, (int)height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
			using(ILockedFramebuffer target = bitmap.Lock()) {
				byte[] row = new byte[width * 4];
				for(int y = 0; y < height; y++) {
					Buffer.BlockCopy(pixels, y * row.Length, row, 0, row.Length);
					Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
				}
			}
			return bitmap;
		}
	}
}
