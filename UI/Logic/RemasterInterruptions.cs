using System.IO;

namespace Mesen.Logic;

//G.6 (PRD Part B §13.5.5 W-X3, rule 7): only lost work asks, once, in place,
//in the W-X1 shape. Quitting asks while a recording or a Remaster job runs.
//Opening another game asks while a recording runs - Remaster's, or the HD
//Pack Builder (classic) one, which used to stop silently. A job is a separate
//process and keeps running when another game opens (its result is then not
//shown, RemasterShow.Decide). Switching profile, opening a sheet or changing
//workspace never asks.
public enum InterruptionKind
{
	None,
	QuitWhileRecording,
	QuitWhileJob,
	OpenWhileRecording,
	OpenWhileClassicBuilder
}

public static class Interruptions
{
	public static InterruptionKind ForQuit(bool recording, bool jobRunning)
	{
		return recording ? InterruptionKind.QuitWhileRecording
			: jobRunning ? InterruptionKind.QuitWhileJob
			: InterruptionKind.None;
	}

	public static InterruptionKind ForOpen(bool recording, bool classicBuilderRecording)
	{
		return recording ? InterruptionKind.OpenWhileRecording
			: classicBuilderRecording ? InterruptionKind.OpenWhileClassicBuilder
			: InterruptionKind.None;
	}

	public static bool Quits(InterruptionKind kind)
	{
		return kind == InterruptionKind.QuitWhileRecording || kind == InterruptionKind.QuitWhileJob;
	}

	public static string GameName(string path)
	{
		return string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);
	}
}
