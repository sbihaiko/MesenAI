using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.ViewModels
{
	//ADR-0250 Decision 3 ("No task-door entry opens a classic window"): the
	//small dialogs a task door's Tools ⋯ reached - About, Command Line, Check
	//for Updates, the video recorder's settings and a barcode - are one sheet
	//inside the main window in Player mode (PlayerToolSheetView, W-P5 sheet
	//shape). One at a time: opening one replaces another. Classic keeps
	//AboutWindow, CommandLineHelpWindow, VideoRecordWindow and InputBarcodeWindow.
	public partial class PlayerToolSheetViewModel : ViewModelBase
	{
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsAbout), nameof(IsCommandLine), nameof(IsCheckForUpdates), nameof(IsVideoRecord), nameof(IsBarcode), nameof(Title), nameof(IsDoneOnly))]
		public partial PlayerToolSheet Kind { get; private set; }

		public bool IsVisible => Kind != PlayerToolSheet.None;
		public bool IsAbout => Kind == PlayerToolSheet.About;
		public bool IsCommandLine => Kind == PlayerToolSheet.CommandLine;
		public bool IsCheckForUpdates => Kind == PlayerToolSheet.CheckForUpdates;
		public bool IsVideoRecord => Kind == PlayerToolSheet.VideoRecord;
		public bool IsBarcode => Kind == PlayerToolSheet.Barcode;

		//About and Command Line only inform: a single Done closes them.
		public bool IsDoneOnly => IsAbout || IsCommandLine;

		public string Title => Kind == PlayerToolSheet.None ? "" : ResourceHelper.GetMessage("PlayerToolSheetTitle" + Kind);

		[ObservableProperty] public partial AboutInfo? About { get; private set; }
		public List<AboutListEntry> Acknowledgements { get; } = new();
		public List<AboutListEntry> Libraries { get; } = new();

		[ObservableProperty] public partial List<CommandLineTabEntry> CommandLineTabs { get; private set; } = new();
		[ObservableProperty] public partial CommandLineTabEntry? SelectedCommandLineTab { get; set; }

		public string UpdateMessage => ResourceHelper.GetMessage("UpdateCheckNoFeed", UpdateChannel.ReleasesPageUrl);

		[ObservableProperty] public partial VideoRecordConfigViewModel? VideoRecord { get; private set; }

		//The last barcode typed, offered again (InputBarcodeWindow keeps it too).
		private static string _lastBarcode = "";

		[ObservableProperty, NotifyPropertyChangedFor(nameof(CanSubmitBarcode))]
		public partial string Barcode { get; set; } = "";

		public bool CanSubmitBarcode => BarcodeEntry.CanSubmit(Barcode);

		partial void OnBarcodeChanged(string value)
		{
			string digits = BarcodeEntry.Digits(value);
			if(digits != value) {
				Barcode = digits;
			}
		}

		public void OpenAbout()
		{
			if(Libraries.Count == 0) {
				Libraries.AddRange(AboutInfo.Libraries());
				Acknowledgements.AddRange(AboutInfo.Acknowledgements());
			}
			About ??= new AboutInfo();
			Show(PlayerToolSheet.About);
		}

		public void OpenCommandLine()
		{
			if(CommandLineTabs.Count == 0) {
				CommandLineTabs = CommandLineHelper.GetAvailableSwitches().Select(kvp => new CommandLineTabEntry() { Name = kvp.Key, Content = kvp.Value }).ToList();
			}
			SelectedCommandLineTab = CommandLineTabs.FirstOrDefault();
			Show(PlayerToolSheet.CommandLine);
		}

		//#672: the fork has no update feed, so Check for Updates offers the
		//release page (UpdateChannel.Decide's OfferReleasePage).
		public void OpenCheckForUpdates() => Show(PlayerToolSheet.CheckForUpdates);

		public void OpenVideoRecord()
		{
			Show(PlayerToolSheet.VideoRecord);
			VideoRecord = new VideoRecordConfigViewModel();
		}

		public void OpenBarcode()
		{
			Show(PlayerToolSheet.Barcode);
			Barcode = _lastBarcode;
		}

		//Record: the classic window's OK - saves the settings and starts the AVI/GIF.
		public void StartVideoRecording()
		{
			VideoRecordConfigViewModel? model = VideoRecord;
			if(model == null) {
				return;
			}
			model.SaveConfig();
			RecordApi.AviRecord(model.SavePath, new RecordAviOptions() {
				Codec = model.Config.Codec,
				CompressionLevel = model.Config.CompressionLevel,
				RecordSystemHud = model.Config.RecordSystemHud,
				RecordInputHud = model.Config.RecordInputHud
			});
			Close();
		}

		//Input: the classic window's OK - the digits go to the barcode reader.
		public void SubmitBarcode()
		{
			string digits = BarcodeEntry.Digits(Barcode);
			if(digits.Length == 0) {
				return;
			}
			_lastBarcode = digits;
			Close();
			if(UInt64.TryParse(digits, out UInt64 value)) {
				EmuApi.InputBarcode(value, BarcodeEntry.Kind(digits));
			}
		}

		//#953: the release page's hand-off to the browser. A seam so a test can
		//see the URL handed over without launching anything.
		public Action<string> ReleasePageLauncher { get; set; } = ApplicationHelper.OpenBrowser;

		public void OpenReleasePage()
		{
			Close();
			ReleasePageLauncher(UpdateChannel.ReleasesPageUrl);
		}

		//Close, Cancel, Esc, or leaving Player mode: nothing more happens.
		public void Close()
		{
			Kind = PlayerToolSheet.None;
			DisposeVideoRecord();
		}

		private void Show(PlayerToolSheet kind)
		{
			DisposeVideoRecord();
			Kind = kind;
		}

		private void DisposeVideoRecord()
		{
			VideoRecordConfigViewModel? model = VideoRecord;
			VideoRecord = null;
			model?.Dispose();
		}
	}
}
