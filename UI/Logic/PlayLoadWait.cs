using System;
using System.Threading;

namespace Mesen.Logic;

//#734: "every wait must have an animation so it does not look broken" (the
//user's rule). In Player mode's Play workspace an open is a wait from the
//click to the game's first picture: the Core's LoadRom (pack resolution and
//LoadHdPack), then the first frames (the HD bitmap decode, and a stalled
//audio device, #733, which opens on the first frame's audio). The load card
//(an indeterminate bar) shows for all of it.
//
//On macOS the game picture is a native view drawn above every Avalonia
//control, so the card is only visible while the native picture is hidden.
//The home already keeps it hidden through the open (rule 5,
//PlayLoadFailure.KeepsHomeDuringLoad); GameLoaded used to take the home away
//at once, leaving a black still window until the first frame. Now the home
//stays until the picture is out (OnGameLoaded returns true).
//
//A reload of the game on screen (power cycle, Reload, an in-place pack
//change - W-P7, the picker, Remaster's Build & Show) is the same wait: the
//Core reloads the ROM and its pack, then draws. BeginReload shows the card
//("Reloading <game>…") from the request to the first picture after it. A
//reload has more ways to end without a picture (refused, paused, failed),
//and each one ends the wait; Expire is the safety net of the one it names.
//
//Threading: Begin/BeginReload run on the UI thread, OnGameLoaded and
//OnLoadReturned on the load thread, OnFrameDone on the emulation thread
//(PpuFrameDone, every frame, so its idle path is one volatile read).

public enum PlayLoadWaitPhase
{
	Idle,
	//From the click until the Core's GameLoaded (or the load call failing).
	Opening,
	//From GameLoaded until the first picture is out.
	WaitingForPicture
}

public enum PlayLoadWaitKind
{
	//An open: from the click to the new game's first picture.
	Open,
	//A reload of the game on screen.
	Reload
}

public sealed class PlayLoadWait
{
	//VideoDecoder::UpdateFrame spins until the previous frame is decoded, so
	//when frame N+2 is emulated frame N has been decoded and handed to the
	//renderer: the third PpuFrameDone after GameLoaded means the first picture
	//is out (with an HD pack, the bitmap decode is behind it too).
	public const int FramesUntilShown = 3;

	//A game that never draws (or a core that never sends PpuFrameDone) still
	//gets its picture back. Longer than #733's ~10 s audio stall.
	public static readonly TimeSpan PictureTimeout = TimeSpan.FromSeconds(30);

	private readonly object _lock = new();
	private int _phase;
	private int _frames;
	private int _ticket;

	public PlayLoadWaitPhase Phase => (PlayLoadWaitPhase)Volatile.Read(ref _phase);
	public bool IsActive => Phase != PlayLoadWaitPhase.Idle;
	public string GameName { get; private set; } = "";
	public int OpenGeneration { get; private set; }
	public PlayLoadWaitKind Kind { get; private set; }
	//Which wait this is: every Begin/BeginReload takes a new one.
	public int Ticket => Volatile.Read(ref _ticket);

	//Where the card can be seen: Player mode's Play workspace, the same place
	//the home stays through an open.
	public static bool ShowsFor(bool playerMode, bool playWorkspace) => PlayLoadFailure.KeepsHomeDuringLoad(playerMode, playWorkspace);

	//Where a reload's card can be seen: Player mode, wherever the game is on
	//screen (Play, Remaster's and Share's game views).
	public static bool ShowsReloadFor(bool playerMode, bool gameOnScreen) => playerMode && gameOnScreen;

	//What the card says: a resource message id and its argument.
	public static (string MessageId, string? Arg) Text(string gameName, PlayLoadWaitKind kind = PlayLoadWaitKind.Open)
	{
		string verb = kind == PlayLoadWaitKind.Reload ? "Reloading" : "Opening";
		return string.IsNullOrWhiteSpace(gameName) ? ("PlayLoadWait" + verb + "Game", null) : ("PlayLoadWait" + verb, gameName.Trim());
	}

	public int Begin(string gameName, int openGeneration) => Start(gameName, openGeneration, PlayLoadWaitKind.Open);

	public int BeginReload(string gameName, int openGeneration) => Start(gameName, openGeneration, PlayLoadWaitKind.Reload);

