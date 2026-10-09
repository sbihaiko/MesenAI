using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play;

//#1109 (spec #1102, ADR-0254's controller reason): while a game runs unpaused in
//Play and the connected-pad count goes down, the game pauses into W-P4 with
//"controller disconnected" written on it. Reconnecting rewrites the line and
//never resumes. The rules are host-free in UI/Logic/PadLossPause;
//MainWindowViewModel.TickPadLoss is the only caller.
public class PadLossPauseTests
{
	//previous, current, isPlayDoor, gameLoaded, paused, expected
	[Theory]
	[InlineData(2u, 1u, true, true, false, true)]   //running: one of two pads gone
	[InlineData(1u, 0u, true, true, false, true)]   //running: the last pad gone
	[InlineData(2u, 1u, true, true, true, false)]   //already paused: stays as it is
	[InlineData(2u, 1u, true, false, false, false)] //no game loaded: nothing to pause
	[InlineData(2u, 1u, false, true, false, false)] //Classic: no W-P4 to open
	[InlineData(1u, 1u, true, true, false, false)]  //same count
	[InlineData(1u, 2u, true, true, false, false)]  //a pad arrived
	public void Only_a_falling_pad_count_under_a_running_game_in_Play_pauses(uint previous, uint current, bool isPlayDoor, bool gameLoaded, bool paused, bool expected)
	{
		Assert.Equal(expected, PadLossPause.ShouldPause(previous, current, isPlayDoor, gameLoaded, paused));
	}

	[Theory]
	[InlineData(PadPauseReason.None, 1u, 2u, PadPauseReason.None)]
	[InlineData(PadPauseReason.ControllerDisconnected, 0u, 1u, PadPauseReason.ControllerReconnected)]
	[InlineData(PadPauseReason.ControllerDisconnected, 2u, 1u, PadPauseReason.ControllerDisconnected)]
	[InlineData(PadPauseReason.ControllerReconnected, 1u, 0u, PadPauseReason.ControllerReconnected)]
	public void A_pad_coming_back_rewrites_the_reason_and_nothing_else(PadPauseReason reason, uint previous, uint current, PadPauseReason expected)
	{
		Assert.Equal(expected, PadLossPause.AfterCountChange(reason, previous, current));
	}
}
