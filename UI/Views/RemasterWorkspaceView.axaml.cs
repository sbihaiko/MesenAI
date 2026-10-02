using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using System.IO;

namespace Mesen.Views
{
	//G.3: thin code-behind - every decision is in RemasterWorkspaceViewModel and
	//UI/Logic/Remaster*.cs. Dialogs and the browser need a window, so they stay here.
	public class RemasterWorkspaceView : UserControl
	{
		public const string PythonDownloadUrl = "https://www.python.org/downloads/";
		public const string ToolsDownloadUrl = "https://github.com/sbihaiko/MesenAI/releases/latest";

		public RemasterWorkspaceView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RemasterWorkspaceViewModel? Model => DataContext as RemasterWorkspaceViewModel;

		private Window? Owner => TopLevel.GetTopLevel(this) as Window;

		private async void OnStart(object? sender, RoutedEventArgs e)
		{
			if(Model == null) {
				return;
			}
			if(Model.StartOpensRom) {
				//rule 10: no game yet - the button that does it is the file dialog
				string? file = await FileDialogHelper.OpenFile(null, Owner, FileDialogHelper.RomExt);
				if(file != null) {
					LoadRomHelper.LoadFile(file);
				}
				return;
			}
			Model.StartRecording();
		}

		private void OnRecord(object? sender, RoutedEventArgs e) => Model?.StartRecording();

		private void OnPrepare(object? sender, RoutedEventArgs e) => Model?.StartKit();

		private void OnBuild(object? sender, RoutedEventArgs e) => Model?.StartBuild();

		private void OnStopJob(object? sender, RoutedEventArgs e) => Model?.StopJob();

		private void OnDismissJob(object? sender, RoutedEventArgs e) => Model?.DismissJobResult();

		private void OnShowFolder(object? sender, RoutedEventArgs e)
		{
			if(Model != null) {
				RemasterFeasibilityProbe.ShowFolder(Model.ProjectFolder);
			}
		}

		private async void OnChooseFolder(object? sender, RoutedEventArgs e)
		{
			string? folder = await FileDialogHelper.OpenFolder(Owner);
			if(folder != null) {
				Model?.OpenProjectFolder(folder);
			}
		}

		private async void OnLocatePython(object? sender, RoutedEventArgs e)
		{
			string? file = await FileDialogHelper.OpenFile(null, Owner);
			if(file != null && Model != null) {
				await Model.Relocate(file, null);
				ConfigManager.Config.Save();
			}
		}

		private async void OnLocateTools(object? sender, RoutedEventArgs e)
		{
			string? folder = await FileDialogHelper.OpenFolder(Owner);
			if(folder != null && Model != null) {
				await Model.Relocate(null, folder);
				ConfigManager.Config.Save();
			}
		}

		private void OnHowToInstallPython(object? sender, RoutedEventArgs e) => Model?.AskToOpenBrowser(PythonDownloadUrl);

		private void OnHowToGetTools(object? sender, RoutedEventArgs e) => Model?.AskToOpenBrowser(ToolsDownloadUrl);

		private void OnConfirmBrowser(object? sender, RoutedEventArgs e)
		{
			string? url = Model?.ConfirmBrowser();
			if(!string.IsNullOrEmpty(url)) {
				ApplicationHelper.OpenBrowser(url);
			}
		}

		private void OnCancelBrowser(object? sender, RoutedEventArgs e) => Model?.CancelBrowser();
	}
}
