using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//ADR-0250: every menu entry has one place per door, so a hint that names a
	//menu path has to name one the door it points at really has. A hint that
	//says "Tools ⋯" names no door at all - that menu is no longer the classic
	//one. Each row below states the door a hint points at; the label the hint
	//names is checked against WorkspaceMenu, the one rule that places entries.
	public class MenuPathHintTests
	{
		//Resource id -> the door the hint points at.
		private static readonly Dictionary<string, Workspace> HintDoors = new() {
			["lblPlayerSettingsEverythingElse"] = Workspace.Classic,
			["RemasterRecordFailed"] = Workspace.Remaster,
			["ShareReplayReasonMovieBusy"] = Workspace.Classic,
			["ShareReplayRefused"] = Workspace.Remaster,
			["ShareReplayEnded"] = Workspace.Classic,
			["PackLayerOffEverywhere"] = Workspace.Remaster,
			["ControllerSetupNoSlot"] = Workspace.Classic
		};

		//The switcher's door names (WorkspaceName* messages).
		private static string DoorName(Workspace door) => door switch {
			Workspace.Play => "Play",
			Workspace.Remaster => "Remaster",
			Workspace.Share => "Share",
			Workspace.Classic => "Classic",
			_ => throw new ArgumentOutOfRangeException(nameof(door))
		};

		//The labels the hints above use, and the entry each one names: a task
		//door's DoorMenu* messages carry the same text in every door, Classic's
		//are its enum text.
		private static readonly Dictionary<string, MenuEntry> EntryLabels = new() {
			["Enhancement Packs"] = MenuEntry.EnhancementPacks,
			["Log Window"] = MenuEntry.LogWindow,
			["Cheats"] = MenuEntry.Cheats,
			["Movies"] = MenuEntry.Movies,
			["Input"] = MenuEntry.InputSettings
		};

		//Classic's menu headers as its bar shows them: the mnu* controls, whose
		//leading underscore is the access key. Its Options menu reads "Settings".
		private static readonly Dictionary<string, ClassicMenu> ClassicHeaders = new() {
			["File"] = ClassicMenu.File, ["Game"] = ClassicMenu.Game, ["Settings"] = ClassicMenu.Options,
			["Tools"] = ClassicMenu.Tools, ["Debug"] = ClassicMenu.Debug, ["Help"] = ClassicMenu.Help,
			["Workspace"] = ClassicMenu.Workspace
		};

		//True when every segment of the path after the door name resolves: a
		//Classic path walks its menu bar ("Classic › Tools › Movies" - the menu
		//header, then an entry inside that menu), a task door's goes through
		//its Tools ⋯ ("Remaster ⋯ › Log Window").
		private static bool PathExists(Workspace door, string[] segments)
		{
			if(door == Workspace.Classic) {
				if(segments.Length < 2 || !ClassicHeaders.TryGetValue(segments[1].Trim(), out ClassicMenu menu)) {
					return false;
				}
				IReadOnlyList<MenuEntry> entries = WorkspaceMenu.ClassicMenuBar(false).First(m => m.Menu == menu).Entries;
				return segments.Skip(2).All(s => NamesEntry(entries, s));
			}
			if(segments.Length < 2 || !segments[0].Contains('⋯')) {
				return false;
			}
			IReadOnlyList<MenuEntry> tools = WorkspaceMenu.ToolsMenu(door, false, GameCapabilities.All).SelectMany(g => g).ToArray();
			return segments.Skip(1).All(s => NamesEntry(tools, s));
		}

		//The segment names one of those entries. It may run into the sentence
		//around it - "Log Window says which." - so a label is a prefix of it.
		private static bool NamesEntry(IReadOnlyList<MenuEntry> entries, string segment)
		{
			string label = segment.Trim();
			return EntryLabels.Any(l => label.StartsWith(l.Key, StringComparison.Ordinal) && entries.Contains(l.Value));
		}

		public static TheoryData<string, Workspace> Hints {
			get {
				TheoryData<string, Workspace> data = new();
				foreach(KeyValuePair<string, Workspace> hint in HintDoors) {
					data.Add(hint.Key, hint.Value);
				}
				return data;
			}
		}

		[Theory]
		[MemberData(nameof(Hints))]
		public void Hint_names_a_path_its_door_has(string id, Workspace door)
		{
			string text = ResourceTexts()[id];
			string name = DoorName(door);
			int at = text.IndexOf(name, StringComparison.Ordinal);
			Assert.True(at >= 0, $"{id} names no door: {text}");
			//The path runs from the door name on, as "<Door> ⋯ › <Entry>" or
			//"<Door> › <Menu> › <Entry>".
			Assert.True(PathExists(door, text[at..].Split('›')), $"{id} names no {name} path: {text}");
		}

		//A hint added without a door is the bug this file exists for: every
		//string that names a "⋯ ›" path has to be checked here, and every row
		//above has to name a string that still exists.
		[Fact]
		public void Every_menu_path_hint_is_checked()
		{
			Dictionary<string, string> texts = ResourceTexts();
			Assert.All(
				texts.Where(p => p.Value.Contains("⋯ ›")).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal),
				id => Assert.True(HintDoors.ContainsKey(id), $"{id} names a menu path and has no row in HintDoors"));
			Assert.All(
				HintDoors.Keys.OrderBy(k => k, StringComparer.Ordinal),
				id => Assert.True(texts.ContainsKey(id), $"{id} is not a resource id"));
		}

		//The Play cheats sheet's note (UI/Logic/CheatSheet.AllOffNote) is a code
		//constant, so it is checked here by hand: the master switch it names
		//lives in Classic's Cheat List window.
		[Fact]
		public void Cheats_all_off_note_names_the_classic_cheat_list()
		{
			Assert.Contains("Classic › Tools › Cheats", CheatSheet.AllOffNote, StringComparison.Ordinal);
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
