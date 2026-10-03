using System;
using System.Diagnostics;
using System.Threading;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #629. Steps the debugger a number of NES frame periods and returns once
//the step has run out and the debugger broke on it.
//
//The tests that needed this used to `Step; Thread.Sleep(300); EmuApi.Pause()`.
//With the debugger attached, Emulator::Pause is itself a one-instruction Step
//that replaces whatever step is still pending, so on a loaded machine it cut
//the frame step short: CopyAfterStateLoadTests then saw the trace still
//NotDrawnSinceLoad. Debugger::Step clears the paused flag before it returns
//and the break at the end of the step sets it again, so polling
//EmuApi.IsPaused waits for exactly this step, however slow the machine.
internal static class DebuggerStep
{
	private const int TimeoutMs = 20000;

	//The caller must have called DebugApi.InitializeDebugger.
	public static void Frames(int frames)
	{
		DebugApi.Step(CpuType.Nes, frames, StepType.PpuFrame);
		WaitForBreak($"a {frames}-frame step");
	}

	//Also for an EmuApi.Pause() under the debugger, which is a one-instruction step.
	public static void WaitForBreak(string what)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!EmuApi.IsPaused()) {
			Assert.True(clock.ElapsedMilliseconds < TimeoutMs, $"the debugger never broke after {what}");
			Thread.Sleep(10);
		}
	}
}
