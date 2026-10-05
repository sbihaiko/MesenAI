using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mesen.Logic;

namespace Mesen.Utilities;

class DependencyHelper
{
	public static void ExtractNativeDependencies(string dest)
	{
		using(Stream? depStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mesen.Dependencies.zip")) {
			if(depStream == null) {
				throw new Exception("Missing dependencies.zip");
			}

#if DEBUG
			try {
				//In builds done via VS (without the "OptimizeUi" flag), the core is not embedded in the .exe (to improve build performance)
				//Copy it directly from the bin folder to the home folder
				string[] extensions = new string[] { ".dll", ".so", "dylib" };
				foreach(string ext in extensions) {
					string src = Path.Join(Program.OriginalFolder, "MesenCore" + ext);
					if(File.Exists(src)) {
						File.Copy(src, Path.Join(dest, "MesenCore" + ext), true);
					}
				}
			} catch { }
#endif

			//The per-member walk lives in UI/Logic so it is dual-compiled into
			//UI.Tests and can be run against a real archive (ADR-0123); what stays
			//here is the part that is host-aware - reaching the embedded resource
			//and the debug copy of the core.
			NativeDependencyExtractor.Extract(depStream, dest);
		}
	}

	public static string? GetFileContent(string filename)
	{
		using Stream? depStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mesen.Dependencies.zip");
		if(depStream != null) {
			using ZipArchive zip = new(depStream);
			foreach(ZipArchiveEntry entry in zip.Entries) {
				if(entry.Name == filename) {
					using Stream entryStream = entry.Open();
					using StreamReader reader = new StreamReader(entryStream);
					return reader.ReadToEnd();
				}
			}
		}
		return null;
	}
}
