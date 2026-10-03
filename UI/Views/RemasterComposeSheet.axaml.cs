using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.7 (W-R7): thin code-behind. The sheet takes focus when it opens so Esc
	//closes it back to the screen it was opened from (§13.3 rule 9).
	public class RemasterComposeSheet : UserControl
	{
		public RemasterComposeSheet()
		{
			InitializeComponent();
			KeyDown += OnKeyDown;
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if(change.Property == IsVisibleProperty && IsVisible) {
				Dispatcher.UIThread.Post(() => DefaultFocus?.Focus());
			}
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RemasterWorkspaceViewModel? Model => DataContext as RemasterWorkspaceViewModel;

		private void OnKeyDown(object? sender, KeyEventArgs e)
		{
			if(e.Key == Key.Escape && IsVisible) {
				Close();
				e.Handled = true;
			}
		}

		private void OnOpen(object? sender, RoutedEventArgs e) => Model?.LaunchComposer();

		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.CancelCompose();

		private void Close() => Model?.CancelCompose();

		private Button? DefaultFocus => this.FindControl<Button>("RemasterComposeCancelButton");
	}
}
