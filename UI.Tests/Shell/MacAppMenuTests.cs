using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//Issue #1008 (PRD §13.5 W-S2, ADR-0250 Decisions 3 and 4): the macOS app
	//menu is titled MesenAI and follows the HIG order — About first, Settings…
	//near the top, Quit last. Avalonia appends its standard items (Services,
	//Hide, Hide Others, Show All, Quit) to the same menu, before or after our
	//own items depending on when the menu is first exported.
	public class MacAppMenuTests
	{
		private const string Sep = "-";
		private const string About = "About MesenAI";
		private const string Settings = "Settings…";

		private static readonly string[] AvaloniaStandardItems = {
			Sep, "Services", Sep, "Hide MesenAI", "Hide Others", "Show All", Sep, "Quit"
		};

		private static List<string> Arrange(IEnumerable<string> current)
		{
			return MacAppMenu.Arrange(current, About, Settings, () => Sep, s => s == Sep).ToList();
		}

		[Fact]
		public void The_app_menu_is_named_after_the_product()
		{
			Assert.Equal("MesenAI", MacAppMenu.AppName);
		}

		[Fact]
		public void About_and_Settings_come_before_the_standard_items_and_Quit_is_last()
		{
			List<string> items = Arrange(AvaloniaStandardItems);

			Assert.Equal(new[] {
				About, Sep, Settings, Sep, "Services", Sep, "Hide MesenAI", "Hide Others", "Show All", Sep, "Quit"
			}, items);
		}

		[Fact]
		public void Our_items_appended_after_Quit_move_to_the_top()
		{
			//The order observed on a real display in the #926 validation.
			List<string> items = Arrange(AvaloniaStandardItems.Concat(new[] { About, Sep, Settings }));

			Assert.Equal(About, items.First());
			Assert.Equal("Quit", items.Last());
			Assert.Equal(Settings, items[2]);
			Assert.Single(items, About);
			Assert.Single(items, Settings);
		}

		[Fact]
		public void Before_Avalonia_adds_its_standard_items_the_menu_holds_only_ours()
		{
			Assert.Equal(new[] { About, Sep, Settings }, Arrange(new string[0]));
		}

		[Fact]
		public void Arranging_twice_changes_nothing()
		{
			List<string> once = Arrange(AvaloniaStandardItems);

			Assert.Equal(once, Arrange(once));
		}
	}
}
