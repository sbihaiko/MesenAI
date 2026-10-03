using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Mesen.Tests.Theme
{
	//ADR-0249 Decision 2: the Player palette is transcribed once from
	//scripts/render_gui_wireframes.py into UI/Styles/PlayerTheme.axaml. This
	//test reads both files and fails when either moves without the other, and
	//when the script grows a palette constant the theme does not carry.
	public class PlayerThemeDriftTests
	{
		//Script constant (or TINT/TINT_TEXT key) -> PlayerTheme.axaml Color key.
		private static readonly Dictionary<string, string> Mapping = new() {
			["TEXT"] = "PlayerTextColor",
			["TEXT2"] = "PlayerText2Color",
			["TEXT3"] = "PlayerText3Color",
			["SEP"] = "PlayerSeparatorColor",
			["WINBG"] = "PlayerWindowBackgroundColor",
			["CARD"] = "PlayerCardColor",
			["FILL"] = "PlayerFillColor",
			["RED"] = "PlayerRedColor",
			["ORANGE"] = "PlayerOrangeColor",
			["TINT.play"] = "PlayerTintPlayColor",
			["TINT.remaster"] = "PlayerTintRemasterColor",
			["TINT.share"] = "PlayerTintShareColor",
			["TINT_TEXT.play"] = "PlayerTintTextPlayColor",
			["TINT_TEXT.remaster"] = "PlayerTintTextRemasterColor",
			["TINT_TEXT.share"] = "PlayerTintTextShareColor",
		};

		private static readonly Regex Constant = new(@"^([A-Z][A-Z0-9_]*)\s*=\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Multiline);
		private static readonly Regex DictConstant = new(@"^(TINT|TINT_TEXT)\s*=\s*\{([^}]*)\}", RegexOptions.Multiline);
		private static readonly Regex DictEntry = new(@"""(\w+)""\s*:\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
		private static readonly Regex AxamlColor = new(@"<Color\s+x:Key=""(\w+)"">\s*#([0-9A-Fa-f]{6,8})\s*</Color>");

		[Fact]
		public void Every_script_palette_value_matches_the_theme()
		{
			Dictionary<string, string> script = ReadScriptPalette();
			Dictionary<string, string> theme = ReadThemeColors();

			List<string> drift = new();
			foreach((string name, string hex) in script) {
				if(!Mapping.TryGetValue(name, out string? key)) {
					drift.Add($"{name} ({hex}) is a palette constant with no PlayerTheme.axaml key - add it to the theme and to this test's mapping");
				} else if(!theme.TryGetValue(key, out string? themeHex)) {
					drift.Add($"{name} -> {key} is missing from PlayerTheme.axaml (script #{hex})");
				} else if(!string.Equals(themeHex, hex, StringComparison.OrdinalIgnoreCase)) {
					drift.Add($"{name} -> {key}: script #{hex}, theme #{themeHex}");
				}
			}
			Assert.True(drift.Count == 0, string.Join(Environment.NewLine, drift));
		}

		[Fact]
		public void The_script_still_defines_every_mapped_constant()
		{
			Dictionary<string, string> script = ReadScriptPalette();
			string[] gone = Mapping.Keys.Where(k => !script.ContainsKey(k)).ToArray();
			Assert.True(gone.Length == 0, "mapped constants no longer in the script: " + string.Join(", ", gone));
		}

		private static Dictionary<string, string> ReadScriptPalette()
		{
			string text = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "render_gui_wireframes.py"));
			//Only the palette block: the constants between "# Palette" and NAME.
			int start = text.IndexOf("# Palette", StringComparison.Ordinal);
			int end = text.IndexOf("\nNAME", start, StringComparison.Ordinal);
			Assert.True(start >= 0 && end > start, "render_gui_wireframes.py has no '# Palette' block ending before NAME");
			string block = text.Substring(start, end - start);

			Dictionary<string, string> palette = new();
			foreach(Match m in Constant.Matches(block)) {
				palette[m.Groups[1].Value] = Hex(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
			}
			foreach(Match m in DictConstant.Matches(block)) {
				foreach(Match e in DictEntry.Matches(m.Groups[2].Value)) {
					palette[m.Groups[1].Value + "." + e.Groups[1].Value] = Hex(e.Groups[2].Value, e.Groups[3].Value, e.Groups[4].Value);
				}
			}
			Assert.NotEmpty(palette);
			return palette;
		}

		private static Dictionary<string, string> ReadThemeColors()
		{
			string path = Path.Combine(FindRepoRoot(), "UI", "Styles", "PlayerTheme.axaml");
			Assert.True(File.Exists(path), path + " does not exist");
			Dictionary<string, string> colors = new();
			foreach(Match m in AxamlColor.Matches(File.ReadAllText(path))) {
				string hex = m.Groups[2].Value;
				//#AARRGGBB: an opaque token is compared as RRGGBB.
				colors[m.Groups[1].Value] = hex.Length == 8 && hex.StartsWith("FF", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
			}
			return colors;
		}

		private static string Hex(string r, string g, string b) => $"{int.Parse(r):X2}{int.Parse(g):X2}{int.Parse(b):X2}";

		//UI.Tests runs from bin/<config>/net10.0/ inside whichever checkout built it.
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
