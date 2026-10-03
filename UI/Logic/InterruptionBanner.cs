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
//banner: a recording that would end is the stop banner, anything else a
//warning; the button that goes on is dark for a quit and tinted for an open
//or a reload; the tint is the workspace whose work would be lost.
public static class InterruptionBanner
{
	public static BannerKind KindOf(InterruptionKind kind)
	{
		return kind is InterruptionKind.QuitWhileRecording or InterruptionKind.OpenWhileRecording or InterruptionKind.ReloadWhileRecording
			? BannerKind.Stop
			: BannerKind.Warning;
	}

	public static bool GoIsTinted(InterruptionKind kind)
	{
		return kind is InterruptionKind.OpenWhileRecording or InterruptionKind.ReloadWhileRecording or InterruptionKind.OpenWhileClassicBuilder;
	}

	public static Workspace WorkspaceOf(InterruptionKind kind)
	{
		return kind switch {
			InterruptionKind.QuitWhilePackaging => Workspace.Share,
			InterruptionKind.OpenWhileClassicBuilder => Workspace.Play,
			_ => Workspace.Remaster,
		};
	}
}
