using System;
using System.Collections.Generic;
using System.Text;

namespace Mesen.Logic;

//#845 follow-up (ADR-0256 Decision 9 amendment): which mounts the library scan
//must not enter.
//
//A network mount whose server is gone blocks a directory read in the kernel, and
//no wall-clock budget can interrupt that - the scan would hang the sheet instead
//of degrading. Nothing about the *mount table* is host-free, so reading it is
//RomFolderScanSource's job (the `mount` command on macOS, /proc/mounts on
//Linux); what is host-free is the rule that turns that text into a list of mount
//points, which is why it lives here and is pinned in UI.Tests.
public static class NetworkMounts
{
	//The file systems a library cannot live on: a server that is not answering is
	//a folder that never returns. `nfs` is matched by prefix so nfs2/nfs3/nfs4
	//come with it.
	public static bool IsNetworkFileSystem(string fileSystem)
	{
		if(fileSystem.StartsWith("nfs", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		foreach(string name in new[] { "smbfs", "afpfs", "webdav", "cifs", "autofs" }) {
			if(string.Equals(fileSystem, name, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}
		return false;
	}

	//The mount points of the network file systems in one mount table, in the
	//order the table lists them.
	//
	//Two shapes, because the two platforms spell it differently and the host
	//hands whichever it read straight in:
	//  macOS `mount`: "/dev/disk3s1 on / (apfs, local)" - the source, the word
	//    "on", the mount point, then the options in parentheses, whose first
	//    token is the file system;
	//  Linux /proc/mounts: "/dev/sda1 / ext4 rw,relatime 0 0" - whitespace
	//    separated, the file system third, and the two paths escaped.
	public static IReadOnlyList<string> FromTable(string table)
	{
		List<string> mounts = new();
		foreach(string raw in table.Split('\n')) {
			string line = raw.TrimEnd('\r').Trim();
			if(line.Length == 0 || line[0] == '#') {
				continue;
			}
			if(ParseMountCommandLine(line) is string point) {
				mounts.Add(point);
			} else if(ParseProcMountsLine(line) is string other) {
				mounts.Add(other);
			}
		}
		return mounts;
	}

	//`<source> on <mount point> (<file system>, <options>)`. The mount point is
	//everything between the first " on " and the LAST " (" - a volume name may
	//hold either, and the parenthesised part is the tail of the line.
	private static string? ParseMountCommandLine(string line)
	{
		int on = line.IndexOf(" on ", StringComparison.Ordinal);
		if(on < 0) {
			return null;
		}
		int options = line.LastIndexOf(" (", StringComparison.Ordinal);
		if(options <= on) {
			return null;
		}
		string fileSystem = FileSystemOf(line[(options + 2)..]);
		return IsNetworkFileSystem(fileSystem) ? line[(on + 4)..options] : null;
	}

	//The first token inside the parentheses: "smbfs" out of "smbfs, nodev)".
	private static string FileSystemOf(string options)
	{
		int end = options.IndexOfAny(new[] { ',', ')' });
		return (end < 0 ? options : options[..end]).Trim();
	}

	//`<source> <mount point> <file system> <options> <dump> <pass>`. Fewer than
	//three fields is not a mount line; the two paths are unescaped because
	///proc/mounts spells a space as `\040`, and a mount point the rest of the app
	//cannot compare is worse than none.
	private static string? ParseProcMountsLine(string line)
	{
		string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if(fields.Length < 3 || !IsNetworkFileSystem(fields[2])) {
			return null;
		}
		return Unescape(fields[1]);
	}

	//The four escapes /proc/mounts uses (util-linux, `mangle`): space, tab,
	//newline and backslash.
	private static string Unescape(string path)
	{
		if(!path.Contains('\\')) {
			return path;
		}
		StringBuilder unescaped = new(path.Length);
		for(int i = 0; i < path.Length; i++) {
			if(path[i] == '\\' && i + 3 < path.Length && IsOctal(path[i + 1]) && IsOctal(path[i + 2]) && IsOctal(path[i + 3])) {
				unescaped.Append((char)((path[i + 1] - '0') * 64 + (path[i + 2] - '0') * 8 + (path[i + 3] - '0')));
				i += 3;
				continue;
			}
			unescaped.Append(path[i]);
		}
		return unescaped.ToString();
	}

	private static bool IsOctal(char c)
	{
		return c is >= '0' and <= '7';
	}
}
