using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1038 review finding 2 (ADR-0264 Decisions 4 and 7): the cleaner runs over
	//the canonical title "before either is displayed or searched". The title the
	//grid shows is the title the search box asks, so a game whose file is called
	//`zap.nes` and whose database title is *Zelda II - The Adventure of Link* is
	//found by `zelda`. Host-free: pure text and entries, no Avalonia (ADR-0123).
	public class LibraryTitleBookTests
	{
		private static LibraryEntry Entry(string file, string cleaned)
		{
			return new LibraryEntry("/games/" + file, RomConsole.Nes, cleaned);
		}

		[Fact]
		public void Search_matches_the_canonical_title_and_not_the_file_name()
		{
			LibraryTitleBook book = new();
			LibraryEntry zap = Entry("zap.nes", "zap");
			LibraryEntry other = Entry("metroid.nes", "Metroid");
			book.Resolve(zap.Path, "Zelda II - The Adventure of Link");

			Assert.Equal("Zelda II - The Adventure of Link", book.TitleOf(zap));
			Assert.Equal(new[] { zap }, book.Search(new[] { zap, other }, e => e, "zelda").ToArray());
			Assert.Empty(book.Search(new[] { zap, other }, e => e, "zap"));
		}

		[Fact]
		public void An_unresolved_entry_is_searched_by_its_cleaned_file_name()
		{
			LibraryTitleBook book = new();
			LibraryEntry other = Entry("metroid.nes", "Metroid");

			Assert.Equal("Metroid", book.TitleOf(other));
			Assert.Equal(new[] { other }, book.Search(new[] { other }, e => e, "metro").ToArray());
		}
	}
}
