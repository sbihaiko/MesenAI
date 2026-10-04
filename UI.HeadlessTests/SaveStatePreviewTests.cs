using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Mesen.Config;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #829. A save state's thumbnail is rendered off the UI thread by
//EmuApi.GetSaveStatePreview (UI/Controls/StateGridEntry.axaml.cs), which reaches
//SaveStateManager::GetSaveStatePreview -> Emulator::GetVideoFilter. That call
//hands out `new NesDefaultVideoFilter(this)` while no console is active, and
//NesDefaultVideoFilter::OnBeforeApplyFilter dereferenced that missing console -
//the filter read a live NES console that the preview path may run without.
//
//The sequence is ordinary, not exotic: the slot grid over a running game starts
//a preview per .mss file, and closing the game (or the window) resets the
//console while those workers are still in flight. Measured on 2026-10-04, the
//access violation landed in OnBeforeApplyFilter at address 0x38 - the Ppu field
//of a null NesConsole - and killed the test process with SIGSEGV (exit 139).
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
[Collection(NativeCoreCollection.Name)]
public class SaveStatePreviewTests
{
	//The deterministic half, and the one that is evidence: one preview while the
	//console is live, the game closed, one preview after, on this thread.
	//
	//AvaloniaFact, not Fact: EmuApi.GetSaveStatePreview decodes the PNG into an
	//Avalonia Bitmap, which needs the headless platform's IPlatformRenderInterface.
	[AvaloniaFact]
	public void A_preview_still_renders_after_the_game_is_closed()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		string folder = Path.Combine(Path.GetTempPath(), "mesen-829-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		string state = Path.Combine(folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, SyntheticNrom.Build());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			WriteRealState(rom, state);

			//The same state, previewed while its console is live. This is the
			//frame the second call has to reproduce: the filter renders with no
			//console active, and it must not render something else - or nothing.
			Bitmap? loaded = EmuApi.GetSaveStatePreview(state);
			Assert.NotNull(loaded);

			//The game goes away - the console Emulator::Stop() resets - and the
			//preview is asked for anyway. Before the fix this line killed the test
			//process (SIGSEGV, exit code 139, at NesDefaultVideoFilter::
			//OnBeforeApplyFilter), which no assertion can report.
			EmuApi.Stop();
			Assert.False(EmuApi.IsRunning());

			Bitmap? closed = EmuApi.GetSaveStatePreview(state);
			Assert.NotNull(closed);
			Assert.Equal(loaded.PixelSize, closed.PixelSize);
		} finally {
			EmuApi.Stop();
			DeleteFolder(folder);
		}
	}

	//The other half, and the sequence the app really runs: StateGridEntry starts
	//every slot's preview on its own worker thread and awaits none of them, so the
	//game can go away underneath any of them. Unlike the case above this one is
	//not deterministic evidence - it can only catch the interleaving it happens to
	//hit - and it is here because that interleaving is the reported trigger, and a
	//preview that survives the console disappearing mid-flight is the invariant.
	[AvaloniaFact]
	public void Previews_in_flight_when_the_game_closes_do_not_crash()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		string folder = Path.Combine(Path.GetTempPath(), "mesen-829-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string rom = Path.Combine(folder, "synthetic-nrom.nes");
		string state = Path.Combine(folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, SyntheticNrom.Build());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			WriteRealState(rom, state);

			//Eight slots' worth, started as the grid starts them.
			Task<Bitmap?>[] previews = new Task<Bitmap?>[8];
			for(int i = 0; i < previews.Length; i++) {
				previews[i] = Task.Run(() => EmuApi.GetSaveStatePreview(state));
			}

			EmuApi.Stop();
			Assert.False(EmuApi.IsRunning());

			//WaitAll faults the test if any preview threw, and the process would
			//already be gone if any of them had crashed; a preview older than the
			//stop renders through the no-console path, which is what the fix makes
			//safe. Nothing asserts *what* they returned: both answers are correct.
			Assert.True(Task.WaitAll(previews, 30000), "the previews never returned");
		} finally {
			EmuApi.Stop();
			DeleteFolder(folder);
		}
	}

	//A real state, written by the core itself, so a preview gets past the header
	//and the version guards and reaches the render.
	private static void WriteRealState(string rom, string state)
	{
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning());
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused());

		EmuApi.SaveStateFile(state);
		Assert.True(WaitFor(() => File.Exists(state) && new FileInfo(state).Length > 0), $"no state was written to {state}");
	}

	private static void DeleteFolder(string folder)
	{
		try {
			Directory.Delete(folder, true);
		} catch(IOException) {
		}
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
}
