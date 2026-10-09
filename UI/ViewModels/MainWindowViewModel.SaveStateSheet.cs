using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//#909 (PRD Part B §13.5.2 W-P4): W-P4's Save states sheet is one grid over
	//the ten manual slots and the auto-save, each row with its own *Save here* /
	//*Load*. This is the sheet's glue - the rows, what each action does, and how
	//the sheet opens - kept in its own partial so the pause overlay's own file
	//stays the overlay's.
	public partial class MainWindowViewModel
	{
		//The rows, in slot order: 1 … 10, then the auto-save.
		public ObservableCollection<SaveStateSlotViewModel> SaveStateSlots { get; } = new();

		//W-P4's Save states row opens this sheet: the grid is read off the disk as
		//it opens, so the ages and the enabled Loads are the state the player left.
		public void RefreshSaveStateSlots()
		{
			SaveStateSlots.Clear();

			string romName = RomInfo.GetRomName();
			if(string.IsNullOrEmpty(romName)) {
				return;
			}

			for(int slot = 1; slot <= SaveStateSheet.ManualSlots; slot++) {
				SaveStateSlots.Add(new SaveStateSlotViewModel(slot, SaveSlotKind.Manual, SlotFile(romName, slot)));
			}
			SaveStateSlots.Add(new SaveStateSlotViewModel(SaveStateSheet.AutoSaveSlot, SaveSlotKind.AutoSave, SlotFile(romName, SaveStateSheet.AutoSaveSlot)));
		}

		//The row a slot's own file lives in, by the same name the classic grid and
		//the overlay's row value use.
		public SaveStateSlotViewModel? SaveStateSlot(int slot)
		{
			foreach(SaveStateSlotViewModel row in SaveStateSlots) {
				if(row.Slot == slot) {
					return row;
				}
			}
			return null;
		}

		//The row the grid opens with the key (or the pad) on: the newest slot that
		//holds a state, else the first. The choice is the host-free rule's
		//(SaveStateSheet.FocusSlot), asked here so the pad wiring and the tests read
		//one answer.
		public SaveStateSlotViewModel? FocusSaveStateSlot()
		{
			if(SaveStateSlots.Count == 0) {
				return null;
			}
			SaveStateSlotRow[] rows = new SaveStateSlotRow[SaveStateSlots.Count];
			for(int i = 0; i < SaveStateSlots.Count; i++) {
				SaveStateSlotViewModel row = SaveStateSlots[i];
				rows[i] = new SaveStateSlotRow(row.Slot, row.Kind, row.HasState, row.Written);
			}
			return SaveStateSlots[SaveStateSheet.FocusSlot(rows)];
		}

		//*Save here*: the core writes the state, the row shows it at once - the
		//age and the now-enabled Load - and the game stays paused under the sheet,
		//which is where the player still is.
		public void SaveSaveStateSlot(SaveStateSlotViewModel row)
		{
			if(!row.SaveHereVisible) {
				return;
			}

			uint slot = (uint)row.Slot;
			Task.Run(() => {
				EmuApi.SaveState(slot);
				Dispatcher.UIThread.Post(() => {
					row.Refresh();
					SaveStatesRowValue = BuildSaveStatesSummary();
				});
			});
		}

		//*Load*: back to the game - the sheet and the overlay go down first, and
		//the load is a state restore onto the game already on screen (which is why
		//it resumes when it lands, the window's own load path).
		public void LoadSaveStateSlot(SaveStateSlotViewModel row)
		{
			if(!row.LoadEnabled) {
				return;
			}

			uint slot = (uint)row.Slot;
			IsSaveStatesSheetVisible = false;
			IsPlayerOverlayVisible = false;
			Task.Run(() => {
				EmuApi.LoadState(slot);
				EmuApi.Resume();
			});
		}

		private static string SlotFile(string romName, int slot)
		{
			return Path.Combine(ConfigManager.SaveStateFolder, romName + "_" + slot + "." + FileDialogHelper.MesenSaveStateExt);
		}
	}
}
