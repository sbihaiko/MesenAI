using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//W-R0 › Recent projects: thin code-behind; the rows are RemasterWorkspaceViewModel's.
	public class RemasterRecentProjectsView : UserControl
	{
		public RemasterRecentProjectsView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnOpenRecent(object? sender, RoutedEventArgs e)
		{
			if(DataContext is RemasterWorkspaceViewModel model && (sender as Control)?.DataContext is RemasterRecentProjectRow row) {
				model.OpenRecentProject(row);
			}
		}
	}
}
