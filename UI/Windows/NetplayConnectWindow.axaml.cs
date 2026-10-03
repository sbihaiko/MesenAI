using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public class NetplayConnectWindow : MesenWindow
	{
		public NetplayConnectWindow()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private async void Ok_OnClick(object sender, RoutedEventArgs e)
		{
			NetplayConnectViewModel model = (NetplayConnectViewModel)DataContext!;
			ConfigManager.Config.Netplay = model.Config.Clone();

			//The window stays up, with its moving indicator, while the socket
			//connects; it closes on success and stays for a retry on failure
			//(the core's own message says why).
			bool connected = await model.ConnectAsync((host, port, password) => {
				NetplayApi.Connect(host, port, password, false);
				return NetplayApi.IsConnected();
			});
			if(connected) {
				Close(true);
			}
		}

		private void Cancel_OnClick(object sender, RoutedEventArgs e)
		{
			Close(false);
		}
	}
}
