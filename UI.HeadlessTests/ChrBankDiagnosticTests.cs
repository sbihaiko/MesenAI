using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #341 / ADR-0215 evidence harness. Not a contract on the shipped copy
//action - it asserts nothing about it, and there is no assertion here that a
//fix would have to keep passing. What it does is put the two CHR mappings side
//by side for one paused frame, so the claim "the copy names the tile with the
//mapping the pause happens to hold, not the one that drew the frame" is a
//measurement rather than an argument:
//
//  - the paused `_chrPages` map, one 256-byte PPU page at a time, read through
//    the same DebugApi.GetAbsoluteAddress the copy goes through;
//  - every nametable cell of the visible screen, with the CHR index the copy
//    would emit for it;
//  - after letting the core render from that same state, the per-scanline CHR
//    mapping the PPU really drew under (BaseNesPpu::GetScanlineChrBankTrace,
//    ADR-0169), collapsed to its distinct bands.
//
//  - since ADR-0215 was decided, a `drawn` line per cell carrying the paused
//    index and the one NesDrawnTileResolver returns, side by side.
//
//Skips unless MESEN_DIAG_ROM and MESEN_DIAG_OUT are set (MESEN_DIAG_STATE is
//optional; without it the dump describes whatever two emulated seconds leave;
//MESEN_DIAG_STEP_FRAMES sets how many frames are rendered before the trace is
//read, and on a per-vblank-banking game the answer depends on it - see the Step
//call), so an ordinary run and CI are unchanged. It stays evidence, not a
//regression test: the contract on the decision is UI.Tests/Mep/
//NesDrawnTileResolverTests.cs, which needs no ROM and no state.
public class ChrBankDiagnosticTests
{
	//How many frames to render from the restored state before reading the trace.
	//At least one is mandatory and the exact value matters - see the Step call's
	//own comment. MESEN_DIAG_STEP_FRAMES overrides it, because on a game that
	//rewrites its CHR bank every vblank the answer alternates with this number
	//and the only honest thing is to let the reader sweep it.
	private static int FramesStepped =>
		int.TryParse(Environment.GetEnvironmentVariable("MESEN_DIAG_STEP_FRAMES"), out int n) && n > 0 ? n : 2;

