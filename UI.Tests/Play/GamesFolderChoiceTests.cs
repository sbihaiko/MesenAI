using System;
using System.IO;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#887: a games folder that answers nothing must not be where the app opens.
	//Measured on the requesting machine: macOS `/home` is an autofs node - `isdir`
	//true, `listdir` empty - so a games folder left pointing at it (which the
	//picker's own "make this my games folder" action will do from the This Mac
	//root) starts Open ROM on a folder with nothing in it and leads the picker's
	//roots list with the same dead end. Every guard on the path asked
	//`Directory.Exists`, which is true for `/home`.
	//
	//Real temp folders rather than an injected predicate, which is why the rule
	//lives in UI/Logic (ADR-0123).
	public class GamesFolderChoiceTests
	{
		private static string NewTempDir()
		{
			string dir = Path.Combine(Path.GetTempPath(), "mesen-gamesfolder-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static string NewTempDirWithAGame()
		{
			string dir = NewTempDir();
			File.WriteAllText(Path.Combine(dir, "contra.nes"), "");
			return dir;
		}

		[Fact]
		public void A_folder_with_a_game_in_it_has_entries()
		{
			string dir = NewTempDirWithAGame();
			try {
				Assert.True(GamesFolderChoice.HasEntries(dir));
				Assert.Equal(dir, GamesFolderChoice.Usable(dir));
			} finally {
				Directory.Delete(dir, true);
			}
		}

		//The defect. The folder is there and `Directory.Exists` says so; there is
		//simply nothing in it, which is `/home`.
		[Fact]
		public void An_empty_folder_has_no_entries()
		{
			string dir = NewTempDir();
			try {
				Assert.False(GamesFolderChoice.HasEntries(dir));
				Assert.Null(GamesFolderChoice.Usable(dir));
			} finally {
				Directory.Delete(dir, true);
			}
		}

		//A path that is not a folder at all answers the same way, and so does no
		//path: nothing was designated.
		[Fact]
		public void A_missing_folder_and_no_folder_at_all_are_the_same_answer()
		{
			string dir = NewTempDir();
			try {
				Assert.False(GamesFolderChoice.HasEntries(Path.Combine(dir, "nope")));
				Assert.Null(GamesFolderChoice.Usable(Path.Combine(dir, "nope")));
				Assert.Null(GamesFolderChoice.Usable(null));
				Assert.Null(GamesFolderChoice.Usable(""));
				Assert.Null(GamesFolderChoice.Usable("   "));
			} finally {
				Directory.Delete(dir, true);
			}
		}

		//...and the app goes where it would have gone had the folder never been
		//designated, which is the last game the player opened.
		[Fact]
		public void The_last_opened_folder_stands_in_for_a_games_folder_that_answers_nothing()
		{
			string empty = NewTempDir();
			string recent = NewTempDirWithAGame();
			try {
				Assert.Equal(recent, GamesFolderChoice.StartFolder(empty, recent));
				Assert.Equal(recent, GamesFolderChoice.StartFolder(null, recent));
				Assert.Null(GamesFolderChoice.StartFolder(null, null));
				Assert.Null(GamesFolderChoice.StartFolder(empty, null));
			} finally {
				Directory.Delete(empty, true);
				Directory.Delete(recent, true);
			}
		}

		//The fallback is held to the same rule, and it has to be: the last game's
		//folder answers nothing as well when the ROM was on a stick that is not
		//plugged in, and a fallback that re-creates the dead end it exists to avoid
		//is not a fallback. Nowhere in particular is the caller's to decide.
		[Fact]
		public void A_last_opened_folder_that_answers_nothing_is_not_a_fallback()
		{
			string empty = NewTempDir();
			string alsoEmpty = NewTempDir();
			try {
				Assert.Null(GamesFolderChoice.StartFolder(empty, alsoEmpty));
				Assert.Null(GamesFolderChoice.StartFolder(null, Path.Combine(empty, "gone")));
			} finally {
				Directory.Delete(empty, true);
				Directory.Delete(alsoEmpty, true);
			}
		}

		//A library laid out one folder per console is a games folder with no game
		//file in it at all, and it must count as answering: the picker's whole point
		//is that descending into it is how the player reaches the games. This is
		//also what separates "has entries" from a `GetFiles()` that only sees files.
		[Fact]
		public void A_folder_of_subfolders_answers()
		{
			string root = NewTempDir();
			try {
				Directory.CreateDirectory(Path.Combine(root, "G3 - Nitendinho", "roms"));
				Assert.True(GamesFolderChoice.HasEntries(root));
				Assert.Equal(root, GamesFolderChoice.Usable(root));
			} finally {
				Directory.Delete(root, true);
			}
		}

		//The setting itself is never touched: this rule chooses where to open, and
		//a folder that answers nothing today may answer something tomorrow - a stick
		//that was unplugged, a library still being copied.
		[Fact]
		public void A_folder_that_answers_nothing_is_only_not_opened_on()
		{
			string empty = NewTempDir();
			try {
				Assert.Null(GamesFolderChoice.Usable(empty));
				File.WriteAllText(Path.Combine(empty, "contra.nes"), "");
				Assert.Equal(empty, GamesFolderChoice.Usable(empty));
			} finally {
				Directory.Delete(empty, true);
			}
		}
	}
}