	private int Start(string gameName, int openGeneration, PlayLoadWaitKind kind)
	{
		lock(_lock) {
			GameName = gameName ?? "";
			OpenGeneration = openGeneration;
			Kind = kind;
			Volatile.Write(ref _frames, 0);
			SetPhase(PlayLoadWaitPhase.Opening);
			return Interlocked.Increment(ref _ticket);
		}
	}

	//GameLoaded. True when the home must stay up until the picture is out;
	//false when nothing waits (no open, or the game loaded paused and will
	//draw nothing until resumed). emulatorPaused is the Core's pause flag: a
	//reload keeps it (a paused game draws one frame and parks), an open's new
	//game does not inherit it.
	public bool OnGameLoaded(bool loadedPaused, bool emulatorPaused = false)
	{
		lock(_lock) {
			if(Phase != PlayLoadWaitPhase.Opening) {
				return false;
			}
			bool paused = loadedPaused || (Kind == PlayLoadWaitKind.Reload && emulatorPaused);
			Volatile.Write(ref _frames, 0);
			SetPhase(paused ? PlayLoadWaitPhase.Idle : PlayLoadWaitPhase.WaitingForPicture);
			return !paused;
		}
	}

	//A blocking reload call returned (the in-place swap, Reload). Still
	//opening: the Core refused or failed it, no GameLoaded is coming. Paused:
	//no frame is coming. True when that ended the wait.
	public bool OnReloadReturned(int ticket, bool emulatorPaused)
	{
		lock(_lock) {
			if(Kind != PlayLoadWaitKind.Reload || ticket != Ticket) {
				return false;
			}
			if(Phase == PlayLoadWaitPhase.Opening || (Phase == PlayLoadWaitPhase.WaitingForPicture && emulatorPaused)) {
				SetPhase(PlayLoadWaitPhase.Idle);
				return true;
			}
			return false;
		}
	}

	//GameLoadFailed: a reload that failed (a power cycle of a file that is
	//gone). An open's failure is OnLoadReturned's, with its generation.
	public bool OnLoadFailed()
	{
		lock(_lock) {
			if(Kind != PlayLoadWaitKind.Reload || Phase != PlayLoadWaitPhase.Opening) {
				return false;
			}
			SetPhase(PlayLoadWaitPhase.Idle);
			return true;
		}
	}

	//The safety net: ends the wait the ticket names, whatever its phase.
	public bool Expire(int ticket)
	{
		lock(_lock) {
			if(ticket != Ticket || Phase == PlayLoadWaitPhase.Idle) {
				return false;
			}
			SetPhase(PlayLoadWaitPhase.Idle);
			return true;
		}
	}

	//PpuFrameDone. True exactly once: the picture is out, show the game.
	public bool OnFrameDone()
	{
		if(Volatile.Read(ref _phase) != (int)PlayLoadWaitPhase.WaitingForPicture) {
			return false;
		}
		if(Interlocked.Increment(ref _frames) != FramesUntilShown) {
			return false;
		}
		return Interlocked.CompareExchange(ref _phase, (int)PlayLoadWaitPhase.Idle, (int)PlayLoadWaitPhase.WaitingForPicture) == (int)PlayLoadWaitPhase.WaitingForPicture;
	}

	//The load call returned. Still opening means it never reached GameLoaded:
	//the open failed (W-P14 takes over). True when that ended the wait.
	public bool OnLoadReturned(int openGeneration)
	{
		lock(_lock) {
			if(Kind != PlayLoadWaitKind.Open || Phase != PlayLoadWaitPhase.Opening || openGeneration != OpenGeneration) {
				return false;
			}
			SetPhase(PlayLoadWaitPhase.Idle);
			return true;
		}
	}

	//A pause, a stop, or the timeout of the open it names. Only the picture
	//wait ends: switching games stops the old one while the new one opens.
	public bool EndPictureWait(int? openGeneration = null)
	{
		lock(_lock) {
			if(Phase != PlayLoadWaitPhase.WaitingForPicture || (openGeneration != null && openGeneration != OpenGeneration)) {
				return false;
			}
			SetPhase(PlayLoadWaitPhase.Idle);
			return true;
		}
	}

	private void SetPhase(PlayLoadWaitPhase phase) => Volatile.Write(ref _phase, (int)phase);
}