	[DllImport("MesenCore")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool HeadlessCaptureNesSpriteLayer(
		IntPtr oam, IntPtr palette, IntPtr chr, IntPtr nametables, IntPtr ppuState,
		IntPtr outHasChrLatch, IntPtr outChrLatchPageSize,
		IntPtr outLeftFdBank, IntPtr outLeftFeBank, IntPtr outRightFdBank, IntPtr outRightFeBank,
		IntPtr outChrFull, uint maxChrFullSize, IntPtr outChrFullSize,
		IntPtr outScanlineScroll, IntPtr outScanlineChrBank);

	private static uint[] ScanlineChrBankTrace()
	{
		uint[] trace = new uint[240 * 32];
		IntPtr buffer = Marshal.AllocHGlobal(trace.Length * sizeof(uint));
		try {
			bool ok = HeadlessCaptureNesSpriteLayer(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0,
				IntPtr.Zero, IntPtr.Zero, buffer);
			Assert.True(ok, "HeadlessCaptureNesSpriteLayer refused");
			for(int i = 0; i < trace.Length; i++) {
				trace[i] = (uint)Marshal.ReadInt32(buffer, i * sizeof(uint));
			}
		} finally {
			Marshal.FreeHGlobal(buffer);
		}
		return trace;
	}

	[Fact]
	public void Dump_the_chr_mapping_the_debugger_sees()
	{
		string rom = Environment.GetEnvironmentVariable("MESEN_DIAG_ROM") ?? "";
		string state = Environment.GetEnvironmentVariable("MESEN_DIAG_STATE") ?? "";
		string output = Environment.GetEnvironmentVariable("MESEN_DIAG_OUT") ?? "";
		Assert.SkipWhen(rom.Length == 0 || output.Length == 0, "set MESEN_DIAG_ROM / MESEN_DIAG_OUT");
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		List<string> lines = new();
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			if(state.Length > 0) {
				EmuApi.Resume();
				Thread.Sleep(500);
				EmuApi.Pause();
				EmuApi.LoadStateFile(state);
				EmuApi.Pause();
				Thread.Sleep(200);
				EmuApi.Pause();
			} else {
				ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, true);
				EmuApi.Resume();
				Thread.Sleep(2000);
				EmuApi.Pause();
			}

			NesPpuState ppu = DebugApi.GetPpuState<NesPpuState>(CpuType.Nes);
			lines.Add($"# scanline={ppu.Scanline} cycle={ppu.Cycle} frame={ppu.FrameCount}");
			lines.Add($"# bgPatternAddr={ppu.Control.BackgroundPatternAddr:X4} sprPatternAddr={ppu.Control.SpritePatternAddr:X4}");
			lines.Add($"# chrRomSize={DebugApi.GetMemorySize(MemoryType.NesChrRom)} chrRamSize={DebugApi.GetMemorySize(MemoryType.NesChrRam)}");

			//The current _chrPages mapping, one 256-byte PPU page at a time.
			for(int page = 0; page < 32; page++) {
				AddressInfo rel = new() { Address = page * 0x100, Type = MemoryType.NesPpuMemory };
				AddressInfo abs = DebugApi.GetAbsoluteAddress(rel);
				lines.Add($"page\t{page * 0x100:X4}\t{abs.Type}\t{abs.Address:X6}");
			}

			//Every nametable tile of the visible screen, as the viewer resolves it.
			byte[] vram = DebugApi.GetMemoryState(MemoryType.NesPpuMemory);
			for(int row = 0; row < 30; row++) {
				for(int col = 0; col < 32; col++) {
					int ntAddr = 0x2000 + row * 32 + col;
					int tileIndex = vram[ntAddr];
					int tileAddr = ppu.Control.BackgroundPatternAddr + (tileIndex << 4);
					AddressInfo abs = DebugApi.GetAbsoluteAddress(new() { Address = tileAddr, Type = MemoryType.NesPpuMemory });
					StringBuilder sb = new();
					for(int i = 0; i < 16; i++) {
						sb.Append(DebugApi.GetMemoryValue(MemoryType.NesPpuMemory, (uint)(tileAddr + i)).ToString("X2"));
					}
					lines.Add($"tile\t{col}\t{row}\t{tileIndex:X2}\t{tileAddr:X4}\t{abs.Type}\t{abs.Address:X6}\t{abs.Address / 16}\t{sb}");
				}
			}

			//Now let the core render from that same state, and ask the PPU what CHR
			//mapping each visible scanline actually drew under.
			//
			//An EXACT number of frames, not a wall-clock sleep - and read the
			//limitation below before trusting any single number this produces.
			//
			//**The per-scanline trace is not part of a save state.** After a
			//LoadStateFile it still holds whatever the run before the restore left,
			//so at least one frame has to be rendered from the restored state before
			//it describes anything. But that frame is the one AFTER the state's, and
			//on a game that rewrites its CHR bank once per vblank the mapping has
			//already moved on. So this harness can describe a frame near the state's,
			//never the state's own - measured 2026-09-19 on Dr. Mario, where the
			//state pause reads CHR $01000 (index 508) and the very next frame draws
			//under $00000 (252), and one frame later the two have swapped again.
			//A wall-clock `Resume(); Sleep(300)` is ~18 frames of unknown phase, which
			//is how ADR-0215's own Dr. Mario row was taken; MESEN_DIAG_STEP_FRAMES
			//makes the phase a parameter instead of luck.
			//Games that split mid-frame the same way every frame - Lemmings, Ninja
			//Gaiden - are phase-independent and reproduce exactly whatever the count.
			DebugApi.InitializeDebugger();
			DebugApi.Step(CpuType.Nes, FramesStepped, StepType.PpuFrame);
			Thread.Sleep(500);
			EmuApi.Pause();
			Thread.Sleep(100);
			uint[] trace = ScanlineChrBankTrace();

			//ADR-0215 as decided 2026-09-19: the same cells again, this time through
			//the exported traces and NesDrawnTileResolver - the answer the shipped
			//copy action now gives. `drawn` lines carry the paused index and the
			//resolved one side by side, which is the whole measurement.
			if(DebugApi.GetNesScanlineTrace(out uint[] scroll, out uint[] chrBank)) {
				NesPpuState live = DebugApi.GetPpuState<NesPpuState>(CpuType.Nes);
				byte[] liveVram = DebugApi.GetMemoryState(MemoryType.NesPpuMemory);
				for(int row = 0; row < 30; row++) {
					for(int col = 0; col < 32; col++) {
						int tileMapAddr = 0x2000 + row * 32 + col;
						int tileAddr = live.Control.BackgroundPatternAddr + (liveVram[tileMapAddr] << 4);
						AddressInfo pausedAbs = DebugApi.GetAbsoluteAddress(new() { Address = tileAddr, Type = MemoryType.NesPpuMemory });
						NesDrawnTileAddress resolved = NesDrawnTileResolver.ResolveTilemapTile(scroll, chrBank, tileMapAddr, tileAddr);
						string drawnIndex = resolved.Status == NesDrawnTileStatus.Resolved ? (resolved.AbsoluteAddress / 16).ToString() : resolved.Status.ToString();
						lines.Add($"drawn\t{col}\t{row}\t{tileMapAddr:X4}\t{tileAddr:X4}\t{pausedAbs.Address / 16}\t{drawnIndex}\t{resolved.Scanline}");
					}
				}
			} else {
				lines.Add("# drawn: the core published no scanline trace");
			}
			NesPpuState after = DebugApi.GetPpuState<NesPpuState>(CpuType.Nes);
			lines.Add($"# after: scanline={after.Scanline} cycle={after.Cycle} frame={after.FrameCount} bgPatternAddr={after.Control.BackgroundPatternAddr:X4}");
			for(int page = 0; page < 32; page++) {
				AddressInfo abs = DebugApi.GetAbsoluteAddress(new() { Address = page * 0x100, Type = MemoryType.NesPpuMemory });
				lines.Add($"pageAfter\t{page * 0x100:X4}\t{abs.Type}\t{abs.Address:X6}");
			}
			//Only the distinct rows, so the dump stays readable.
			string previous = "";
			for(int scanline = 0; scanline < 240; scanline++) {
				StringBuilder sb = new();
				for(int page = 0; page < 32; page++) {
					sb.Append(trace[scanline * 32 + page].ToString("X6"));
					sb.Append(' ');
				}
				string row = sb.ToString();
				if(row != previous) {
					lines.Add($"scan\t{scanline}\t{row}");
					previous = row;
				}
			}
		} finally {
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.MaximumSpeed, false);
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
		}

		string? folder = Path.GetDirectoryName(Path.GetFullPath(output));
		if(!string.IsNullOrEmpty(folder)) {
			Directory.CreateDirectory(folder);
		}
		File.WriteAllLines(output, lines);
	}
}
