using System;
using System.IO;
using System.IO.Compression;

namespace Mesen.Logic;

//Extracting the native libraries the app ships inside Dependencies.zip into the
//per-user home folder. Host-free (ADR-0123): a zip stream and a folder, nothing
//else, so UI.Tests runs it against a real archive and a temp folder
//(UI.Tests/Utilities/NativeDependencyExtractorTests) instead of the machine.
//
//The rule this file exists to hold is that an extracted library is never
//rewritten in place (ADR-0259). Two installs that share one home folder - a development
//build and the installed one ship different cores, so each re-extracts on
//launch - and by then the same library is mapped by the instance that is already
//running. Rewriting the bytes under a mapped Mach-O kills a process
//(measured 2026-10-05: SIGKILL, termination namespace CODESIGNING with indicator
//"Invalid Page", faulting thread in dyld dlopen_from). That is issue #628's
//failure mode, which the build side already answers by writing a new inode -
//scripts/replace_file_atomic.sh - and this is the runtime side of the same rule.
public static class NativeDependencyExtractor
{
	public static void Extract(Stream zipStream, string destFolder)
	{
		using ZipArchive zip = new(zipStream);
		foreach(ZipArchiveEntry entry in zip.Entries) {
			ExtractEntry(entry, destFolder);
		}
	}

	//One member, and one member's failure. The archive carries native libraries
	//for several platforms plus Satellaview data; a member that cannot be read or
	//written must not stop the others, which is why every failure is swallowed
	//here rather than thrown at the caller - the app would not start at all.
	private static void ExtractEntry(ZipArchiveEntry entry, string destFolder)
	{
		try {
			if(entry.FullName.StartsWith("Internal")) {
				return;
			}

			string path = Path.Combine(destFolder, entry.FullName);
			entry.ExternalAttributes = 0;
			if(File.Exists(path)) {
				if(Path.GetExtension(path)?.ToLower() == ".bin") {
					//Don't overwrite BS-X bin files if they already exist on the disk
					return;
				}

				FileInfo fileInfo = new(path);
				if(fileInfo.LastWriteTime == entry.LastWriteTime && fileInfo.Length == entry.Length) {
					return;
				}
			} else {
				string? folderName = Path.GetDirectoryName(path);
				if(folderName != null && !Directory.Exists(folderName)) {
					//Create any missing directory (e.g Satellaview)
					Directory.CreateDirectory(folderName);
				}
			}

			WriteAtomically(entry, path);
		} catch {

		}
	}

	//A library is replaced through a NEW file, never by editing the one that is
	//already there. The instance that is running has it mapped, and a write to the
	//inode under a mapped image is what kills that process - so the new content
	//goes to a sibling temp file and is moved into place, which is the same rule
	//scripts/replace_file_atomic.sh applies on the build side (issue #628).
	//
	//It is also the only form that works at all while the old library is open:
	//.NET opens an in-place destination with FileShare.None, so that write fails
	//and the per-member catch above swallows it, leaving a stale core.
	//
	//The temp file is a sibling (so the move stays inside one filesystem) and its
	//name cannot be mistaken for a library - the core is found by scanning beside
	//the executable. A write that fails leaves the old library untouched and takes
	//its own temp file with it.
	private static void WriteAtomically(ZipArchiveEntry entry, string path)
	{
		ReplaceAtomically(path, temp => {
			using(FileStream target = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
				using Stream source = entry.Open();
				source.CopyTo(target);
			}
			//The member's own timestamp, which is what the guard above compares
			//against: without it every launch would look like a change and unpack
			//the whole archive again. ExtractToFile did this and the replacement
			//has to keep doing it.
			File.SetLastWriteTime(temp, entry.LastWriteTime.LocalDateTime);
		});
	}

	//The same rule for a caller whose new content is already a file on disk - the
	//debug build copies the core out of the bin folder rather than unpacking it.
	//Opening the destination would put it straight back on the inode a running
	//instance has mapped, which is the whole thing this class exists to avoid.
	public static void ReplaceFromFile(string sourcePath, string destinationPath)
	{
		ReplaceAtomically(destinationPath, temp => File.Copy(sourcePath, temp, true));
	}

	//Fill `path` with new content without ever opening the file that is there.
	//`fill` writes a sibling of the destination, so the move that follows stays
	//inside one filesystem, and it is the one that leaves the timestamp the
	//caller needs. A `fill` that throws leaves the destination exactly as it was.
	private static void ReplaceAtomically(string path, Action<string> fill)
	{
		string temp = path + ".new-" + Guid.NewGuid().ToString("N");
		try {
			fill(temp);
			File.Move(temp, path, true);
		} finally {
			if(File.Exists(temp)) {
				File.Delete(temp);
			}
		}
	}
}
