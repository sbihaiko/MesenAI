using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//#887: the notice the picker shows when the player designates a folder that
	//answers nothing. Found by the third review of PR #894, which traced the
	//sentence rather than the rule.
	//
	//The notice must not promise where the app opens instead, because the picker -
	//the surface that shows it - never falls back anywhere: `Open` always opens on
	//the roots, and a folder that answers nothing is simply not made a root. And
	//the one fallback the app does have, `GamesFolderChoice.StartFolder` in
	//`Open ROM`, is held to the same rule as the setting: when the last game's
	//folder answers nothing too - the ROM was on a stick that is not plugged in -
	//it returns nowhere in particular, which the dialog reads as its own default.
	//
	//The first version of the string claimed "MesenAI will keep opening where you
	//last played", and both halves of that were untrue. What the notice can say
	//truthfully on all four entry points is what it did: the folder is kept, and
	//it is not the one in use.
	public class GamesFolderNoticeTests
	{
		//The defect, and the assertion that fails against the old string.
		[Fact]
		public void The_empty_games_folder_notice_does_not_promise_a_fallback_location()
		{
			string notice = ResourceTexts()["RomPickerGamesFolderEmpty"];
			Assert.DoesNotContain("played", notice, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("last game", notice, StringComparison.OrdinalIgnoreCase);
		}

		//...and it is still a sentence about the folder being empty, not a blank and
		//not the plain "Saved" the other outcome gets. Without this the assertion
		//above would pass on a string that says nothing at all.
		[Fact]
		public void The_empty_games_folder_notice_is_still_about_the_empty_folder()
		{
			Dictionary<string, string> texts = ResourceTexts();
			string notice = texts["RomPickerGamesFolderEmpty"];
			Assert.False(string.IsNullOrWhiteSpace(notice));
			Assert.NotEqual(texts["RomPickerGamesFolderSaved"], notice);
			Assert.Contains("no games", notice, StringComparison.OrdinalIgnoreCase);
		}

		private static Dictionary<string, string> ResourceTexts()
		{
			XDocument doc = XDocument.Load(Path.Combine(FindRepoRoot(), "UI", "Localization", "resources.en.xml"));
			Dictionary<string, string> texts = new();
			foreach(XElement node in doc.Descendants().Where(n => n.Name.LocalName is "Message" or "Control")) {
				string? id = node.Attribute("ID")?.Value;
				if(id != null) {
					texts.TryAdd(id, node.Value);
				}
			}
			return texts;
		}

		private static string FindRepoRoot()
		{
			DirectoryInfo? dir = new(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}
	}
}
