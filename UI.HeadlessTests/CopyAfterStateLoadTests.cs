using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Mesen.Config;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #419 (ADR-0215, amended 2026-09-24), end to end through the real core.
//A save state does not carry the per-scanline trace the copy actions resolve a
//key through, so after a state load - with no frame emulated since - the trace
//still describes the frame drawn BEFORE the load. F14.2 measured the result:
//Dr. Mario's cell (0,0) copied as 1276 instead of 252, and Super Mario Bros.
//and Zelda copied nothing, all silent. The GUI reaches the same state when a
//person pauses, loads a state and copies without unpausing.
//
//UI.Tests/Mep/NesDrawnTileResolverTests.cs covers the refusal host-free; this
//covers the half only the core can: that a real LoadStateFile marks the trace
//stale, that the copy refuses on it out loud, and that one drawn frame clears
//it. It needs no ROM from the library: the ROM is the copyright-free synthetic
//NROM scripts/gen_synthetic_nrom.py describes (an infinite loop, blank CHR),
//built here byte for byte. Its rendering stays off, but the trace is captured
//at cycle 257 of every scanline regardless, so a drawn frame still resolves -
//every scanline draws nametable 0 row 0 from column 0.
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
public class CopyAfterStateLoadTests
{
	private static readonly UInt32[] Palette = new UInt32[32];

	[Fact]
	public void A_copy_after_a_state_load_refuses_until_a_frame_is_drawn()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		string folder = Path.Combine(Path.GetTempPath(), "mesen-419-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		string state = Path.Combine(folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, BuildSyntheticNrom());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			DebugApi.InitializeDebugger();

			DrawFrames(3);
			Assert.Equal(NesScanlineTraceStatus.Current, TraceStatus());

			EmuApi.SaveStateFile(state);
			Assert.True(WaitFor(() => File.Exists(state) && new FileInfo(state).Length > 0), $"no state was written to {state}");
			DrawFrames(3);

			EmuApi.LoadStateFile(state);
			EmuApi.Pause();

			Assert.Equal(NesScanlineTraceStatus.NotDrawnSinceLoad, TraceStatus());
			HdPackCopyResult stale = CopyCellZero();
			Assert.False(stale.Copied, $"the copy resolved through a trace from before the state load: {stale.Text}");
			Assert.Contains("not copied", stale.Receipt);
			Assert.Contains("state", stale.Receipt);

			//One frame period from a state saved mid-frame ends mid-frame, before
			//a whole frame was traced, so a person may need a second step - the
			//refusal names the step, and repeating it is safe.
			for(int i = 0; i < 3 && TraceStatus() != NesScanlineTraceStatus.Current; i++) {
				DrawFrames(1);
			}
			Assert.Equal(NesScanlineTraceStatus.Current, TraceStatus());
			HdPackCopyResult drawn = CopyCellZero();
			Assert.True(drawn.Copied, drawn.Receipt);
			Assert.Contains("\"index\": 0", drawn.Text);
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

	private static HdPackCopyResult CopyCellZero()
	{
		return HdPackCopyHelper.CopyAsMepSheetCell(
			0x0000, MemoryType.NesPpuMemory, Palette, 0, false, HdPackCopyContext.ForTilemap(0x2000));
	}

	private static NesScanlineTraceStatus TraceStatus()
	{
		return DebugApi.GetNesScanlineTrace(out _, out _);
	}

	//A deterministic number of frame periods (the same Step ChrBankDiagnosticTests
	//uses), then paused again once the step has run out.
	private static void DrawFrames(int frames)
	{
		DebugApi.Step(CpuType.Nes, frames, StepType.PpuFrame);
		Thread.Sleep(300);
		EmuApi.Pause();
		Thread.Sleep(100);
	}

	private static bool WaitFor(Func<bool> condition)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(clock.ElapsedMilliseconds < 5000) {
			if(condition()) {
				return true;
			}
			Thread.Sleep(20);
		}
		return condition();
	}

	//scripts/gen_synthetic_nrom.py, byte for byte: NROM-256, a JMP-to-self at
	//the reset vector, an RTI for NMI/IRQ, and 8 KB of blank CHR ROM.
	private static byte[] BuildSyntheticNrom()
	{
		const int prgSize = 32 * 1024;
		const int chrSize = 8 * 1024;
		byte[] rom = new byte[16 + prgSize + chrSize];
		rom[0] = (byte)'N';
		rom[1] = (byte)'E';
		rom[2] = (byte)'S';
		rom[3] = 0x1A;
		rom[4] = prgSize / 16384;
		rom[5] = chrSize / 8192;

		int prg = 16;
		for(int i = 0; i < prgSize; i++) {
			rom[prg + i] = 0xEA;
		}
		rom[prg + 0x0000] = 0x4C;
		rom[prg + 0x0001] = 0x00;
		rom[prg + 0x0002] = 0x80;
		rom[prg + 0x0003] = 0x40;
		rom[prg + 0x7FFA] = 0x03;
		rom[prg + 0x7FFB] = 0x80;
		rom[prg + 0x7FFC] = 0x00;
		rom[prg + 0x7FFD] = 0x80;
		rom[prg + 0x7FFE] = 0x03;
		rom[prg + 0x7FFF] = 0x80;
		return rom;
	}
}
