using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Config
{
	//"Estilizar o HUD do Core" (user's decision, 2026-10-03): the Core draws its
	//system toasts as W-P3's rounded card in Player mode and keeps the classic
	//outlined text everywhere else. The UI tells the Core which through
	//PreferencesConfig.ToastStyle, derived from UiMode.
	public class HudToastStyleRuleTests
	{
		[Fact]
		public void PlayerMode_DrawsThePlayerCard()
		{
			Assert.Equal(HudToastStyle.Player, HudToastStyleRule.For(UiMode.Player));
		}

		[Fact]
		public void AdvancedMode_KeepsTheClassicToast()
		{
			Assert.Equal(HudToastStyle.Classic, HudToastStyleRule.For(UiMode.Advanced));
		}

		[Fact]
		public void EnumValues_MatchTheCoreMirror()
		{
			string header = File.ReadAllText(Path.Combine(FindRepoRoot(), "Core", "Shared", "SettingTypes.h"));
			Match m = Regex.Match(header, @"enum class HudToastStyle\s*\{(?<body>[^}]*)\}");
			Assert.True(m.Success, "SettingTypes.h has no `enum class HudToastStyle`");
			string[] names = m.Groups["body"].Value.Split(',')
				.Select(s => Regex.Replace(s, @"//.*", "").Trim())
				.Where(s => s.Length > 0)
				.Select(s => s.Split('=')[0].Trim())
				.ToArray();
			Assert.Equal(Enum.GetNames(typeof(HudToastStyle)), names);
		}

		[Fact]
		public void InteropStruct_PlacesToastStyleRightAfterHudSize_InBothLanguages()
		{
			//InteropPreferencesConfig is marshalled by value into the Core's
			//PreferencesConfig, so the field must sit at the same position.
			string root = FindRepoRoot();
			string header = File.ReadAllText(Path.Combine(root, "Core", "Shared", "SettingTypes.h"));
			string cs = File.ReadAllText(Path.Combine(root, "UI", "Config", "PreferencesConfig.cs"));
			Assert.Matches(new Regex(@"HudDisplaySize HudSize = HudDisplaySize::Fixed;\s*HudToastStyle ToastStyle = HudToastStyle::Classic;"), header);
			Assert.Matches(new Regex(@"public HudDisplaySize HudSize;\s*public HudToastStyle ToastStyle;"), cs);
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
