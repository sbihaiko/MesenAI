using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Mesen.Logic;

//#951: an opaque 8-bit sRGB colour, the comparator's own type so it never
//needs Avalonia's Color (UI.Tests dual-compiles this file host-free).
public readonly record struct Rgb(byte R, byte G, byte B)
{
	public static Rgb Parse(string hex)
	{
		string h = hex.TrimStart('#');
		if(h.Length != 6) {
			throw new FormatException("expected #RRGGBB, got " + hex);
		}
		int v = Convert.ToInt32(h, 16);
		return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
	}

	public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

//A box in device pixels; Right and Bottom are exclusive.
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
	public int Right => X + Width;
	public int Bottom => Y + Height;
	public bool IsEmpty => Width <= 0 || Height <= 0;

	//The part of this box inside a width x height frame (empty when none is).
	public PixelBox ClampTo(int width, int height)
	{
		int x0 = Math.Clamp(X, 0, width), y0 = Math.Clamp(Y, 0, height);
		int x1 = Math.Clamp(Right, 0, width), y1 = Math.Clamp(Bottom, 0, height);
		return new PixelBox(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
	}
}

//A box in logical pixels.
public readonly record struct LogicalBox(double X, double Y, double Width, double Height)
{
	public double Left => X;
	public double Top => Y;
	public double Right => X + Width;
	public double Bottom => Y + Height;
}

//An opaque RGB copy of a frame, so the comparison never depends on a bitmap's
//pixel format. The headless hook fills it from an Avalonia bitmap; FromPng
//reads a committed PNG host-free, which is what lets CI compare without a core.
public sealed class RgbFrame
{
	public int Width { get; }
	public int Height { get; }
	private readonly byte[] _rgb;

	public RgbFrame(int width, int height, byte[] rgb)
	{
		if(rgb.Length != width * height * 3) {
			throw new ArgumentException($"{width} x {height} needs {width * height * 3} bytes, got {rgb.Length}");
		}
		Width = width;
		Height = height;
		_rgb = rgb;
	}

	public Rgb At(int x, int y)
	{
		int i = (y * Width + x) * 3;
		return new Rgb(_rgb[i], _rgb[i + 1], _rgb[i + 2]);
	}

	public RgbFrame With(PixelBox area, Rgb fill)
	{
		byte[] copy = (byte[])_rgb.Clone();
		for(int y = area.Y; y < area.Bottom; y++) {
			for(int x = area.X; x < area.Right; x++) {
				int i = (y * Width + x) * 3;
				copy[i] = fill.R; copy[i + 1] = fill.G; copy[i + 2] = fill.B;
			}
		}
		return new RgbFrame(Width, Height, copy);
	}

	//The area's content moved dy px down, the uncovered strip filled with background.
	public RgbFrame Moved(PixelBox area, int dy, Rgb background)
	{
		RgbFrame moved = With(area, background);
		for(int y = area.Y; y < area.Bottom && y + dy < Height; y++) {
			Array.Copy(_rgb, (y * Width + area.X) * 3, moved._rgb, ((y + dy) * Width + area.X) * 3, area.Width * 3);
		}
		return moved;
	}

	//Area-averages the box down to width x height.
	public RgbFrame Scaled(PixelBox box, int width, int height)
	{
		byte[] rgb = new byte[width * height * 3];
		for(int y = 0; y < height; y++) {
			int sy0 = box.Y + y * box.Height / height, sy1 = Math.Max(sy0 + 1, box.Y + (y + 1) * box.Height / height);
			for(int x = 0; x < width; x++) {
				int sx0 = box.X + x * box.Width / width, sx1 = Math.Max(sx0 + 1, box.X + (x + 1) * box.Width / width);
				int r = 0, g = 0, b = 0, n = 0;
				for(int sy = sy0; sy < sy1; sy++) {
					for(int sx = sx0; sx < sx1; sx++) {
						int i = (sy * Width + sx) * 3;
						r += _rgb[i]; g += _rgb[i + 1]; b += _rgb[i + 2]; n++;
					}
				}
				int o = (y * width + x) * 3;
				rgb[o] = (byte)(r / n); rgb[o + 1] = (byte)(g / n); rgb[o + 2] = (byte)(b / n);
			}
		}
		return new RgbFrame(width, height, rgb);
	}

	public static RgbFrame FromPng(string path) => FromPng(File.ReadAllBytes(path));

	//A non-interlaced 8-bit PNG (grey, RGB, palette, grey+alpha or RGBA) as
	//opaque RGB; alpha is dropped, which is right for the opaque renders and
	//wireframes this reads. Anything else throws NotSupportedException.
	public static RgbFrame FromPng(byte[] png)
	{
		ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
		if(png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(signature)) {
			throw new InvalidDataException("not a PNG");
		}
		int width = 0, height = 0, colorType = -1;
		byte[]? palette = null;
		using MemoryStream idat = new();
		for(int pos = 8; pos + 8 <= png.Length;) {
			int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
			string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
			ReadOnlySpan<byte> data = png.AsSpan(pos + 8, length);
			if(type == "IHDR") {
				width = BinaryPrimitives.ReadInt32BigEndian(data);
				height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
				colorType = data[9];
				if(data[8] != 8 || data[12] != 0) {
					throw new NotSupportedException($"PNG bit depth {data[8]}, interlace {data[12]}: only 8-bit non-interlaced is read");
				}
			} else if(type == "PLTE") {
				palette = data.ToArray();
			} else if(type == "IDAT") {
				idat.Write(data);
			} else if(type == "IEND") {
				break;
			}
			pos += 12 + length;
		}
		int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("PNG colour type " + colorType) };
		int stride = width * channels;
		byte[] raw = new byte[height * stride];
		idat.Position = 0;
		using(ZLibStream inflate = new(idat, CompressionMode.Decompress)) {
			byte[] prior = new byte[stride], line = new byte[stride];
			for(int y = 0; y < height; y++) {
				int filter = inflate.ReadByte();
				inflate.ReadExactly(line);
				Unfilter(filter, line, prior, channels);
				Array.Copy(line, 0, raw, y * stride, stride);
				(prior, line) = (line, prior);
			}
		}
		byte[] rgb = new byte[width * height * 3];
		for(int p = 0; p < width * height; p++) {
			int s = p * channels;
			(byte r, byte g, byte b) = colorType switch {
				0 or 4 => (raw[s], raw[s], raw[s]),
				3 => (palette![raw[s] * 3], palette[raw[s] * 3 + 1], palette[raw[s] * 3 + 2]),
				_ => (raw[s], raw[s + 1], raw[s + 2]),
			};
			rgb[p * 3] = r; rgb[p * 3 + 1] = g; rgb[p * 3 + 2] = b;
		}
		return new RgbFrame(width, height, rgb);
	}

	private static void Unfilter(int filter, byte[] line, byte[] prior, int bpp)
	{
		for(int i = 0; i < line.Length; i++) {
			int a = i >= bpp ? line[i - bpp] : 0, b = prior[i], c = i >= bpp ? prior[i - bpp] : 0;
			line[i] = (byte)(line[i] + filter switch {
				0 => 0,
				1 => a,
				2 => b,
				3 => (a + b) / 2,
				4 => Paeth(a, b, c),
				_ => throw new InvalidDataException("PNG filter " + filter),
			});
		}
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
