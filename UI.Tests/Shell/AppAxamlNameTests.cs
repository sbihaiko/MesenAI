using System;
using System.IO;
using System.Xml.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//Issue #1018: MacAppMenu.AppName is read by no production code, and App.axaml
	//repeats the literal. Avalonia builds "Hide <Name>" from Application.Name, so
	//the two must agree; this reads the real UI/App.axaml and pins them together.
	public class AppAxamlNameTests
	{
		[Fact]
		public void App_axaml_Name_equals_the_app_menu_name()
		{
			XDocument doc = XDocument.Load(Path.Combine(FindRepoRoot(), "UI", "App.axaml"));

			Assert.Equal(MacAppMenu.AppName, (string?)doc.Root!.Attribute("Name"));
		}

		private static string FindRepoRoot()
		{
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			if(dir == null) {
				throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
			}
			return dir.FullName;
		}
	}
}
