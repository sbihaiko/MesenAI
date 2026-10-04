using System;
using System.IO;
using Xunit;

namespace Mesen.HeadlessTests;

//#786: `make core` writes InteropDLL/obj.<rid>/, `make ui` writes
//bin/<rid>/<config>/. A leftover Release build under bin/ used to win over the
//build the checkout had just produced, so the headless tests silently exercised a
//stale core - and reported the result as a failing product.
//
//These cases need no native library, which is why they are plain [Fact]s: unlike
//the rest of this project they also run on the CI runner, where ADR-0131 leaves
//the core unbuilt and every NativeCore-dependent test self-skips.
public class NativeCoreLibraryResolutionTests
{
	[Fact]
	public void TheNewestBuildWins_NotTheLeftoverReleaseOne()
	{
		using TempRepo repo = new();
		repo.WriteLibrary("bin/osx-arm64/Release", age: TimeSpan.FromHours(1));
		string fresh = repo.WriteLibrary("InteropDLL/obj.osx-arm64", age: TimeSpan.Zero);

		Assert.Equal(fresh, NativeCore.FindBuiltLibrary(repo.Root));
	}

	[Fact]
	public void ANewerBinBuildWinsOverAnOlderObjectBuild()
	{
		//The rule is "the newest build wins", not "obj. always wins". A `make ui`
		//that runs after the last `make core` is the most recent answer to "what
		//does this checkout build", and it holds the same image anyway.
		using TempRepo repo = new();
		repo.WriteLibrary("InteropDLL/obj.osx-arm64", age: TimeSpan.FromHours(1));
		string fresh = repo.WriteLibrary("bin/osx-arm64/Release", age: TimeSpan.Zero);

		Assert.Equal(fresh, NativeCore.FindBuiltLibrary(repo.Root));
	}

	[Fact]
	public void NoBuildAnywhereResolvesToNothing()
	{
		using TempRepo repo = new();

		Assert.Null(NativeCore.FindBuiltLibrary(repo.Root));
	}

	private sealed class TempRepo : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "mesence-nativecore-" + Guid.NewGuid().ToString("N"));

		public TempRepo()
		{
			Directory.CreateDirectory(Root);
		}

		//`age` is how long ago the file was last written - the whole point of the
		//cases above. Hours apart, so no filesystem timestamp granularity is in play.
		public string WriteLibrary(string relativeDirectory, TimeSpan age)
		{
			string directory = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(directory);
			string file = Path.Combine(directory, NativeCore.LibraryFileName);
			File.WriteAllText(file, "");
			File.SetLastWriteTimeUtc(file, DateTime.UtcNow - age);
			return Path.GetFullPath(file);
		}

		public void Dispose()
		{
			try {
				Directory.Delete(Root, true);
			} catch(IOException) {
				//A temp directory left behind by the OS is not a test failure.
			}
		}
	}
}
