using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mesen.Views
{
	public class PlayerWindowSettingsView : UserControl
	{
		public PlayerWindowSettingsView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
