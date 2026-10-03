using System.Threading;

namespace Mesen.Logic
{
	//Exclusion gate shared by the two UI/Services flows that rewrite the same
	//installed pack folder (CommunityPackInstallService): the ROM-load
	//auto-install and the user's explicit Restore. At most one may run at a
	//time; an install/Restore that finds the gate already held refuses instead
	//of queuing, and the refused caller must NOT release the holder's token (a
	//Restore refused while an auto-install is in flight leaves the install free
	//to finish). Host-free (BCL only) so UI.Tests can pin the mutual exclusion
	//that keeps the two from ever rewriting mep/ concurrently; the host-aware
	//service owns a single static instance and releases it in a finally.
	public sealed class CommunityPackInstallGate
	{
		private int _held; // 0 = free, 1 = held; Interlocked so check-and-set is atomic across threads
		private int _deferred; // 1 = an auto-install was refused while held (#657)

		//Acquires the gate when free. False means another install/Restore is in
		//flight - the caller must back off and, on the false path, must not call
		//Exit (that would release a token it does not hold).
		public bool TryEnter()
		{
			return Interlocked.CompareExchange(ref _held, 1, 0) == 0;
		}

		//#657: the ROM-load auto-install's entry. Refused while held, like
		//TryEnter, but the refused load is remembered: the holder's Exit reports
		//it so the service can run the auto-install for whatever game is loaded
		//then (ADR-0146: auto-load whenever possible - a game opened during
		//another game's download or Restore would otherwise go without its pack
		//for the session). A user's Restore uses TryEnter and is never replayed.
		public bool TryEnterOrDefer()
		{
			while(true) {
				if(TryEnter()) {
					return true;
				}
				Interlocked.Exchange(ref _deferred, 1);
				if(Interlocked.CompareExchange(ref _held, 1, 1) != 0) {
					//Still held after the request was posted: the holder's Exit
					//has not run its exchange yet, so it will see the request.
					return false;
				}
				//The holder left between the refusal and the post. Take the
				//request back and retry; if it is already gone, that Exit
				//reported it and its caller runs the load.
				if(Interlocked.Exchange(ref _deferred, 0) == 0) {
					return false;
				}
			}
		}

		//Releases the gate. Only the caller that received true from TryEnter /
		//TryEnterOrDefer may call this, typically from a finally so a throwing
		//install still frees the gate for the next ROM load or Restore. True
		//when an auto-install was refused meanwhile (TryEnterOrDefer) - handed
		//back exactly once; the caller runs it for the game loaded now.
		public bool Exit()
		{
			Interlocked.Exchange(ref _held, 0);
			return Interlocked.Exchange(ref _deferred, 0) != 0;
		}

		public bool IsHeld
		{
			get { return Volatile.Read(ref _held) != 0; }
		}
	}
}
