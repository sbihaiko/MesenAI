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
//rewritten in place. Two installs that share one home folder - a development
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

			entry.ExtractToFile(path, true);
		} catch {

		}
	}
}
