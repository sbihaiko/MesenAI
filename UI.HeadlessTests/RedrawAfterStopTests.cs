using System;
using System.IO;
using Mesen.Config;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #616. Settings › Look (P.13, ADR-0246) redraws the paused frame after a
//pick or a Hold to Compare, through EmuApi.RedrawPausedFrame. Emulator::Stop
//releases the console but leaves the pause flag set, and the video decoder kept
//the last frame - a pointer into the PPU buffer the console just freed - plus
//the console's own video filter. A redraw after a game paused and then stopped
//therefore ran NesDefaultVideoFilter::OnBeforeApplyFilter on a null console and
//killed the process with SIGSEGV. The full headless suite hit it whenever a
//class that pauses and stops the synthetic NROM (CopyAfterStateLoadTests) ran
//before LookSettingsTabTests in the serial collection; the GUI reaches it when
//a person pauses, quits the game and changes the look.
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
[Collection(NativeCoreCollection.Name)]
public class RedrawAfterStopTests
{
	[Fact]
	public void A_redraw_after_a_paused_game_stopped_draws_nothing_and_does_not_crash()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		string folder = Path.Combine(Path.GetTempPath(), "mesen-616-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		File.WriteAllBytes(rom, SyntheticNrom.Build());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			//The sequence CopyAfterStateLoadTests runs: frames drawn under the
			//debugger, paused, debugger released, game stopped. The decoder holds
			//the last frame and the pause flag survives the stop.
			DebugApi.InitializeDebugger();
			DebuggerStep.Frames(3);
			DebugApi.ReleaseDebugger();
			EmuApi.Stop();
			Assert.False(EmuApi.IsRunning());

			//Both entry points Settings › Look uses. Before the fix the first one
			//crashed the test host (exit 139).
			EmuApi.RedrawPausedFrame();
			EmuApi.SetLookCompare(true);
			EmuApi.SetLookCompare(false);
			Assert.False(EmuApi.IsRunning());
		} finally {
			DebugApi.ReleaseDebugger();
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
			try {
				Directory.Delete(folder, true);
			} catch(IOException) {
			}
		}
	}
}
