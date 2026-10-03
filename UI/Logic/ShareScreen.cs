using Mesen.Interop;

namespace Mesen.Logic
{
	//G.8 (PRD Part B §13.5.4, §13.6): Share's own graph. W-H1 is the home with
	//two cards (pack, replay - ADR-0205 keeps the pipelines apart); W-H2 and W-H3
	//are pages with ‹ Share; W-H4 is two sheets over the home and, between
	//them, the game shown inside Share while it records.
	public enum ShareView
	{
		Home,
		Pack,
		Project
	}

	public enum ReplaySheet
	{
		None,
		//"Record and share — <game>": Cancel / Start Recording.
		Start,
		//The game fills the window inside Share, one pill.
		Recording,
		//"Replay saved": Show in Finder / Continue on GitHub ↗.
		Saved
	}

	//Why W-H4's Start Recording is disabled (rule 4).
	public enum ReplayStartReason
	{
		None,
		NoGame,
		ConsoleNotSupported,
		//A movie is already recording or playing (Classic › Tools › Movies).
		MovieBusy,
		Netplay
	}

	public enum ShareEscAction
	{
		None,
		CloseProjectList,
		CloseSheet,
		StopRecording
	}

	//The Record-and-share session as Share drives it (ADR-0205 §2/§6). The
	//real one is UI/Utilities/ShareRecordingSession; the tests use a fake.
	public interface IReplayRecorder
	{
		//The core still records the shared replay (it ends one by itself when
		//a save state loads or the ROM goes away).
		bool IsSharing { get; }

		//Starts from power-on; the file it writes, or null when refused.
		string? Start();

		//Stops, and returns the file when it was a share session that wrote
		//one - without revealing it or opening the browser (W-H4's second
		//sheet has a button for each).
		string? StopAndKeep();

		//ReplayShare.BuildIssueUrl with the recorder's description.
		string IssueUrl();
	}

	public static class ShareReplay
	{
		//Mirror of Core/Shared/Movies/ShareRecordingSettings.h IsSupported: the
		//consoles whose power-on state the share recording can make
		//deterministic. Anything else is refused by the core
		//("MovieShareUnsupportedConsole"); W-H4 says so before Start instead.
		public static bool IsSupported(ConsoleType console)
		{
			return console switch {
				ConsoleType.Snes or ConsoleType.Gameboy or ConsoleType.Nes or ConsoleType.PcEngine or ConsoleType.Sms or ConsoleType.Gba => true,
				_ => false
			};
		}

		//The same gate as the Share home's Record and share, plus the console
		//check the core would otherwise make after the click.
		public static ReplayStartReason StartReason(bool gameRunning, ConsoleType console, bool movieBusy, bool netplay)
		{
			if(!gameRunning) {
				return ReplayStartReason.NoGame;
			}
			if(!IsSupported(console)) {
				return ReplayStartReason.ConsoleNotSupported;
			}
			if(movieBusy) {
				return ReplayStartReason.MovieBusy;
			}
			return netplay ? ReplayStartReason.Netplay : ReplayStartReason.None;
		}
	}

	//Rule 8 in Share: Esc closes the topmost panel. While recording it stops
	//the recording (as W-R2); on a page with nothing open it does nothing -
	//going back is ‹ Share, and navigation never needs a confirmation.
	public static class ShareEsc
	{
		//While a replay starts or stops (RecordingTransition) Esc waits for
		//the core's answer, like the buttons.
		public static ShareEscAction Next(ReplaySheet sheet, bool projectListOpen, RecordingTransition transition)
		{
			return RecordingTransitions.AcceptsClick(transition) ? Next(sheet, projectListOpen) : ShareEscAction.None;
		}

		public static ShareEscAction Next(ReplaySheet sheet, bool projectListOpen)
		{
			if(sheet == ReplaySheet.Recording) {
				return ShareEscAction.StopRecording;
			}
			if(sheet != ReplaySheet.None) {
				return ShareEscAction.CloseSheet;
			}
			return projectListOpen ? ShareEscAction.CloseProjectList : ShareEscAction.None;
		}
	}
}
