using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//#909 (PRD Part B §13.5.2 W-P4): one row of W-P4's Save states grid - the
	//slot's name and age, its preview, and the two actions the wireframe puts on
	//every slot (*Save here*, and *Load*, which an empty slot cannot offer).
	//The rules (which actions a row offers, which are enabled, where the sheet
	//opens focused) are host-free in UI/Logic/PlaySaveStateSheet.
	public partial class SaveStateSlotViewModel : ObservableObject
	{
		//#619's rule, for this grid: previews load on a thread-pool thread and
		//post back to the UI thread, also for a row a rebuild has dropped since.
		//The headless settle hook waits until none is in flight before the
		//window it belongs to is torn down.
		private static int _previewsInFlight;
		public static bool PreviewsInFlight => Volatile.Read(ref _previewsInFlight) > 0;

		private int _previewGeneration;

		public int Slot { get; }
		public SaveSlotKind Kind { get; }
		public string FileName { get; }
		public string Label { get; }

		//When the slot was written, as the file says - what the age line is built
		//from and what the sheet's focus rule reads (SaveStateSheet.FocusSlot).
		public DateTime? Written { get; private set; }

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(LoadEnabled))]
		public partial bool HasState { get; private set; }

		[ObservableProperty] public partial string AgeText { get; private set; } = "";

		[ObservableProperty] public partial Bitmap? Thumbnail { get; private set; }

		//The auto-save belongs to the core: its row has no *Save here* at all.
		public bool SaveHereVisible => SaveStateSheet.Actions(Kind).Contains(SaveSlotAction.SaveHere);

		//"Load disabled on an empty slot": the rule reads the row's own state, so
		//the button can never be armed by anything but a state on disk.
		public bool LoadEnabled => SaveStateSheet.IsEnabled(SaveSlotAction.Load, HasState);

		public SaveStateSlotViewModel(int slot, SaveSlotKind kind, string fileName)
		{
			Slot = slot;
			Kind = kind;
			FileName = fileName;
			Label = kind == SaveSlotKind.AutoSave
				? ResourceHelper.GetMessage("AutoSave")
				: ResourceHelper.GetMessage(PlaySlotGrid.SlotKey(player: true), slot);
			Refresh();
		}

		//Re-reads the slot's file: whether it holds a state, when it was written
		//(the age line), and its preview. Called when the sheet opens and after
		//*Save here* lands, which is what turns the row's Load on.
		public void Refresh()
		{
			DateTime? written = null;
			try {
				FileInfo info = new(FileName);
				if(info.Exists) {
					written = info.LastWriteTime;
				}
			} catch(Exception) {
				written = null;
			}

			HasState = written.HasValue;
			Written = written;
			AgeText = written.HasValue ? AgeOf(written.Value) : ResourceHelper.GetMessage(PlaySlotGrid.EmptyKey(player: true));

			int generation = ++_previewGeneration;
			if(!written.HasValue) {
				Thumbnail = null;
				return;
			}

			Interlocked.Increment(ref _previewsInFlight);
			string file = FileName;
			Task.Run(() => {
				Bitmap? image = null;
				try {
					image = EmuApi.GetSaveStatePreview(file);
				} catch(Exception ex) {
					EmuApi.WriteLogEntry("[SaveStates] slot preview failed: " + ex.Message);
				}
				Dispatcher.UIThread.Post(() => {
					if(generation == _previewGeneration) {
						Thumbnail = image;
					}
				});
				Interlocked.Decrement(ref _previewsInFlight);
			});
		}

		//The same wording the overlay's own row uses for the newest slot.
		private static string AgeOf(DateTime written)
		{
			TimeSpan age = DateTime.Now - written;
			(SaveStateAgeKind kind, int count) = SaveStatesSummary.Age(age < TimeSpan.Zero ? TimeSpan.Zero : age);
			return kind switch {
				SaveStateAgeKind.JustNow => ResourceHelper.GetMessage("AgeJustNow"),
				SaveStateAgeKind.Minutes => ResourceHelper.GetMessage("AgeMinutes", count),
				SaveStateAgeKind.Hours => ResourceHelper.GetMessage("AgeHours", count),
				_ => ResourceHelper.GetMessage("AgeDays", count)
			};
		}
	}
}
