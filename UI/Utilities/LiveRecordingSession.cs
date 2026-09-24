using Mesen.Config;
using Mesen.Interop;

namespace Mesen.Utilities
{
	//The live recording session as the UI drives it (ADR-0169 section 4): the
	//Core recorder publishing into the convention slot, and the ROM identity
	//that keeps it pointed at the game actually running.
	//
	//One place rather than one per caller: the Tools menu, the game-load
	//notification and the emulation-stopped notification all have to keep the
	//recorder and the ROM in step, and "recording the previous ROM while
	//playing the new one" is exactly the inconsistency this exists to prevent.
	//
	//The emulator launches no viewer (ADR-0169 section 4, amended 2026-09-23):
	//scripts/record_viewer.py is a developer/diagnostic tool, run by hand, that
	//auto-attaches to the same slot. The protocol is one-way file publishing,
	//so any number of hand-started viewers can watch it (section 1).
	public static class LiveRecordingSession
	{
		//Fixed by ADR-0169 section 4 ("the interval is fixed") - one less field
		//in a surface whose whole point is that nothing is typed.
		public const int IntervalMs = 250;

		public static bool IsRecording => RecordApi.LiveRecordingIsRecording();

		//Start publishing the running game into the convention slot. A refusal
		//(an already-running session, a bad slot) is logged here and, with its
		//reason, by the Core half.
		public static void Start()
		{
			AnnounceRom();
			string dir = ConfigManager.LiveRecordingFolder;
			bool started = RecordApi.LiveRecordingStart(dir, IntervalMs);
			EmuApi.WriteLogEntry("[LiveRecording] UI: start requested (" + dir + ", " + IntervalMs + "ms) -> " + (started ? "started" : "REFUSED"));
		}

		public static void Stop()
		{
			RecordApi.LiveRecordingStop();
			EmuApi.WriteLogEntry("[LiveRecording] UI: stop requested");
		}

		//Announce the game that just loaded, so the recorder re-targets its
		//slot instead of publishing a new game's frames beside the previous
		//one's CHR/palette (LiveFrameRecorder::SetRomName).
		public static void OnGameLoaded(RomInfo romInfo)
		{
			RecordApi.LiveRecordingSetRom(romInfo.GetRomName());
		}

		//No game, no session: with the ROM closed the recorder has nothing to
		//publish, and a "Stop" left enabled over an idle slot is the menu
		//inconsistency this avoids.
		public static void OnEmulationStopped()
		{
			RecordApi.LiveRecordingSetRom("");
			if(RecordApi.LiveRecordingIsRecording()) {
				RecordApi.LiveRecordingStop();
				EmuApi.WriteLogEntry("[LiveRecording] UI: stopped with the game");
			}
		}

		private static void AnnounceRom()
		{
			RomInfo romInfo = EmuApi.GetRomInfo();
			RecordApi.LiveRecordingSetRom(romInfo.GetRomName());
		}
	}
}
