using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//ADR-0249 (W-P10 › Adjust…): thin code-behind - each button is one call
	//into MainWindowViewModel, which owns the sheet's ShaderConfigViewModel.
	public class PlayerShaderSheetView : UserControl
	{
		public PlayerShaderSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

		private void OnReset(object? sender, RoutedEventArgs e) => Model?.ShaderSheet?.ResetToDefaults();
		private void OnCancel(object? sender, RoutedEventArgs e) => Model?.CloseShaderSheet(false);
		private void OnOk(object? sender, RoutedEventArgs e) => Model?.CloseShaderSheet(true);
	}
}
