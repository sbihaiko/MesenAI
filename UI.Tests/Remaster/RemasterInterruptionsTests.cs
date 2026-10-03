using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//G.6 (PRD Part B §13.5.5 W-X3, rule 7): only lost work asks. Quitting asks
	//while a recording or a job runs; opening another game asks while a
	//recording (Remaster's, or HD Pack Builder classic's) runs - a job is a
	//separate process and keeps running; nothing else ever asks.
	public class RemasterInterruptionsTests
	{
		[Theory]
		[InlineData(false, false, InterruptionKind.None)]
		[InlineData(true, false, InterruptionKind.QuitWhileRecording)]
		[InlineData(false, true, InterruptionKind.QuitWhileJob)]
		[InlineData(true, true, InterruptionKind.QuitWhileRecording)]
		public void Quitting_asks_only_while_work_runs(bool recording, bool job, InterruptionKind kind)
		{
			Assert.Equal(kind, Interruptions.ForQuit(recording, job));
		}

		[Theory]
		[InlineData(false, false, InterruptionKind.None)]
		[InlineData(true, false, InterruptionKind.OpenWhileRecording)]
		[InlineData(false, true, InterruptionKind.OpenWhileClassicBuilder)]
		[InlineData(true, true, InterruptionKind.OpenWhileRecording)]
		public void Opening_a_game_asks_only_while_a_recording_runs(bool recording, bool classicBuilder, InterruptionKind kind)
		{
			Assert.Equal(kind, Interruptions.ForOpen(recording, classicBuilder));
		}

		//#698: Reload ROM, Power Cycle, a pack switch or pick reload the running
		//game, which ends its recording like opening another game would.
		[Theory]
		[InlineData(false, InterruptionKind.None)]
		[InlineData(true, InterruptionKind.ReloadWhileRecording)]
		public void Reloading_the_game_asks_only_while_a_recording_runs(bool recording, InterruptionKind kind)
		{
			Assert.Equal(kind, Interruptions.ForReload(recording));
		}

		[Theory]
		[InlineData(InterruptionKind.None, false)]
		[InlineData(InterruptionKind.QuitWhileRecording, true)]
		[InlineData(InterruptionKind.QuitWhileJob, true)]
		[InlineData(InterruptionKind.OpenWhileRecording, false)]
		[InlineData(InterruptionKind.OpenWhileClassicBuilder, false)]
		[InlineData(InterruptionKind.ReloadWhileRecording, false)]
		public void Quit_kinds_quit_and_open_kinds_open(InterruptionKind kind, bool quits)
		{
			Assert.Equal(quits, Interruptions.Quits(kind));
		}

		[Theory]
		[InlineData("C:/roms/Castlevania (USA).nes", "Castlevania (USA)")]
		[InlineData("/roms/Castlevania.zip", "Castlevania")]
		[InlineData("", "")]
		public void The_question_names_the_game_by_its_file(string path, string name)
		{
			Assert.Equal(name, Interruptions.GameName(path));
		}
	}
}
