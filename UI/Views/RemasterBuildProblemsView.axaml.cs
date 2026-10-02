using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Services;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.6 (W-R4): thin code-behind; the problems are RemasterWorkspaceViewModel's.
	public class RemasterBuildProblemsView : UserControl
	{
		public RemasterBuildProblemsView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RemasterWorkspaceViewModel? Model => DataContext as RemasterWorkspaceViewModel;

		private void OnOpenFile(object? sender, RoutedEventArgs e)
		{
			if((sender as Control)?.DataContext is RemasterProblemRow row) {
				RemasterFileOpener.Open(row.FilePath);
			}
		}

		private void OnShowLog(object? sender, RoutedEventArgs e) => Model?.ToggleBuildLog();

		private void OnTryAgain(object? sender, RoutedEventArgs e) => Model?.StartBuild();
	}
}
