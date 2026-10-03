using System.IO;

namespace Mesen.Logic;

//G.6 (PRD Part B §13.5.5 W-X3, rule 7): only lost work asks, once, in place,
//in the W-X1 shape. Quitting asks while a recording, a Remaster job or a
//Share pack job (#650) runs.
//Opening another game asks while a recording runs - Remaster's, or the HD
//Pack Builder (classic) one, which used to stop silently. A job is a separate
//process and keeps running when another game opens (its result is then not
//shown, RemasterShow.Decide). Reloading the running game asks while a
//Remaster recording runs (#698). Switching profile, opening a sheet or changing
//workspace never asks.
public enum InterruptionKind
{
	None,
	QuitWhileRecording,
	QuitWhileJob,
	//#650: Share's pack job (mep_build.py pack) is a job too.
	QuitWhilePackaging,
	OpenWhileRecording,
	OpenWhileClassicBuilder,
	//#698: Reload ROM, Power Cycle, a pack switch or pick, while recording.
	ReloadWhileRecording
}

public static class Interruptions
{
	//jobRunning: Remaster's job; packaging: Share's (#650). Both are children
	//that would outlive the app, so quitting asks and stops them.
	public static InterruptionKind ForQuit(bool recording, bool jobRunning, bool packaging = false)
	{
		return recording ? InterruptionKind.QuitWhileRecording
			: jobRunning ? InterruptionKind.QuitWhileJob
			: packaging ? InterruptionKind.QuitWhilePackaging
			: InterruptionKind.None;
	}

	public static InterruptionKind ForOpen(bool recording, bool classicBuilderRecording)
	{
		return recording ? InterruptionKind.OpenWhileRecording
			: classicBuilderRecording ? InterruptionKind.OpenWhileClassicBuilder
			: InterruptionKind.None;
	}

	//#698: Reload ROM, Power Cycle, a pack switch or a pack pick reload the
	//running game, and the core ends its recording with the ROM it was
	//recording - so a reload asks too, like opening another game.
	public static InterruptionKind ForReload(bool recording)
	{
		return recording ? InterruptionKind.ReloadWhileRecording : InterruptionKind.None;
	}

	public static bool Quits(InterruptionKind kind)
	{
		return kind == InterruptionKind.QuitWhileRecording || kind == InterruptionKind.QuitWhileJob || kind == InterruptionKind.QuitWhilePackaging;
	}

	public static string GameName(string path)
	{
		return string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);
	}
}
