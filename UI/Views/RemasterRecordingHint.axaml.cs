using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mesen.Views
{
	//ADR-0249 (W-R2): the recording hint as its own toast; no behaviour.
	public class RemasterRecordingHint : UserControl
	{
		public RemasterRecordingHint()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
