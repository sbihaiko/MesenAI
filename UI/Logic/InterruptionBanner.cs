namespace Mesen.Logic;

//ADR-0249 (W-X1, W-X2, W-X3): the shared in-place banner's three looks -
//a pale orange warning, a pale blue question, a pale red stop.
public enum BannerKind
{
	Warning,
	Info,
	Stop
}

//How the interruption question (RemasterInterruptions.cs) is drawn as that
//banner: a recording that would end, or a game's unsaved progress (Player
//mode's Quit game / quit questions), is the stop banner, anything else a
//warning; the button that goes on is dark for a quit and tinted for an open
//or a reload (#732's Reload Without Patch included); the tint is the
//workspace whose work would be lost.
public static class InterruptionBanner
{
	public static BannerKind KindOf(InterruptionKind kind)
	{
		return kind is InterruptionKind.QuitWhileRecording or InterruptionKind.OpenWhileRecording or InterruptionKind.ReloadWhileRecording
			or InterruptionKind.QuitGame or InterruptionKind.QuitApp
			? BannerKind.Stop
			: BannerKind.Warning;
	}

	public static bool GoIsTinted(InterruptionKind kind)
	{
		return kind is InterruptionKind.OpenWhileRecording or InterruptionKind.ReloadWhileRecording or InterruptionKind.OpenWhileClassicBuilder
			or InterruptionKind.ForcedPatch;
	}

	public static Workspace WorkspaceOf(InterruptionKind kind)
	{
		return kind switch {
			InterruptionKind.QuitWhilePackaging => Workspace.Share,
			InterruptionKind.OpenWhileClassicBuilder or InterruptionKind.QuitGame or InterruptionKind.QuitApp or InterruptionKind.ForcedPatch => Workspace.Play,
			_ => Workspace.Remaster,
		};
	}
}
