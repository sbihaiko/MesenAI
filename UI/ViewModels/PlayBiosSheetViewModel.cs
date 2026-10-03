using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P13): a game needs a BIOS file.
	//Replaces FirmwareHelper.RequestFirmwareFile's message box and file-dialog
	//loop in Player mode; the Core's MissingFirmware notification waits on
	//Request() exactly as it waited on that loop. A wrong size is an inline line
	//(W-X2) and an unknown dump an inline confirmation (W-X1); the drop zone
	//stays. MesenAI never points to a BIOS source.
	public partial class PlayBiosSheetViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial string Body { get; private set; } = "";
		[ObservableProperty] public partial string DropTitle { get; private set; } = "";
		[ObservableProperty] public partial string DropHint { get; private set; } = "";
		[ObservableProperty] public partial string ErrorText { get; private set; } = "";
		[ObservableProperty] public partial bool IsConfirmingUnknown { get; private set; }

		private FirmwareType _type;
		private long[] _sizes = Array.Empty<long>();
		private string _gameName = "";
		private string _pendingUnknown = "";
		private TaskCompletionSource<bool>? _request;

		//Set when the last request ended with Cancel - the load that follows
		//fails, and that is not a broken file (PlayLoadFailure.ShowsAlert).
		public bool LastRequestCancelled { get; private set; }

		//The status-line clause a Cancel leaves: "Zelda needs the FDS BIOS".
		public string CancelNotice { get; private set; } = "";

		public BiosKind Kind { get; private set; }

		//True when the file was installed (the Core then retries the load).
		public Task<bool> Request(FirmwareType type, string fileName, uint size, uint altSize, string gameName)
		{
			_request?.TrySetResult(false);
			_type = type;
			_gameName = gameName ?? "";
			_sizes = altSize > 0 && altSize != size ? new long[] { size, altSize } : new long[] { size };
			Kind = PlayBiosPrompt.Classify(type.ToString());
			Body = ResourceHelper.GetMessage("BiosSheetBody" + Kind);
			DropTitle = ResourceHelper.GetMessage("BiosSheetDropTitle", fileName);
			DropHint = ResourceHelper.GetMessage("BiosSheetDropHint", PlayBiosPrompt.SizeText(size));
			ErrorText = "";
			IsConfirmingUnknown = false;
			_pendingUnknown = "";
			LastRequestCancelled = false;
			CancelNotice = "";
			_request = new TaskCompletionSource<bool>();
			IsVisible = true;
			return _request.Task;
		}

		//A file dropped on the sheet or picked with Choose File….
		public void TryFile(string path)
		{
			if(!IsVisible || !File.Exists(path)) {
				return;
			}
			IsConfirmingUnknown = false;
			//The file can vanish or be unreadable after File.Exists: a sentence,
			//never a throw (the Core waits on this sheet's answer).
			BiosSizeCheck check;
			bool known;
			try {
				check = PlayBiosPrompt.CheckSize(new FileInfo(path).Length, _sizes);
				known = check.Accepted && FirmwareHelper.IsKnownDump(_type, path);
			} catch(Exception ex) {
				ErrorText = ResourceHelper.GetMessage("BiosSheetReadFailed", ex.Message);
				return;
			}
			if(!check.Accepted) {
				ErrorText = ResourceHelper.GetMessage("BiosSheetWrongSize", PlayBiosPrompt.SizeText(check.ActualSize),
					ResourceHelper.GetMessage("BiosShortName" + Kind), PlayBiosPrompt.SizeText(check.ExpectedSize));
				return;
			}
			if(!known) {
				_pendingUnknown = path;
				ErrorText = "";
				IsConfirmingUnknown = true;
				return;
			}
			Install(path);
		}

		//W-X1: "This is not a copy MesenAI knows. Use it anyway?"
		public void ConfirmUnknown(bool useIt)
		{
			string path = _pendingUnknown;
			_pendingUnknown = "";
			IsConfirmingUnknown = false;
			if(useIt && path.Length > 0) {
				Install(path);
			}
		}

		private void Install(string path)
		{
			try {
				FirmwareHelper.CopyFirmwareFile(_type, path);
			} catch(Exception ex) {
				ErrorText = ResourceHelper.GetMessage("BiosSheetCopyFailed", ex.Message);
				return;
			}
			Finish(true);
		}

		public void Cancel()
		{
			if(!IsVisible) {
				return;
			}
			LastRequestCancelled = true;
			CancelNotice = ResourceHelper.GetMessage("BiosNeededStatus", _gameName, ResourceHelper.GetMessage("BiosShortName" + Kind));
			Finish(false);
		}

		//#658: the request is dropped, not refused - another open, a power off
		//or quitting. The Core's load ends without the file, quietly: no W-P14
		//alert (LastRequestCancelled) and no "needs the BIOS" sentence.
		public void Dismiss()
		{
			if(!IsVisible) {
				return;
			}
			LastRequestCancelled = true;
			CancelNotice = "";
			Finish(false);
		}

		private void Finish(bool installed)
		{
			IsVisible = false;
			IsConfirmingUnknown = false;
			_request?.TrySetResult(installed);
			_request = null;
		}

		//Read once by the load-failure path, so a later failure is judged alone.
		public bool ConsumeCancelled()
		{
			bool cancelled = LastRequestCancelled;
			LastRequestCancelled = false;
			return cancelled;
		}
	}
}
