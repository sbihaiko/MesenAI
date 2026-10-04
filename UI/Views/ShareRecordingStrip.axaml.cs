using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.8 (W-H4): Stop ends the replay recording; Esc does the same (ShortcutHandler).
	public class ShareRecordingStrip : UserControl
	{
		public ShareRecordingStrip()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnStop(object? sender, RoutedEventArgs e)
		{
			_ = (DataContext as ShareWorkspaceViewModel)?.StopReplay();
		}
	}
}
