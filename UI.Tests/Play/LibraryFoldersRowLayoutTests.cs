using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1079: the *Library folders…* list (#1036, ADR-0264 Decision 8) is the
	//sheet's own inset list of the folders "Your library" scans, and the rules it
	//has to keep are the sheet's (PlayerReplaysSheetView's list of the same
	//shape): each row inset from the box, the path on the left and its *Remove*
	//press on the right, a hairline between rows and none on the last, the box
	//the height of its rows rather than the sheet's, and the *Add a folder…*
	//press on the sheet's own chrome with the app's focus ring.
	//
	//Host-free (ADR-0123): the sheet is read as XAML and the rules off the theme.
	//Nothing here opens a window, and the measured outcome - rows inside the box,
	//no gap above the first - is asserted in
	//UI.HeadlessTests/PlayerLibraryFoldersRowLayoutTests.
	public class LibraryFoldersRowLayoutTests
	{
		//The row the theme draws for an inset list: 46 high, a hairline from the
		//label to the right edge, none on the last row.
		private const string InsetRowSelector = ".player Border.switch-row";
		private const string LastRowSelector = ".player Border.switch-row:nth-last-child(1), .player ContentPresenter:nth-last-child(1) > Border.switch-row";

		//A row is inset from the box it sits in, and the press at its right end is
		//inset too - the two things the issue saw flush against the edges. The
		//theme's row carries the left inset; the row's own DockPanel carries the
		//right one, which is what keeps *Remove* off the container's edge.
		private const int MinLeftInset = 12;
		private const int MinRightInset = 12;

		[Fact]
		public void Each_row_is_the_themes_inset_row()
		{
			XElement row = RowTemplateRoot();
			Assert.Equal("Border", row.Name.LocalName);
			Assert.Contains("switch-row", ClassesOf(row));

			//And the class means something: the row is inset, it carries a
			//separator, and the last row's separator is dropped.
			XElement style = RequireStyle(InsetRowSelector);
			Assert.Equal("0 0 0 1", Setter(style, "BorderThickness"));
			Assert.True(int.TryParse(Setter(style, "MinHeight"), out int minHeight) && minHeight >= 32,
				$"{InsetRowSelector} has no row height: MinHeight={Setter(style, "MinHeight")}");
			Assert.Equal("0", Setter(RequireStyle(LastRowSelector), "BorderThickness"));
		}

		[Fact]
		public void The_row_insets_its_path_and_its_remove_press()
		{
			XElement row = RowTemplateRoot();
			XElement content = row.Elements().First();
			Assert.Equal("DockPanel", content.Name.LocalName);

			//Left: the theme's row margin. Right: this DockPanel's own margin, so
			//the *Remove* press ends inside the box rather than on its edge.
			Thickness row_margin = ThicknessOf(Setter(RequireStyle(InsetRowSelector), "Margin"));
			Assert.True(row_margin.Left >= MinLeftInset, $"the row is flush against the box's left edge: Margin={row_margin}");

			Thickness content_margin = ThicknessOf((string?)content.Attribute("Margin"));
			Assert.True(content_margin.Right >= MinRightInset, $"the press at the row's right end is flush against the box's right edge: Margin={content_margin}");
			Assert.True(content_margin.Top > 0 && content_margin.Bottom > 0, $"the row has no breathing room above or below its label: Margin={content_margin}");

			//Right end: the press, docked. Fill: the path.
			XElement remove = content.Elements().First(e => e.Name.LocalName == "Button");
			Assert.Equal("RomPickerFolderRemove", (string?)remove.Attribute("Name"));
			Assert.Equal("Right", (string?)remove.Attribute("DockPanel.Dock"));
			XElement label = content.Elements().Last();
			Assert.Equal("TextBlock", label.Name.LocalName);
			Assert.Equal("{Binding Label}", (string?)label.Attribute("Text"));
		}

		//The gap above the first row (#1079): the sheet laid the list out as the
		//fill child of a DockPanel, so the box took the whole remaining height and
		//the capped list floated in the middle of it. A row-tall box needs a
		//container that hands its children their own height, and the sheet itself
		//is top-aligned so the space it does not need stays below it.
		[Fact]
		public void The_list_box_sizes_to_its_rows()
		{
			XElement sheet = RequireSheet();
			Assert.Equal("StackPanel", sheet.Name.LocalName);
			Assert.Equal("Top", (string?)sheet.Attribute("VerticalAlignment"));

			//The box and the list are one on top of the other, so the list's own
			//max height is what the box measures: 300 is the sheet's cap, and the
			//box is exactly that tall when the rows fill it.
			XElement box = ListBox();
			Assert.Equal("Panel", box.Name.LocalName);
			Assert.Contains(box.Elements(), e => e.Name.LocalName == "Border" && ClassesOf(e).Contains("inset"));
			XElement scroller = box.Elements().First(e => e.Name.LocalName == "ScrollViewer");
			Assert.Equal("300", (string?)scroller.Attribute("MaxHeight"));
			Assert.Contains(scroller.Descendants(), e => (string?)e.Attribute("Name") == "RomPickerFoldersList");
		}

		//The *Add a folder…* press (#1079): the sheet's own footer chrome, and the
		//ring the theme draws for every Play button. Nothing in this view may
		//redraw it - the heavy border the issue saw was a focus look of its own.
		[Fact]
		public void The_add_press_keeps_the_sheets_own_chrome()
		{
			XElement add = RequireSheet().Descendants().First(e => (string?)e.Attribute("Name") == "RomPickerAddFolder");
			Assert.Equal("Button", add.Name.LocalName);
			Assert.Contains("secondary", ClassesOf(add));
			Assert.Contains("regular", ClassesOf(add));
			Assert.Null(add.Attribute("BorderThickness"));
			Assert.Null(add.Attribute("BorderBrush"));
			Assert.Null(add.Attribute("FocusAdorner"));

			//The ring is the theme's, on the layer every Play button template
			//carries, and it is the only border focus adds: the button draws the
			//theme's 1px hairline and nothing heavier on top of it.
			Assert.Equal("{StaticResource PlayerFocusRing}", Setter(RequireStyle(".player Button:focus-visible /template/ Border#PART_Background"), "BoxShadow"));
			Assert.Equal("1", Setter(RequireStyle(".player Button.secondary"), "BorderThickness"));
			Assert.DoesNotContain(add.Attributes(), a => a.Name.LocalName.Contains("Focus"));
		}

		private static XElement RowTemplateRoot()
		{
			XElement list = RequireSheet().Descendants().First(e => (string?)e.Attribute("Name") == "RomPickerFoldersList");
			XElement template = list.Descendants().First(e => e.Name.LocalName == "DataTemplate");
			return template.Elements().First();
		}

		//The box the list is drawn in: the Border.inset and the list, one over the
		//other, so the box can be behind the list without the list being inside the
		//rounded edge (PlayerReplaysSheetView).
		private static XElement ListBox()
		{
			XElement scroller = RequireSheet().Descendants().First(e => e.Name.LocalName == "ScrollViewer");
			return scroller.Parent!;
		}

		private static XElement RequireSheet()
		{
			return Sheet().Descendants().First(e => (string?)e.Attribute("Name") == "RomPickerFoldersSheet");
		}

		private static XDocument Sheet() =>
			XDocument.Load(Path.Combine(Root(), "UI", "Views", "PlayerRomPickerView.axaml"));

		private static XElement RequireStyle(string selector)
		{
			XDocument theme = XDocument.Load(Path.Combine(Root(), "UI", "Styles", "PlayerTheme.axaml"));
			XElement? style = theme.Descendants().FirstOrDefault(e => e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == selector);
			if(style == null) {
				throw new Xunit.Sdk.XunitException($"PlayerTheme.axaml has no <Style Selector=\"{selector}\">");
			}
			return style;
		}

		private static string? Setter(XElement style, string property) =>
			(string?)style.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == property)?.Attribute("Value");

		private static string ClassesOf(XElement element) => (string?)element.Attribute("Classes") ?? "";

		private static Thickness ThicknessOf(string? value)
		{
			string[] parts = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return new Thickness(
				parts.Length > 0 ? int.Parse(parts[0]) : 0,
				parts.Length > 1 ? int.Parse(parts[1]) : 0,
				parts.Length > 2 ? int.Parse(parts[2]) : 0,
				parts.Length > 3 ? int.Parse(parts[3]) : 0);
		}

		private readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
		{
			public override string ToString() => $"{Left} {Top} {Right} {Bottom}";
		}

		private static string Root()
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
