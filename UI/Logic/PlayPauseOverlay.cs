using System;
using System.Collections.Generic;

namespace Mesen.Logic;

//G.2 (PRD Part B §8, ADR-0241, §13.5.2 W-P4): the redesigned pause overlay's
//host-free rules - its seven controls, where each action of the P.4 overlay it
//replaces lives now, the row values, and the Esc order (rule 8).

//W-P4's controls, top to bottom. Rule 2 caps a screen at seven; a new pause
//item has to replace or merge one of these (§13.5.2, user's decision 2026-10-02).
public enum PauseOverlayControl
{
	Resume,
	SaveStates,
	Pack,
	Enhancements,
	Cheats,
	Settings,
	QuitGame
}

//Every action today's (P.4 + P.7 + P.10) overlay offered.
public enum FormerOverlayAction
{
	Resume,
	SaveSlot,
	LoadSlot,
	Pack,
	Enhancements,
	Cheats,
	Settings,
	AdvancedGui,
	QuitApp
}

//Where a former overlay action is reached in W-P4: either one of its rows, or
//(when the wireframe moved it out) the Tools ⋯ path the overlay reveals by
//showing the shell bar.
public sealed record PauseOverlayDestination(PauseOverlayControl? Control, string ToolsPath);

public static class PauseOverlay
{
	public const int MaxControls = 7;

	public static IReadOnlyList<PauseOverlayControl> Controls { get; } = new[] {
		PauseOverlayControl.Resume,
		PauseOverlayControl.SaveStates,
		PauseOverlayControl.Pack,
		PauseOverlayControl.Enhancements,
		PauseOverlayControl.Cheats,
		PauseOverlayControl.Settings,
		PauseOverlayControl.QuitGame
	};

	//W-P4's "Differences from today's overlay": Save and Load merged into one
	//Save states row; Advanced GUI is gone because Tools ⋯ sits in the bar the
	//overlay reveals (the UiMode choice is in Settings › Preferences there);
	//Quit no longer closes the app - that is Tools ⋯ › File › Exit (and the OS's
	//own ⌘Q / Alt+F4), while the row powers the game off.
	public static PauseOverlayDestination WhereNow(FormerOverlayAction action)
	{
		return action switch {
			FormerOverlayAction.Resume => new(PauseOverlayControl.Resume, ""),
			FormerOverlayAction.SaveSlot => new(PauseOverlayControl.SaveStates, ""),
			FormerOverlayAction.LoadSlot => new(PauseOverlayControl.SaveStates, ""),
			FormerOverlayAction.Pack => new(PauseOverlayControl.Pack, ""),
			FormerOverlayAction.Enhancements => new(PauseOverlayControl.Enhancements, ""),
			FormerOverlayAction.Cheats => new(PauseOverlayControl.Cheats, ""),
			FormerOverlayAction.Settings => new(PauseOverlayControl.Settings, ""),
			FormerOverlayAction.AdvancedGui => new(null, "Tools ⋯ › Settings › Preferences"),
			FormerOverlayAction.QuitApp => new(null, "Tools ⋯ › File › Exit"),
			_ => throw new ArgumentOutOfRangeException(nameof(action))
		};
	}

	//The Enhancements row's "N on": the §6.1 quick toggles that are on. A toggle
	//the loaded console cannot use (Overclock on SMS) is never counted.
	public static int EnhancementsOn(bool textures, bool audio, bool border, bool wideScreen, bool overclock, bool overclockSupported)
	{
		int count = 0;
		foreach(bool on in new[] { textures, audio, border, wideScreen, overclock && overclockSupported }) {
			if(on) {
				count++;
			}
		}
		return count;
	}
}

//The Save states row's value: the most recently written slot and its age.
public sealed record SaveStateSlotSummary(int Slot, TimeSpan Age);

public enum SaveStateAgeKind
{
	JustNow,
	Minutes,
	Hours,
	Days
}

