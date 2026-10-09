using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1108 (spec #1102): the remaining Play sheets render their footer from the
	//shared bar, and Library folders… names the action the ring is on - Add or
	//Remove - instead of the Browser's A Select (found in #1120's review).
	public class PlayBarSheetDeclarationsTests
	{
		private static string Text(System.Collections.Generic.IReadOnlyList<PlayBarEntry> declared)
			=> PlayActionBar.Text(declared, PlayInputDevice.Controller, PadFamily.Xbox, false, key => key);

		[Theory]
		[InlineData("RomPickerAddFolder", "A BarAddFolder     B BarBack")]
		[InlineData("RomPickerFolderRemove", "A BarRemoveFolder     B BarBack")]
		[InlineData("RomPickerBack", "A BarBack")]
		public void The_folders_sheet_names_the_action_the_ring_is_on(string focused, string expected)
		{
			Assert.Equal(expected, Text(PlayBarDeclarations.LibraryFoldersSheet(focused)));
		}

		[Fact]
		public void The_folders_sheet_keeps_a_Back_when_nothing_it_names_has_the_focus()
		{
			Assert.Contains(PlayBarDeclarations.LibraryFoldersSheet(null), e => e.Action == PlayAction.Back);
		}

		[Fact]
		public void The_generic_sheet_declares_Select_and_Back()
		{
			Assert.Equal("A BarSelect     B BarBack", Text(PlayBarDeclarations.Sheet));
		}
	}
}
