using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Logic;
using System;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//Netplay > Connect: the config being edited plus the wait. Connect blocks
	//on the socket, so IsConnecting drives the moving indicator (and disables
	//OK) from the click until the attempt succeeds or fails.
	public partial class NetplayConnectViewModel : ViewModelBase
	{
		private readonly NetplayConnectWait _wait = new();

		public NetplayConfig Config { get; }

		[ObservableProperty] public partial bool IsConnecting { get; private set; }

		public NetplayConnectViewModel(NetplayConfig config)
		{
			Config = config;
			_wait.Changed += () => IsConnecting = _wait.IsConnecting;
		}

		//connect gets host, port, password and returns whether the client is connected.
		public Task<bool> ConnectAsync(Func<string, ushort, string, bool> connect)
		{
			NetplayConfig cfg = Config;
			return _wait.RunAsync(() => connect(cfg.Host, cfg.Port, cfg.Password));
		}
	}
}
