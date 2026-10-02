using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.3 (W-R2): Stop ends the recording; Esc does the same (MainWindow).
	public class RemasterRecordingStrip : UserControl
	{
		public RemasterRecordingStrip()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnStop(object? sender, RoutedEventArgs e)
		{
			(DataContext as RemasterWorkspaceViewModel)?.StopRecording();
		}
	}
}
