using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Mesen.Tests.Remaster
{
	//G.7: a project folder with a kit laid out as `scripts/mep_project.py kit`
	//writes it (kit/rec-NNN/kit.json, kit/pages/kit.json, the PNGs and their
	//`*.orig.png` twins), and a stdlib-shaped PNG writer for the pixels.
	public sealed class KitFixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "mesen-g7-" + Guid.NewGuid().ToString("N"));
		public string Project => Path.Combine(Root, "Contra (USA)");

		public KitFixture()
		{
			Directory.CreateDirectory(Project);
		}

		public void Dispose()
		{
			try {
				Directory.Delete(Root, true);
			} catch(IOException) {
			}
		}

		public string Write(string relative, string text)
		{
			string path = Path.Combine(Project, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, text);
			return path;
		}

		//A width x height RGBA picture filled with one colour, and its 1x twin
		//(the picture downscaled by `scale`, as the recorder writes it).
		public void WritePair(string relative, int width, int height, int scale, byte[] rgba)
		{
			string path = Path.Combine(Project, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, Png(width * scale, height * scale, rgba));
			File.WriteAllBytes(path.Substring(0, path.Length - 4) + ".orig.png", Png(width, height, rgba));
		}

		//Repaint one pixel of a picture (the artist's brush).
		public void Paint(string relative, int width, int height, byte[] rgba, byte[] brush)
		{
			string path = Path.Combine(Project, relative);
			byte[][] pixels = Enumerable.Range(0, width * height).Select(i => i == 0 ? brush : rgba).ToArray();
			File.WriteAllBytes(path, Png(width, height, pixels));
		}

		//The standard two-recording kit: one figure grid and one scenery object
		//in rec-001, one figure grid in rec-002, two pattern pages in pages/.
		public void WriteStandardKit()
		{
			byte[] red = { 255, 0, 0, 255 };
			WritePair("kit/rec-001/sheets/usr000.png", 4, 2, 4, red);
			WritePair("kit/rec-001/figures/usr000-figure.png", 4, 2, 4, red);
			WritePair("kit/rec-001/sheets/usr005.png", 2, 2, 4, red);
			WritePair("kit/rec-002/sheets/usr000.png", 4, 2, 4, red);
			WritePair("kit/pages/chr/Chr_0.png", 2, 2, 1, red);
			WritePair("kit/pages/chr/Chr_1.png", 2, 2, 1, red);
			Write("kit/rec-001/kit.json", Kit(
				Part("sprites", "{\"path\": \"sheets/usr000.png\", \"title\": \"run — a 6-phase loop, seen 2 time(s) — plays columns 1 2 3 4 5 6\", \"unit\": \"grid\", \"rows\": 1, \"columns\": 6, \"cells\": 6, \"seen\": true, \"playsColumns\": [1,2,3,4,5,6], \"figure\": \"figures/usr000-figure.png\"}"),
				Part("background", "{\"path\": \"sheets/usr005.png\", \"title\": \"obj000 (10 cells)\", \"unit\": \"object\", \"rows\": 3, \"columns\": 4, \"cells\": 10, \"seen\": true}")));
			Write("kit/rec-002/kit.json", Kit(
				Part("sprites", "{\"path\": \"sheets/usr000.png\", \"title\": \"pose007 — seen in 3 frame(s)\", \"unit\": \"grid\", \"rows\": 1, \"columns\": 1, \"cells\": 1, \"seen\": true}")));
			Write("kit/pages/kit.json", Kit(
				Part("chr", "{\"path\": \"chr/Chr_0.png\", \"title\": \"Chr_0 — CHR bank 1, 40 recorded / 12 ROM fill / 2 empty\", \"unit\": \"page\", \"rows\": 8, \"columns\": 8, \"cells\": 64, \"evidence\": 40, \"donated\": 9, \"folded\": 1, \"fill\": 12, \"empty\": 2, \"seen\": false, \"reference\": \"chr/Chr_0.orig.png\"}",
					"{\"path\": \"chr/Chr_1.png\", \"title\": \"Chr_1\", \"unit\": \"page\", \"rows\": 16, \"columns\": 16, \"cells\": 256, \"evidence\": 256, \"fill\": 0, \"empty\": 0, \"seen\": true}")));
		}

		public static string Kit(params string[] parts) => "{\"version\": 1, \"title\": \"t\", \"parts\": [" + string.Join(",", parts) + "]}";

		public static string Part(string part, params string[] files) => "{\"part\": \"" + part + "\", \"files\": [" + string.Join(",", files) + "]}";

		public static byte[] Png(int width, int height, byte[] rgba)
		{
			return Png(width, height, Enumerable.Repeat(rgba, width * height).ToArray());
		}

		//8-bit RGBA, filter 0 on every row: what Python's zlib/struct writers emit.
		public static byte[] Png(int width, int height, byte[][] pixels)
		{
			using MemoryStream raw = new();
			for(int y = 0; y < height; y++) {
				raw.WriteByte(0);
				for(int x = 0; x < width; x++) {
					raw.Write(pixels[y * width + x]);
				}
			}
			using MemoryStream z = new();
			using(ZLibStream deflate = new(z, CompressionLevel.Optimal, true)) {
				deflate.Write(raw.ToArray());
			}
			byte[] ihdr = new byte[13];
			BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
			BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
			ihdr[8] = 8;
			ihdr[9] = 6;
			using MemoryStream png = new();
			png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
			Chunk(png, "IHDR", ihdr);
			Chunk(png, "IDAT", z.ToArray());
			Chunk(png, "IEND", Array.Empty<byte>());
			return png.ToArray();
		}

		private static void Chunk(Stream s, string tag, byte[] body)
		{
			byte[] len = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
			s.Write(len);
			byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
			s.Write(tagBytes);
			s.Write(body);
			byte[] crc = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(tagBytes.Concat(body).ToArray()));
			s.Write(crc);
		}

		private static uint Crc(byte[] data)
		{
			uint c = 0xFFFFFFFF;
			foreach(byte b in data) {
				c ^= b;
				for(int k = 0; k < 8; k++) {
					c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
				}
			}
			return c ^ 0xFFFFFFFF;
		}
	}
}
