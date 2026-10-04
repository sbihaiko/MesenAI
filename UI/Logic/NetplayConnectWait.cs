using System;
using System.Threading.Tasks;

namespace Mesen.Logic;

//Netplay's Connect blocks on the socket, so the Connect window is busy from
//the click until the attempt succeeds or fails (the window shows a moving
//indicator meanwhile - no wait may look like a freeze). Host-free: the window
//supplies the blocking call, this tracks the busy state.
public sealed class NetplayConnectWait
{
	private int _busy;

	public bool IsConnecting => _busy != 0;

	//Raised on the caller's context when IsConnecting flips.
	public event Action? Changed;

	//connect runs off the calling thread and returns whether the client is
	//connected. A second call while one runs is refused (false, no attempt).
	public async Task<bool> RunAsync(Func<bool> connect)
	{
		if(System.Threading.Interlocked.Exchange(ref _busy, 1) != 0) {
			return false;
		}
		Changed?.Invoke();
		try {
			return await Task.Run(connect);
		} finally {
			System.Threading.Volatile.Write(ref _busy, 0);
			Changed?.Invoke();
		}
	}
}
