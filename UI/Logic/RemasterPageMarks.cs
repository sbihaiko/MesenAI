using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mesen.Logic;

//ADR-0219 (G.7 follow-up, PRD Part B §13.5.3): a pattern page mixes cells the
//recording saw in play with cells the kit filled from the game's own data - a
//static kit (F12.9) is nothing but fills - and the thumbnail said nothing
//about which cell was which. What a page's cells are comes from the page's own
//sidecar beside the picture (ADR-0172's `<stem>.json`: `state` and `seen` per
//cell), never from the pixels and never inferred. A page whose sidecar is
//missing, foreign or unreadable is "cannot tell" and stays undimmed.
public enum RemasterPageCellKind
{
	//`state: fill` - the shape came out of the ROM, not out of play.
	Fill,
	//`state: empty` - neither seen in play nor in the game's own data.
	Empty
}

//One cell of a page, in the thumbnail's own pixels.
public sealed record RemasterPageMark(double X, double Y, double Size, RemasterPageCellKind Kind);

//A page thumbnail: the canvas its marks land on (the picture's own aspect at
//`Height` px tall) and the cells to mark.
public sealed record RemasterPageThumb(double Width, double Height, IReadOnlyList<RemasterPageMark> Marks);

public static class RemasterPageMarks
{
	//The `kind` a page sidecar declares (artist_chr_kit's own word).
	public const string SidecarKind = "chr";
	//mep_build's cell side when a sidecar declares none.
	private const int DefaultGridUnit = 8;

	private static readonly byte[] PngSignature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

	//Null = cannot tell (no pattern page, no sidecar beside the picture, no
	//picture size): the thumbnail then keeps every cell as it is.
	public static RemasterPageThumb? Thumbnail(RemasterKitTile tile, double height)
	{
		if(tile.Category != RemasterKitCategory.PatternPages || height <= 0) {
			return null;
		}
		(int Width, int Height) picture = PictureSize(tile.ImagePath);
		if(picture.Width <= 0 || picture.Height <= 0) {
			return null;
		}
		string sidecar = RemasterCellPaint.SidecarOf(tile.ImagePath);
		if(sidecar.Length == 0 || !File.Exists(sidecar)) {
			return null;
		}
		string json;
		try {
			json = File.ReadAllText(sidecar);
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			return null;
		}
		return Of(json, picture.Width, picture.Height, height);
	}

	//The marks a page sidecar carries, scaled from the page's own pixels to a
	//thumbnail `height` px tall. Null when the file is no page sidecar: a
	//sheet's sidecar (a figure grid, a scenery object) has the same `cells[]`
	//shape and must never mark a page.
	public static RemasterPageThumb? Of(string sidecarJson, int pictureWidth, int pictureHeight, double height)
	{
		if(pictureWidth <= 0 || pictureHeight <= 0 || height <= 0) {
			return null;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(sidecarJson);
			JsonElement root = doc.RootElement;
			if(root.ValueKind != JsonValueKind.Object || Str(root, "kind") != SidecarKind
				|| !root.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Array) {
				return null;
			}
			int unit = Int(root, "gridUnit", DefaultGridUnit);
			unit = unit > 0 ? unit : DefaultGridUnit;
			int scale = Int(root, "scale", 1);
			scale = scale > 0 ? scale : 1;
			double k = height / pictureHeight;
			double side = unit * scale * k;
			List<RemasterPageMark> marks = new();
			foreach(JsonElement c in cells.EnumerateArray()) {
				if(!TryInt(c, "x", out int x) || !TryInt(c, "y", out int y)) {
					continue;
				}
				RemasterPageCellKind? kind = KindOf(Str(c, "state"));
				if(kind != null) {
					marks.Add(new RemasterPageMark(x * k, y * k, side, kind.Value));
				}
			}
			return new RemasterPageThumb(pictureWidth * k, height, marks);
		} catch(JsonException) {
			return null;
		}
	}

	//The two states a thumbnail can mark. Every other state the kit writes
	//(`evidence`, `borrowed`, `donated`, `folded`) was seen in play, and a token
	//this reader does not know is "cannot tell" - both stay as the picture is.
	private static RemasterPageCellKind? KindOf(string state)
	{
		return state switch {
			"fill" => RemasterPageCellKind.Fill,
			"empty" => RemasterPageCellKind.Empty,
			_ => null,
		};
	}

	//What a row keeps to know whether its marks are still the sidecar's: the
	//sidecar is not part of the picture's own stamp, and a kit re-run may
	//rewrite it with the picture untouched.
	public static string Stamp(RemasterKitTile tile)
	{
		return RemasterFileStamp.Of(RemasterCellPaint.SidecarOf(tile.ImagePath));
	}

	//The picture's own size, off its IHDR chunk: the one fact the overlay needs
	//to land a cell on the thumbnail, and 24 bytes rather than a PNG decode.
	//(0, 0) when the file is missing or is not a PNG this reader can size.
	public static (int Width, int Height) PictureSize(string path)
	{
		byte[] head = new byte[24];
		try {
			using FileStream s = File.OpenRead(path);
			int read = 0;
			while(read < head.Length) {
				int n = s.Read(head, read, head.Length - read);
				if(n <= 0) {
					return (0, 0);
				}
				read += n;
			}
		} catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {
			return (0, 0);
		}
		ReadOnlySpan<byte> span = head;
		if(!span.Slice(0, 8).SequenceEqual(PngSignature) || !span.Slice(12, 4).SequenceEqual("IHDR"u8)) {
			return (0, 0);
		}
		int width = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4));
		int height = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(20, 4));
		return width > 0 && height > 0 ? (width, height) : (0, 0);
	}

	private static string Str(JsonElement e, string key)
	{
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
	}

	private static int Int(JsonElement e, string key, int fallback) => TryInt(e, key, out int n) ? n : fallback;

	private static bool TryInt(JsonElement e, string key, out int n)
	{
		n = 0;
		return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out n);
	}
}
