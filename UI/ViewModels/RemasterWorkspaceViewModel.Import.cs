using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using System.Collections.Generic;
using System.IO;

namespace Mesen.ViewModels
{
	//G.7 (PRD Part B §13.5.3 W-R6, ADR-0198): a finished pack picked with
	//*Open a project folder…* asks once, in place, to be made editable; yes
	//runs `mep_import.py import` as a job into a new folder next to the pack
	//(the pack itself is never written), and the result opens as the project.
	//A refusal is shown in the W-R4 shape - one sentence per rule, *Show line*
	//per row - with no *Make editable* button.
	public partial class RemasterWorkspaceViewModel
	{
		private string _importPack = "";
		private string _importDestination = "";
		private bool _importPatched;

		[ObservableProperty] public partial bool IsImportSheetVisible { get; private set; }
		[ObservableProperty] public partial bool IsImportAsking { get; private set; }
		[ObservableProperty] public partial bool IsImportRunning { get; private set; }
		[ObservableProperty] public partial bool IsImportRefused { get; private set; }
		[ObservableProperty] public partial bool IsImportPatched { get; private set; }
		[ObservableProperty] public partial string ImportPackName { get; private set; } = "";
		[ObservableProperty] public partial RemasterControlViewModel MakeEditable { get; private set; } = RemasterControlViewModel.Hidden;
		[ObservableProperty] public partial List<RemasterRefusalRow> ImportRefusals { get; private set; } = new();

		private bool BeginImport(string packFolder)
		{
			_importPack = packFolder;
			_importPatched = RemasterHandOff.IsPatchedPack(packFolder);
			ImportPackName = Path.GetFileName(packFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			IsImportPatched = _importPatched;
			ImportRefusals = new();
			NoticeText = "";
			ShowImportState(asking: true);
			return false;
		}

		private void ShowImportState(bool asking = false, bool running = false, bool refused = false)
		{
			IsImportSheetVisible = asking || running || refused;
			IsImportAsking = asking;
			IsImportRunning = running;
			IsImportRefused = refused;
			RefreshImportControl();
		}

		private void RefreshImportControl()
		{
			if(!IsImportSheetVisible) {
				return;
			}
			RemasterHandOffControl c = RemasterHandOff.ImportControl(_feasibility ?? PendingFeasibility, File.Exists, _jobs.Snapshot.IsRunning, IsRecording, _importPatched, _gameLoaded);
			MakeEditable = new RemasterControlViewModel(c.Enabled, c.Enabled ? "" : HandOffReason(c.Reason));
		}

		//[ Make editable ]
		public bool ConfirmImport()
		{
			RefreshImportControl();
			if(!MakeEditable.IsEnabled || _feasibility == null) {
				return false;
			}
			_importDestination = RemasterHandOff.ImportDestination(_importPack, p => Directory.Exists(p) || File.Exists(p));
			RemasterJobSpec spec = RemasterHandOff.ImportJob(_feasibility, _importPack, _importDestination, _romPath, _importPatched, ImportPackName);
			_jobResultTimer?.Stop();
			if(!_jobs.Start(spec)) {
				return false;
			}
			ShowImportState(running: true);
			OnJobChanged();
			return true;
		}

		//[Cancel], Esc, and OK after a refusal: back to where the user was.
		public void CancelImport()
		{
			if(_jobs.Snapshot.IsRunning && _jobs.Snapshot.Kind == RemasterJobKind.Import) {
				_jobs.Stop();
			}
			if(_jobs.Snapshot.Kind == RemasterJobKind.Import && !_jobs.Snapshot.IsRunning) {
				_jobs.Clear();
			}
			ShowImportState();
			Refresh();
		}

		//Called by OnJobChanged after every change of the import job.
		private void OnImportJobChanged(RemasterJobSnapshot job)
		{
			if(job.Kind != RemasterJobKind.Import || !IsImportSheetVisible) {
				return;
			}
			switch(job.Status) {
				case RemasterJobStatus.Succeeded:
					ShowImportState();
					_jobs.Clear();
					OpenProjectFolder(_importDestination);
					break;
				case RemasterJobStatus.Failed:
					ImportRefusals = new List<RemasterRefusalRow> { RemasterRefusalRow.From(RemasterHandOff.Refusal(job.FailureLine)) };
					ShowImportState(refused: true);
					break;
				case RemasterJobStatus.Stopped:
					_jobs.Clear();
					ShowImportState(asking: true);
					break;
			}
		}

		//The job card and the status line name the job that runs.
		private static string JobMessage(RemasterJobKind kind, string kitId)
		{
			return kind == RemasterJobKind.Import ? kitId.Replace("RemasterJob", "RemasterImport") : kitId;
		}

		private static string HandOffReason(RemasterHandOffReason reason)
		{
			return reason == RemasterHandOffReason.None ? "" : ResourceHelper.GetMessage("RemasterHandOff" + reason);
		}
	}

	//One refusal row: the plain sentence, and the cited manifest line behind
	//*Show line* (empty when mep_import cited none).
	public sealed record RemasterRefusalRow(string Sentence, string Where, string CitedLine)
	{
		public bool HasLine => CitedLine.Length > 0;

		public static RemasterRefusalRow From(RemasterImportRefusal r)
		{
			string where = r.Line > 0 ? ResourceHelper.GetMessage("RemasterImportRefusalWhere", Path.GetFileName(r.File), r.Line) : "";
			return new RemasterRefusalRow(r.Sentence, where, RemasterHandOff.CitedText(r));
		}
	}
}
