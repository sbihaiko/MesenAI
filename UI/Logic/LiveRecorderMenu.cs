using System.Collections.Generic;

namespace Mesen.Logic;

//ADR-0169 section 4, amended 2026-09-23: the Tools > Live Recorder submenu
//drives the recorder only - Record and Stop, shaped like the Sound/Video/Music
//recorders above it. There is no viewer entry: scripts/record_viewer.py is a
//developer/diagnostic tool run by hand, never launched by the emulator UI.
//
//This is the host-free list MainMenuViewModel.GetLiveRecorderMenu builds the
//submenu from, one MainMenuAction per entry and in this order, so the policy
//is asserted in UI.Tests (UI.Tests/Recording/LiveRecorderMenuTests.cs) and
//the realized menu in UI.HeadlessTests/LiveRecorderMenuTests.cs.
public static class LiveRecorderMenu
{
	public enum Entry
	{
		Record,
		Stop
	}

	public static IReadOnlyList<Entry> Entries { get; } = new Entry[] { Entry.Record, Entry.Stop };
}
