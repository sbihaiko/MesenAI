using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Mesen.Logic;

//G.7 (PRD Part B §13.5.3 W-R1/W-R5): the "Painted" badge of a tile is the
//same measurement scripts/mep_build.py makes before it lets a cell claim a
//key - the picture against its `*.orig.png` twin, upscaled nearest-neighbor
//(`_EditedProbe`). The reader mirrors `_png_pixels`: 8-bit RGB or RGBA,
//non-interlaced; anything else is "cannot tell", never a guess either way.
public sealed record RemasterPixels(int Width, int Height, int Channels, byte[] Data)
{
	public int Stride => Width * Channels;
}

public static class RemasterPng
{
	private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

	//Null when the file is missing, is not a PNG, or is a form this reader
	//does not decode (palette, gray, 16-bit, interlaced) - as mep_build.
	public static RemasterPixels? Read(string path)
	{
		byte[] data;
		try {
			data = File.ReadAllBytes(path);
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			return null;
		}
		return Decode(data);
	}

	public static RemasterPixels? Decode(byte[] data)
	{
		if(data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) {
			return null;
		}
		int width = 0, height = 0, channels = 0;
		using MemoryStream idat = new();
		int pos = 8;
		while(pos + 8 <= data.Length) {
			int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
			if(length < 0 || pos + 12 + (long)length > data.Length) {
				return null;
			}
			ReadOnlySpan<byte> tag = data.AsSpan(pos + 4, 4);
			ReadOnlySpan<byte> body = data.AsSpan(pos + 8, length);
			if(tag.SequenceEqual("IHDR"u8)) {
				if(length < 13) {
					return null;
				}
				width = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(0, 4));
				height = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
				byte depth = body[8], color = body[9], interlace = body[12];
				if(depth != 8 || interlace != 0 || (color != 2 && color != 6)) {
					return null;
				}
				channels = color == 2 ? 3 : 4;
			} else if(tag.SequenceEqual("IDAT"u8)) {
				idat.Write(body);
			} else if(tag.SequenceEqual("IEND"u8)) {
				break;
			}
			pos += 12 + length;
		}
		if(width <= 0 || height <= 0 || channels == 0 || (long)width * height * channels > 1L << 28) {
			return null;
		}
		int stride = width * channels;
		byte[] raw = new byte[(long)height * (stride + 1)];
		try {
			idat.Position = 0;
			using ZLibStream z = new(idat, CompressionMode.Decompress);
			int read = 0;
			while(read < raw.Length) {
				int n = z.Read(raw, read, raw.Length - read);
				if(n == 0) {
					return null;
				}
				read += n;
			}
		} catch(InvalidDataException) {
			return null;
		}
		byte[] output = new byte[(long)height * stride];
		for(int y = 0; y < height; y++) {
			int src = y * (stride + 1);
			byte filter = raw[src];
			int dst = y * stride;
			int prev = dst - stride;
			for(int i = 0; i < stride; i++) {
				int a = i >= channels ? output[dst + i - channels] : 0;
				int b = y > 0 ? output[prev + i] : 0;
				int c = i >= channels && y > 0 ? output[prev + i - channels] : 0;
				int x = raw[src + 1 + i];
				int value = filter switch {
					0 => x,
					1 => x + a,
					2 => x + b,
					3 => x + ((a + b) >> 1),
					4 => x + Paeth(a, b, c),
					_ => -1,
				};
				if(value < 0) {
					return null;
				}
				output[dst + i] = (byte)value;
			}
		}
		return new RemasterPixels(width, height, channels, output);
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c;
		int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}

public enum RemasterPaintState
{
	//Equal to its twin: nothing painted yet.
	Untouched,
	Painted,
	//No usable twin, or a twin this surface does not keep as a pre-paint copy.
	Unknown
}

public enum RemasterPaintUnknown
{
	None,
	//Pattern pages, whole-screen captures and imported sheets: their twin is
	//not a pixel copy of the untouched picture (measured 2026-10-02 - the
	//pages go through the pack's scale filter, so an untouched page already
	//differs from its twin), so comparing them would call everything painted.
	NoPrePaintCopy,
	NoTwin,
	Unreadable,
	SizeMismatch
}

public sealed record RemasterPaintResult(RemasterPaintState State, RemasterPaintUnknown Why)
{
	public static RemasterPaintResult Untouched { get; } = new(RemasterPaintState.Untouched, RemasterPaintUnknown.None);
	public static RemasterPaintResult Painted { get; } = new(RemasterPaintState.Painted, RemasterPaintUnknown.None);
	public static RemasterPaintResult Unknown(RemasterPaintUnknown why) => new(RemasterPaintState.Unknown, why);
}

public static class RemasterPaintProbe
{
	//The picture against its twin upscaled by the whole-number ratio of their
	//widths (the twin is 1x, the sheet at the pack's <scale>).
	public static RemasterPaintResult Compare(string picturePath, string twinPath)
	{
		if(string.IsNullOrEmpty(twinPath) || !File.Exists(twinPath)) {
			return RemasterPaintResult.Unknown(RemasterPaintUnknown.NoTwin);
		}
		RemasterPixels? sheet = RemasterPng.Read(picturePath);
		RemasterPixels? orig = RemasterPng.Read(twinPath);
		if(sheet == null || orig == null) {
			return RemasterPaintResult.Unknown(RemasterPaintUnknown.Unreadable);
		}
		return Compare(sheet, orig);
	}

	public static RemasterPaintResult Compare(RemasterPixels sheet, RemasterPixels orig)
	{
		if(orig.Width == 0 || sheet.Width % orig.Width != 0 || sheet.Channels != orig.Channels) {
			return RemasterPaintResult.Unknown(RemasterPaintUnknown.SizeMismatch);
		}
		int n = sheet.Width / orig.Width;
		if(n < 1 || orig.Height * n != sheet.Height) {
			return RemasterPaintResult.Unknown(RemasterPaintUnknown.SizeMismatch);
		}
		int ch = sheet.Channels;
		for(int y = 0; y < sheet.Height; y++) {
			int srow = y * sheet.Stride;
			int orow = (y / n) * orig.Stride;
			for(int x = 0; x < sheet.Width; x++) {
				int s = srow + x * ch;
				int o = orow + (x / n) * ch;
				for(int c = 0; c < ch; c++) {
					if(sheet.Data[s + c] != orig.Data[o + c]) {
						return RemasterPaintResult.Painted;
					}
				}
			}
		}
		return RemasterPaintResult.Untouched;
	}
}