public static class SaveStatesSummary
{
	//slots: slot number and the slot file's write time (null = no file).
	//Returns null when no slot holds a state (the row reads "empty").
	public static SaveStateSlotSummary? Newest(IEnumerable<(int Slot, DateTime? Written)> slots, DateTime now)
	{
		int bestSlot = 0;
		DateTime best = DateTime.MinValue;
		foreach((int slot, DateTime? written) in slots) {
			if(written.HasValue && written.Value > best) {
				best = written.Value;
				bestSlot = slot;
			}
		}
		if(bestSlot == 0) {
			return null;
		}
		TimeSpan age = now - best;
		return new SaveStateSlotSummary(bestSlot, age < TimeSpan.Zero ? TimeSpan.Zero : age);
	}

	//"2 min ago" reads in the largest whole unit; under a minute is "just now".
	public static (SaveStateAgeKind Kind, int Count) Age(TimeSpan age)
	{
		if(age.TotalMinutes < 1) {
			return (SaveStateAgeKind.JustNow, 0);
		}
		if(age.TotalHours < 1) {
			return (SaveStateAgeKind.Minutes, (int)age.TotalMinutes);
		}
		if(age.TotalDays < 1) {
			return (SaveStateAgeKind.Hours, (int)age.TotalHours);
		}
		return (SaveStateAgeKind.Days, (int)age.TotalDays);
	}
}

//What is on top of the game in Play, for the Esc router.
public enum PlaySheet
{
	None,
	//W-P5, opened by itself over an un-enhanced first start (§5).
	PackPickerOnLoad,
	//W-P5 opened from W-P4's Pack row.
	PackPickerFromOverlay,
	Enhancements,
	Cheats,
	SaveStates,
	//Today's slot grid (GameScreenMode.SaveState/LoadState) opened from the
	//Save states sheet.
	SaveStateGrid,
	//G.4: W-P6, the current pack's detail, opened from W-P4's Pack row.
	PackDetail,
	//G.5 W-P16: a pack waits for a file, opened with the overlay.
	PackDep,
	//R.2 (ADR-0205 §7): Shared replays, opened from the Save states sheet
	//(W-P4 is at its seven controls, so the list merges into that row).
	Replays
}

public enum PlayEscAction
{
	None,
	//The un-enhanced first-start picker: dismissed, nothing stored (§5).
	DismissPackPicker,
	//Rule 8: a sheet opened from the pause overlay closes back to it.
	CloseSheetToOverlay,
	CloseOverlayAndResume,
	OpenOverlayAndPause
}

//Rule 8 / W-P4: Esc does one thing per context. The order is game → W-P4 →
//resume; a sheet opened from W-P4 returns to W-P4 and the next Esc resumes.
//On the Play home (no game) Esc does nothing - there is nothing to pause.
public static class PlayEsc
{
	public static PlayEscAction Next(bool gameLoaded, PlaySheet sheet, bool overlayVisible)
	{
		switch(sheet) {
			case PlaySheet.PackPickerOnLoad:
				return PlayEscAction.DismissPackPicker;
			case PlaySheet.PackPickerFromOverlay:
			case PlaySheet.Enhancements:
			case PlaySheet.Cheats:
			case PlaySheet.SaveStates:
			case PlaySheet.SaveStateGrid:
			case PlaySheet.PackDetail:
			case PlaySheet.PackDep:
			case PlaySheet.Replays:
				return PlayEscAction.CloseSheetToOverlay;
		}
		if(overlayVisible) {
			return PlayEscAction.CloseOverlayAndResume;
		}
		return gameLoaded ? PlayEscAction.OpenOverlayAndPause : PlayEscAction.None;
	}
}

//#639: the pause overlay and the sheets opened from it belong to the game
//they were opened for. Opening another ROM directly (A → B) stops A without
//EmulationStopped, so the game changes without passing through "no game":
//the surfaces close on any change of game, and when it is gone. A reload of
//the same file (power cycle, an in-place pack change) keeps them. The game is
//the ROM path as loaded (an archive's inner file included).
public static class PlaySurfaceGame
{
	public static bool ClosesSurfaces(bool wasLoaded, string wasRomPath, bool isLoaded, string isRomPath)
	{
		return !wasLoaded || !isLoaded || !string.Equals(wasRomPath, isRomPath, StringComparison.Ordinal);
	}
}
