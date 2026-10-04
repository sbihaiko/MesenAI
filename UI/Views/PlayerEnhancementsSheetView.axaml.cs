using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.4 (W-P7): the switches bind the draft TwoWay; the one button applies it.
	public class PlayerEnhancementsSheetView : UserControl
	{
		//The Pack row: the window owns the EmuApi reads W-P4's Pack row uses.
		public event EventHandler? PackRequested;

		public PlayerEnhancementsSheetView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnPack(object? sender, RoutedEventArgs e) => PackRequested?.Invoke(this, EventArgs.Empty);
		private void OnApply(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.ApplyEnhancements();
	}
}
