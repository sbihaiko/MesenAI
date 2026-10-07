using System;
using System.Globalization;
using System.IO;

namespace Mesen.Logic
{
	//Where a cover lives on the player's disk, and what "there is no cover" looks
	//like there. Split from BoxArtCache so that the file layout is one small,
	//readable thing and the fetch flow is another.
	//
	//The layout under the cache directory is:
	//
	//    <cache>/<console tag>/<sha1>.boxart.png     a downloaded box art
	//    <cache>/<console tag>/<sha1>.title.jpg      a downloaded title screen
	//    <cache>/<console tag>/<sha1>.miss           a recorded miss, timestamped
	//
	//The key is the console tag plus the ROM's SHA1 (seen-before: the cache is
	//keyed by what the game *is*, never by its file name, so a renamed or moved
	//ROM keeps its cover). The kind and the image format are in the file name so
	//that reading the cache back needs no sidecar and no decoder: a hit is a
	//`File.Exists` on four candidate names, in cover-priority order.
	internal static class BoxArtCacheStore
	{
		public const string MissSuffix = "miss";

		private static readonly (BoxArtCoverKind Kind, BoxArtImageFormat Format)[] CandidateOrder = {
			(BoxArtCoverKind.Boxart, BoxArtImageFormat.Png),
			(BoxArtCoverKind.Boxart, BoxArtImageFormat.Jpeg),
			(BoxArtCoverKind.Title, BoxArtImageFormat.Png),
			(BoxArtCoverKind.Title, BoxArtImageFormat.Jpeg)
		};

		public static string ConsoleFolder(string cacheDirectory, string consoleTag) => Path.Combine(cacheDirectory, consoleTag);

		public static string ImagePath(string folder, string sha1, BoxArtCoverKind kind, BoxArtImageFormat format) =>
			Path.Combine(folder, $"{sha1}.{KindName(kind)}.{BoxArtImage.Extension(format)}");

		public static string MissPath(string folder, string sha1) => Path.Combine(folder, $"{sha1}.{MissSuffix}");

		//The cover already on disk, in cover-priority order (a box art beats a
		//title screen when both were somehow downloaded), or null.
		public static BoxArtCover? Find(string cacheDirectory, string consoleTag, string sha1)
		{
			foreach((BoxArtCoverKind kind, BoxArtImageFormat format) in CandidateOrder) {
				string path = ImagePath(ConsoleFolder(cacheDirectory, consoleTag), sha1, kind, format);
				if(File.Exists(path)) {
					return new BoxArtCover(path, kind);
				}
			}
			return null;
		}

		//Whether a miss has been recorded recently enough to still count. False for
		//a marker that cannot be read or whose stamp cannot be parsed: a miss that
		//cannot be trusted is re-asked, never kept forever.
		public static bool IsMissFresh(string folder, string sha1, TimeSpan expiry, DateTimeOffset now)
		{
			string path = MissPath(folder, sha1);
			if(!File.Exists(path)) {
				return false;
			}

			string text;
			try {
				text = File.ReadAllText(path);
			} catch(IOException) {
				return false;
			} catch(UnauthorizedAccessException) {
				return false;
			}

			if(!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset stamp)) {
				return false;
			}
			return now - stamp < expiry;
		}

		//Writes the image and answers the path it was written to, or null when it
		//could not be written - a cache that cannot write must not hand back a path
		//that holds nothing.
		public static string? WriteImage(string folder, string sha1, BoxArtCoverKind kind, BoxArtImageFormat format, byte[] body)
		{
			string path = ImagePath(folder, sha1, kind, format);
			try {
				Directory.CreateDirectory(folder);
				File.WriteAllBytes(path, body);
				return path;
			} catch(IOException) {
				return null;
			} catch(UnauthorizedAccessException) {
				return null;
			}
		}

		public static void WriteMiss(string folder, string sha1, DateTimeOffset now)
		{
			try {
				Directory.CreateDirectory(folder);
				File.WriteAllText(MissPath(folder, sha1), now.ToString("O", CultureInfo.InvariantCulture));
			} catch(IOException) {
				//A miss that cannot be recorded costs a repeated request, never a
				//broken tile - the caller already has its null.
			} catch(UnauthorizedAccessException) {
			}
		}

		public static void ClearMiss(string folder, string sha1)
		{
			try {
				string path = MissPath(folder, sha1);
				if(File.Exists(path)) {
					File.Delete(path);
				}
			} catch(IOException) {
			} catch(UnauthorizedAccessException) {
			}
		}

		private static string KindName(BoxArtCoverKind kind) =>
			kind == BoxArtCoverKind.Title ? "title" : "boxart";
	}
}
