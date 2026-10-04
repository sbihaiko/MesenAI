using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Utilities;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.8: thin code-behind - every decision is in ShareWorkspaceViewModel and
	//UI/Logic/PackShare.cs, ShareProjectPackage.cs and ShareScreen.cs. The
	//folder dialog needs a window, so it stays here.
	public class ShareWorkspaceView : UserControl
	{
		public ShareWorkspaceView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private ShareWorkspaceViewModel? Model => DataContext as ShareWorkspaceViewModel;

		private Window? Owner => TopLevel.GetTopLevel(this) as Window;

		private void OnSharePack(object? sender, RoutedEventArgs e) => Model?.OpenPackPage();
		private void OnRecordAndShare(object? sender, RoutedEventArgs e) => Model?.OpenReplaySheet();
		private void OnBack(object? sender, RoutedEventArgs e) => Model?.GoHome();
		private void OnPackageAProject(object? sender, RoutedEventArgs e) => Model?.ToggleProjectList();
		private void OnContinuePack(object? sender, RoutedEventArgs e) => Model?.ContinuePack();
		private void OnBuildZip(object? sender, RoutedEventArgs e) => Model?.BuildZip();
		private void OnStopBuild(object? sender, RoutedEventArgs e) => Model?.StopBuild();
		private void OnShowZip(object? sender, RoutedEventArgs e) => Model?.ShowZip();
		private void OnOpenDrive(object? sender, RoutedEventArgs e) => Model?.OpenGoogleDrive();
		private void OnContinueProject(object? sender, RoutedEventArgs e) => Model?.ContinueProject();
		private void OnCloseSheet(object? sender, RoutedEventArgs e) => Model?.CloseSheet();
		private void OnStartReplay(object? sender, RoutedEventArgs e) => _ = Model?.StartReplay();
		private void OnShowReplayFile(object? sender, RoutedEventArgs e) => Model?.ShowReplayFile();
		private void OnContinueReplay(object? sender, RoutedEventArgs e) => Model?.ContinueReplay();

		private void OnPickProject(object? sender, RoutedEventArgs e)
		{
			if(sender is Button { Tag: string folder }) {
				Model?.OpenProject(folder);
			}
		}

		private async void OnChooseProjectFolder(object? sender, RoutedEventArgs e)
		{
			string? folder = await FileDialogHelper.OpenFolder(Owner);
			if(folder != null) {
				Model?.OpenProject(folder);
			}
		}
	}
}
