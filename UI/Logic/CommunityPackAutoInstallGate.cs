namespace Mesen.Logic;

//ADR-0146: every accepted community pack auto-installs "whenever possible" -
//the master switch is on and the user has not disabled it. Two user disables
//override the blanket rule: a per-pack one (DisabledPacks, by container) and
//W-P5's per-ROM "No pack" (PackPreferenceResolver.NoPack), which would
//otherwise download a pack the core will not render and raise the install
//pill over a game the player chose to play without one. Host-free so UI.Tests
//pins the order; CommunityPackInstallCoordinator.EvaluateGates asks it first.
public static class CommunityPackAutoInstallGate
{
	//null when the install may proceed; else the Skipped reason it logs.
	public static string? SkipReason(bool autoInstallOn, bool containerDisabled, bool romPrefersNoPack)
	{
		if(!autoInstallOn) {
			return "AutoInstallCommunityPacks is off";
		}
		if(containerDisabled) {
			return "pack disabled by user";
		}
		if(romPrefersNoPack) {
			return "no pack chosen for this game";
		}
		return null;
	}
}
