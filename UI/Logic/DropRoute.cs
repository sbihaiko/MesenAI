using System;

namespace Mesen.Logic;

public enum DropAction
{
	//The drop carried no file path.
	Ignore,
	//The path is not a file (gone, or a folder): say so.
	FileNotFound,
	//An IPS/BPS/UPS patch, told by its header: the ROM beside it opens patched.
	ApplyPatch,
	LoadState,
	//A movie, only while a game runs (it plays over the running game).
	PlayMovie,
	//Anything else - a ROM, or an archive the ROM loader opens.
	LoadRom
}

//#953: what a file dropped on the main window opens (MainWindow.OnDrop) and
//what LoadRomHelper.LoadFile does with a path - decided here over plain inputs
//so UI.Tests pins it; reading the file and acting on the answer stay in the
//window. There is deliberately no pack branch: a pack archive reaches the ROM
//loader like any other archive and a pack folder is not a file.
public static class DropRoute
{
	private const string SaveStateExt = ".mss";
	private static readonly string[] MovieExts = { ".mmo", ".bk2", ".gbmv" };

	//How many leading bytes Decide needs to tell a patch.
	public const int HeaderLength = 5;

	public static DropAction Decide(string? path, bool fileExists, ReadOnlySpan<byte> header, bool isRunning)
	{
		if(string.IsNullOrEmpty(path)) {
			return DropAction.Ignore;
		}
		if(!fileExists) {
			return DropAction.FileNotFound;
		}
		if(IsPatchHeader(header)) {
			return DropAction.ApplyPatch;
		}
		string ext = ExtensionOf(path);
		if(ext == SaveStateExt) {
			return DropAction.LoadState;
		}
		if(isRunning && Array.IndexOf(MovieExts, ext) >= 0) {
			return DropAction.PlayMovie;
		}
		return DropAction.LoadRom;
	}

	//"PATCH" (IPS), "BPS1" or "UPS1".
	public static bool IsPatchHeader(ReadOnlySpan<byte> header)
	{
		if(header.Length < HeaderLength) {
			return false;
		}
		if(header[0] == 'P' && header[1] == 'A' && header[2] == 'T' && header[3] == 'C' && header[4] == 'H') {
			return true;
		}
		return (header[0] == 'U' || header[0] == 'B') && header[1] == 'P' && header[2] == 'S' && header[3] == '1';
	}

	private static string ExtensionOf(string path)
	{
		int dot = path.LastIndexOf('.');
		int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
		return dot > slash ? path.Substring(dot).ToLowerInvariant() : "";
	}
}
