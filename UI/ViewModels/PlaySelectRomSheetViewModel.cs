using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.GUI.Utilities;
using Mesen.Localization;
using Mesen.Logic;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//One game of the archive, as a row of the sheet.
	public class PlaySelectRomRow
	{
		public PlaySelectRomRow(ArchiveRomEntry entry, int position)
		{
			Entry = entry;
			Position = position;
		}

		public ArchiveRomEntry Entry { get; }
		public int Position { get; }
		public string Name => Entry.Filename;
	}

	//ADR-0249 (W-P5 sheet shape): an archive with two or more games, in Player
	//mode - a sheet inside the main window instead of SelectRomWindow. The
	//heading names the archive, a search field narrows the list, each game is a
	//row that opens it, and Cancel (or Esc) opens nothing. LoadRomHelper waits
	//on Request() as it waited on the window's ShowDialog.
	public partial class PlaySelectRomSheetViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial string Title { get; private set; } = "";
		[ObservableProperty] public partial string SearchString { get; set; } = "";
		[ObservableProperty] public partial List<PlaySelectRomRow> Rows { get; private set; } = new();

		private List<PlaySelectRomRow> _all = new();
		private TaskCompletionSource<PlaySelectRomRow?>? _request;

		//The picked row, or null when the sheet was cancelled or dismissed.
		public Task<PlaySelectRomRow?> Request(string archiveName, IReadOnlyList<ArchiveRomEntry> entries)
		{
			_request?.TrySetResult(null);
			_all = entries.Select((e, i) => new PlaySelectRomRow(e, i)).ToList();
			Title = ResourceHelper.GetMessage("SelectRomPlayerTitle", archiveName);
			SearchString = "";
			Rows = _all;
			_request = new TaskCompletionSource<PlaySelectRomRow?>();
			IsVisible = true;
			return _request.Task;
		}

		partial void OnSearchStringChanged(string value)
		{
			Rows = _all.Where(r => ArchiveRomPick.Matches(r.Name, value)).ToList();
		}

		public void Choose(PlaySelectRomRow row) => Finish(row);

		//Enter in the search field: the first game it keeps.
		public void ChooseFirst()
		{
			if(Rows.Count > 0) {
				Finish(Rows[0]);
			}
		}

		//Cancel, Esc, or another open replacing this one: nothing opens.
		public void Cancel() => Finish(null);

		private void Finish(PlaySelectRomRow? row)
		{
			if(!IsVisible) {
				return;
			}
			IsVisible = false;
			TaskCompletionSource<PlaySelectRomRow?>? request = _request;
			_request = null;
			request?.TrySetResult(row);
		}
	}
}
