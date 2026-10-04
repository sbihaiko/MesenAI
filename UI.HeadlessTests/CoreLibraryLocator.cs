using System;
using System.Collections.Generic;
using System.IO;

namespace Mesen.HeadlessTests;

//#786: which MesenCore a headless run should load. It lives beside NativeCore
//rather than inside it on purpose: the rule is pure path selection, and a test
//class that *names* NativeCore has to join the serial collection (#432,
//NativeCoreCollectionGuardTests) - which would also stop it from running on the
//CI runner, where ADR-0131 leaves the core unbuilt and the serial tests
//self-skip. Nothing here touches the library; it only decides between files the
//checkout has already built.
internal static class CoreLibraryLocator
{
	//The RID sub-folder is whatever the local `make` produced; it is globbed rather
	//than guessed (RuntimeInformation.RuntimeIdentifier can carry an OS version,
	//e.g. osx.15-arm64, that the build folder never has).
	public static string FileName =>
		OperatingSystem.IsWindows() ? "MesenCore.dll" : (OperatingSystem.IsMacOS() ? "MesenCore.dylib" : "MesenCore.so");

	public static string? Find()
	{
		string fromEnv = Environment.GetEnvironmentVariable("MESEN_CORE_LIB") ?? "";
		if(fromEnv == "none") {
			//Escape hatch to reproduce the CI runner's "no native core" state on a
			//machine that does have one built.
			return null;
		}
		if(fromEnv.Length > 0 && File.Exists(fromEnv)) {
			return fromEnv;
		}

		string? repo = FindRepoRoot();
		return repo == null ? null : FindBuilt(repo);
	}

	public static string? FindBuilt(string repoRoot)
	{
		//#786: the newest build is the one this checkout just made, and the only one
		//that can answer for the current source. No fixed precedence can do this:
		//`make core` writes InteropDLL/obj.<rid>/ and `make ui` writes
		//bin/<rid>/<config>/, so either order is wrong half the time - and a leftover
		//Release build under bin/ made it wrong for the rest of the checkout's life.
		List<string> candidates = new();
		foreach(string dir in EnumerateDirectories(Path.Combine(repoRoot, "bin"))) {
			foreach(string config in new[] { "Release", "Debug" }) {
				string candidate = Path.Combine(dir, config, FileName);
				if(File.Exists(candidate)) {
					candidates.Add(candidate);
				}
			}
		}
		foreach(string dir in EnumerateDirectories(Path.Combine(repoRoot, "InteropDLL"))) {
			if(!Path.GetFileName(dir).StartsWith("obj.", StringComparison.Ordinal)) {
				continue;
			}
			string candidate = Path.Combine(dir, FileName);
			if(File.Exists(candidate)) {
				candidates.Add(candidate);
			}
		}

		//Sorted first so an exact tie (two copies of one image) resolves the same way
		//on every run, instead of following the filesystem's directory order.
		candidates.Sort(StringComparer.Ordinal);
		string? newest = null;
		DateTime newestTime = DateTime.MinValue;
		foreach(string candidate in candidates) {
			DateTime written = File.GetLastWriteTimeUtc(candidate);
			if(newest == null || written > newestTime) {
				newest = candidate;
				newestTime = written;
			}
		}
		return newest;
	}

	private static string[] EnumerateDirectories(string path)
	{
		return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
	}

	private static string? FindRepoRoot()
	{
		DirectoryInfo? dir = new(AppContext.BaseDirectory);
		while(dir != null) {
			if(File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				return dir.FullName;
			}
			dir = dir.Parent;
		}
		return null;
	}
}
