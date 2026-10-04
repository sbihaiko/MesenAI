using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//ADR-0250 Decision 3's last clause: "No task-door entry opens a classic
	//window: each one opens its Player sheet, or is not offered in that door."
	//The guard reads UI/Logic/DoorEntryOpens.cs: every entry a task door shows
	//must be in the table, and none may open a classic window.
	public class DoorEntryOpensTests
	{
		[Fact]
		public void Every_task_door_entry_is_in_the_table()
		{
			foreach(MenuEntry entry in DoorEntryOpensTable.TaskDoorEntries()) {
				Assert.NotEmpty(DoorEntryOpensTable.Of(entry));
			}
		}

		[Fact]
		public void No_task_door_entry_opens_a_classic_window()
		{
			MenuEntry[] classic = DoorEntryOpensTable.TaskDoorEntries().Where(DoorEntryOpensTable.OpensClassicWindow).ToArray();
			Assert.Empty(classic);
		}

		[Fact]
		public void An_entry_no_task_door_shows_is_not_in_the_table()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => DoorEntryOpensTable.Of(MenuEntry.Debugger));
			Assert.Throws<ArgumentOutOfRangeException>(() => DoorEntryOpensTable.Of(MenuEntry.HdPackBuilder));
		}

		[Fact]
		public void The_task_doors_show_the_entries_the_table_was_written_for()
		{
			Assert.Equal(
				new[] {
					MenuEntry.Exit, MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.FdsSelectDisk, MenuEntry.FdsEjectDisk,
					MenuEntry.InsertCoin1, MenuEntry.InsertCoin2, MenuEntry.InsertCoin3, MenuEntry.InsertCoin4, MenuEntry.InputBarcode, MenuEntry.TapeRecorder,
					MenuEntry.MoviePlay, MenuEntry.NetPlay, MenuEntry.MusicRecorder, MenuEntry.ReloadPackImages, MenuEntry.EnhancementPacks, MenuEntry.LogWindow,
					MenuEntry.Screenshot, MenuEntry.About, MenuEntry.Fullscreen, MenuEntry.Record, MenuEntry.Help, MenuEntry.Settings
				}.OrderBy(e => e),
				DoorEntryOpensTable.TaskDoorEntries()
			);
		}

		//The small dialogs became sheets; the big tool windows keep their
		//window in the Player look.
		[Theory]
		[InlineData(MenuEntry.About, EntryOpens.PlayerSheet)]
		[InlineData(MenuEntry.Help, EntryOpens.PlayerSheet)]
		[InlineData(MenuEntry.Settings, EntryOpens.PlayerSheet)]
		[InlineData(MenuEntry.InputBarcode, EntryOpens.PlayerSheet)]
		[InlineData(MenuEntry.Record, EntryOpens.PlayerSheet)]
		[InlineData(MenuEntry.EnhancementPacks, EntryOpens.PlayerLookWindow)]
		[InlineData(MenuEntry.LogWindow, EntryOpens.PlayerLookWindow)]
		[InlineData(MenuEntry.NetPlay, EntryOpens.PlayerLookWindow)]
		[InlineData(MenuEntry.MoviePlay, EntryOpens.SystemPicker)]
		public void Each_entry_opens_its_Player_surface(MenuEntry entry, EntryOpens opens)
		{
			Assert.Contains(opens, DoorEntryOpensTable.Of(entry));
		}

		[Theory]
		[InlineData(null, "", false)]
		[InlineData("12ab34", "1234", true)]
		[InlineData("49123456789012345", "4912345678901", true)]
		public void A_barcode_keeps_up_to_13_digits(string? typed, string digits, bool canSubmit)
		{
			Assert.Equal(digits, BarcodeEntry.Digits(typed));
			Assert.Equal(canSubmit, BarcodeEntry.CanSubmit(typed));
		}

		[Theory]
		[InlineData("12345678", 8u)]
		[InlineData("123456789", 13u)]
		[InlineData("4912345678904", 13u)]
		public void A_barcode_over_8_digits_is_an_EAN_13(string digits, uint kind)
		{
			Assert.Equal(kind, BarcodeEntry.Kind(digits));
		}
	}
}
