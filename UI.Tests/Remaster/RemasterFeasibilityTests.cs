using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.3 (PRD Part B §13.4, W-R0b; ADR-0243 Decision 6, ADR-0242 Q4): finding
	//the user's Python and the tools, once, without opening an installer.
	public class RemasterFeasibilityTests
	{
		private sealed class FakeProbe : IPythonProbe
		{
			private readonly Dictionary<string, string> _versions;
			public List<string> Asked { get; } = new();

			public FakeProbe(Dictionary<string, string> versions)
			{
				_versions = versions;
			}

			public string? Version(PythonCandidate candidate)
			{
				Asked.Add(candidate.Executable);
				return _versions.TryGetValue(candidate.Executable, out string? v) ? v : null;
			}
		}

		[Theory]
		[InlineData("3.10", true)]
		[InlineData("3.12", true)]
		[InlineData("4.0", true)]
		[InlineData("3.9", false)]
		[InlineData("2.7", false)]
		[InlineData("", false)]
		[InlineData("Python", false)]
		public void Python_3_10_is_the_oldest_that_runs_the_kit(string version, bool ok)
		{
			Assert.Equal(ok, PythonLocator.IsRecentEnough(version));
		}

		[Fact]
		public void On_macOS_the_install_locations_come_first_and_the_usr_bin_stub_is_never_asked()
		{
			IReadOnlyList<PythonCandidate> list = PythonLocator.Candidates("", "/usr/bin:/bin:/opt/local/bin", isWindows: false, isMacOS: true, fileExists: _ => true);
			string[] exes = list.Select(c => c.Executable).ToArray();

			Assert.Equal("/opt/homebrew/bin/python3", exes[0]);
			Assert.DoesNotContain("/usr/bin/python3", exes);
			Assert.Contains("/opt/local/bin/python3", exes);
			Assert.Equal("/Library/Developer/CommandLineTools/usr/bin/python3", exes[^1]);
		}

		[Fact]
		public void A_configured_python_is_tried_first_and_missing_files_are_skipped()
		{
			IReadOnlyList<PythonCandidate> list = PythonLocator.Candidates("/custom/python3", "", isWindows: false, isMacOS: true,
				fileExists: p => p == "/custom/python3" || p == "/usr/local/bin/python3");

			Assert.Equal(new[] { "/custom/python3", "/usr/local/bin/python3" }, list.Select(c => c.Executable).ToArray());
		}

		[Fact]
		public void On_Windows_the_store_alias_is_skipped_and_the_py_launcher_asks_for_3()
		{
			IReadOnlyList<PythonCandidate> list = PythonLocator.Candidates("", @"C:\Users\a\AppData\Local\Microsoft\WindowsApps;C:\Python312", isWindows: true, isMacOS: false, fileExists: _ => true);

			Assert.DoesNotContain(list, c => c.Executable.Contains("WindowsApps"));
			Assert.Contains(list, c => c.Executable == "py" && c.PrefixArgs.SequenceEqual(new[] { "-3" }));
		}

		[Fact]
		public void The_first_recent_enough_python_wins()
		{
			PythonCandidate[] candidates = { new("/a", Array.Empty<string>()), new("/b", Array.Empty<string>()), new("/c", Array.Empty<string>()) };
			FakeProbe probe = new(new() { ["/a"] = "3.9", ["/b"] = "3.12", ["/c"] = "3.13" });

			(PythonGate gate, PythonCandidate? found, string version) = PythonLocator.Locate(candidates, probe);

			Assert.Equal(PythonGate.Found, gate);
			Assert.Equal("/b", found!.Executable);
			Assert.Equal("3.12", version);
			Assert.Equal(new[] { "/a", "/b" }, probe.Asked);
		}

		[Fact]
		public void Only_an_old_python_reads_too_old_with_its_version_and_none_reads_missing()
		{
			PythonCandidate[] candidates = { new("/a", Array.Empty<string>()), new("/b", Array.Empty<string>()) };

			(PythonGate gate, _, string version) = PythonLocator.Locate(candidates, new FakeProbe(new() { ["/b"] = "3.9" }));
			Assert.Equal(PythonGate.TooOld, gate);
			Assert.Equal("3.9", version);

			Assert.Equal(PythonGate.Missing, PythonLocator.Locate(candidates, new FakeProbe(new())).Gate);
		}

		[Fact]
		public void The_tools_are_found_in_a_folder_or_its_scripts_folder()
		{
			string root = Path.Combine("x", "tools");
			string script = Path.Combine(root, "scripts", "mep_project.py");
			Assert.Equal(Path.Combine(root, "scripts"), RemasterToolsLocator.Resolve(root, p => p == script));
			Assert.Equal(Path.Combine(root, "scripts"), RemasterToolsLocator.Resolve(Path.Combine(root, "scripts"), p => p == script));
			Assert.Equal("", RemasterToolsLocator.Resolve(root, _ => false));
		}

		[Fact]
		public void Without_a_configured_folder_the_tools_are_searched_above_the_app_and_in_a_tools_zip_beside_it()
		{
			string repo = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));
			string app = Path.Combine(repo, "UI", "bin", "Release");
			string checkoutScript = Path.Combine(repo, "scripts", "mep_project.py");
			Assert.Equal(Path.Combine(repo, "scripts"), RemasterToolsLocator.Locate("", app, p => p == checkoutScript, _ => Array.Empty<string>()));

			string downloads = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Downloads"));
			string zipped = Path.Combine(downloads, "mesenai-tools-v1", "scripts", "mep_project.py");
			Assert.Equal(Path.Combine(downloads, "mesenai-tools-v1", "scripts"), RemasterToolsLocator.Locate("", Path.Combine(downloads, "Mesen.app", "Contents", "MacOS"),
				p => p == zipped, d => d == downloads ? new[] { Path.Combine(downloads, "mesenai-tools-v1"), Path.Combine(downloads, "other") } : Array.Empty<string>()));

			Assert.Equal("", RemasterToolsLocator.Locate("", app, _ => false, _ => Array.Empty<string>()));
		}
	}
}
