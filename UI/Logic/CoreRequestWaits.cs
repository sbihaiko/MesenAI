using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mesen.Logic;

//#658: the Core's load thread raises a request (MissingFirmware - W-P13's BIOS
//sheet, or the classic firmware dialog) and blocks until the UI answers, while
//it holds its load locks. EmuApi.Stop takes those locks on the UI thread, which
//is where the answer would run, so quitting with a request up hung the app.
//Closing answers every pending request first, and any request raised after
//that is answered at once (the load then fails without the file). The answer
//never depends on the dispatcher: Close completes the waits itself.
public sealed class CoreRequestWaits
{
	private readonly object _lock = new();
	private readonly List<TaskCompletionSource> _pending = new();
	private bool _closed;

	public bool IsClosed
	{
		get { lock(_lock) { return _closed; } }
	}

	public int PendingCount
	{
		get { lock(_lock) { return _pending.Count; } }
	}

	//The wait the Core's thread blocks on; already answered once Close ran.
	public TaskCompletionSource Begin()
	{
		TaskCompletionSource wait = new(TaskCreationOptions.RunContinuationsAsynchronously);
		lock(_lock) {
			if(!_closed) {
				_pending.Add(wait);
				return wait;
			}
		}
		wait.SetResult();
		return wait;
	}

	//The UI answered (or gave up): the Core's thread goes on.
	public void End(TaskCompletionSource wait)
	{
		lock(_lock) {
			_pending.Remove(wait);
		}
		wait.TrySetResult();
	}

	//The app is quitting: every pending request is answered now.
	public void Close()
	{
		TaskCompletionSource[] pending;
		lock(_lock) {
			_closed = true;
			pending = _pending.ToArray();
			_pending.Clear();
		}
		foreach(TaskCompletionSource wait in pending) {
			wait.TrySetResult();
		}
	}
}
