using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Recording
{
	//ADR-0169 section 4, amended 2026-09-23: Tools > Live Recorder offers
	//Record and Stop only; scripts/record_viewer.py is a developer/diagnostic
	//tool run by hand, so no menu entry opens it. MainMenuViewModel builds the
	//submenu from LiveRecorderMenu.Entries; the realized MenuItem tree is
	//checked in UI.HeadlessTests/LiveRecorderMenuTests.cs.
	public class LiveRecorderMenuTests
	{
		[Fact]
		public void Entries_AreExactlyRecordThenStop()
		{
			Assert.Equal(new[] { LiveRecorderMenu.Entry.Record, LiveRecorderMenu.Entry.Stop }, LiveRecorderMenu.Entries.ToArray());
		}

		[Fact]
		public void NoEntry_OpensAViewer()
		{
			//The enum itself, not just the list: a viewer value parked in the
			//enum is one line away from coming back into the menu.
			Assert.DoesNotContain(Enum.GetNames<LiveRecorderMenu.Entry>(), n => n.Contains("Viewer", StringComparison.OrdinalIgnoreCase));
			Assert.Equal(Enum.GetValues<LiveRecorderMenu.Entry>().Length, LiveRecorderMenu.Entries.Count);
		}
	}
}
