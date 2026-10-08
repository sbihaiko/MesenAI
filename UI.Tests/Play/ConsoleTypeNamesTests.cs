using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1081: the Enhancements sheet's disabled Overclock row names the console it
	//cannot overclock ("not available on Master System"), and had been printing
	//the raw id the resource helper falls back to - `not available on [[Sms]]` -
	//because a ConsoleType was never a name the locale file carried.
	//
	//A console's name is a string the player reads, so it comes from the locale
	//file like every other one (the RomConsoleKinds rule for the picker's rows);
	//ConsoleTypeNames names the id, the caller resolves it. These tests hold the
	//two ends: every member of ConsoleType is named, and every name the rule
	//hands out is a string that file actually defines - a console added without
	//one fails here instead of reaching the panel.
	public class ConsoleTypeNamesTests
	{
		[Fact]
		public void Every_console_type_is_named()
		{
			Dictionary<string, string> texts = ResourceTexts();
			foreach(ConsoleType console in Enum.GetValues<ConsoleType>()) {
				string id = ConsoleTypeNames.MessageId(console);
				Assert.False(string.IsNullOrWhiteSpace(id), console + " has no name in ConsoleTypeNames");
				Assert.True(texts.ContainsKey(id), console + " names " + id + ", which resources.en.xml does not define");
			}
		}

		//The id set is exactly the console set: a name left behind by a console
		//that no longer exists is as wrong as a console without one.
		[Fact]
		public void The_names_are_one_per_console()
		{
			string[] ids = Enum.GetValues<ConsoleType>().Select(ConsoleTypeNames.MessageId).ToArray();
			Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
		}

		//The bug's own console, and the one the whole row exists for: a name a
		//player reads, not the core's own spelling of the enum.
		[Fact]
		public void The_SMS_console_is_the_players_word_for_it()
		{
			Dictionary<string, string> texts = ResourceTexts();
			string name = texts[ConsoleTypeNames.MessageId(ConsoleType.Sms)];
			Assert.Equal("Master System", name);
			Assert.DoesNotContain("[", name);
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
