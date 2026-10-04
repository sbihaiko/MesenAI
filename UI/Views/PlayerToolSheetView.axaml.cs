using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;

namespace Mesen.Views
{
	//ADR-0250: thin code-behind over PlayerToolSheetViewModel. Enter in the
	//barcode field submits it; Browse… asks the system's save picker for the
	//video's file, as VideoRecordWindow did.
	public class PlayerToolSheetView : UserControl
	{
		public PlayerToolSheetView()
		{
			InitializeComponent();
			this.GetControl<TextBox>("ToolSheetBarcode").AddHandler(KeyDownEvent, OnBarcodeKeyDown, RoutingStrategies.Tunnel);
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private PlayerToolSheetViewModel? Model => DataContext as PlayerToolSheetViewModel;

		private void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
		{
			if(e.Key == Key.Enter) {
				Model?.SubmitBarcode();
				e.Handled = true;
			}
		}

		private void OnClose(object? sender, RoutedEventArgs e) => Model?.Close();

		private void OnOpenReleases(object? sender, RoutedEventArgs e) => Model?.OpenReleasePage();

		private void OnRecord(object? sender, RoutedEventArgs e) => Model?.StartVideoRecording();

		private void OnSubmitBarcode(object? sender, RoutedEventArgs e) => Model?.SubmitBarcode();

		private void OnWebsite(object? sender, RoutedEventArgs e) => ApplicationHelper.OpenBrowser(AboutInfo.WebsiteUrl);

		private void OnCommit(object? sender, RoutedEventArgs e)
		{
			if(Model?.About is AboutInfo about && about.BuildSha.Length > 0) {
				ApplicationHelper.OpenBrowser(AboutInfo.CommitUrl(about.BuildSha));
			}
		}

		private void OnCredit(object? sender, RoutedEventArgs e)
		{
			if(sender is Control { DataContext: AboutListEntry entry } && entry.Url.Length > 0) {
				ApplicationHelper.OpenBrowser(entry.Url);
			}
		}

		private async void OnBrowseVideo(object? sender, RoutedEventArgs e)
		{
			VideoRecordConfigViewModel? video = Model?.VideoRecord;
			if(video == null) {
				return;
			}
			bool isGif = video.Config.Codec == VideoCodec.GIF;
			string initFilename = EmuApi.GetRomInfo().GetRomName() + (isGif ? ".gif" : ".avi");
			string? filename = await FileDialogHelper.SaveFile(ConfigManager.AviFolder, initFilename, this.GetWindow(), isGif ? FileDialogHelper.GifExt : FileDialogHelper.AviExt);
			if(filename != null && Model?.VideoRecord == video) {
				video.SavePath = filename;
			}
		}
	}
}
