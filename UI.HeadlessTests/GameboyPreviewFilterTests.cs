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

//Issue #831, the Game Boy site of the read #829 fixed in NesDefaultVideoFilter
//and #831 guarded in NesNtscFilter. GbDefaultVideoFilter::OnBeforeApplyFilter
//asked the loaded console whether it is a CGB -
//`gbConfig.GbcAdjustColors && ((Gameboy*)_emu->GetConsole().get())->IsCgb()` -
//and the preview path (SaveStateManager::GetSaveStatePreview) builds and drives
//a filter that a Game Boy console lent it, on a worker thread, with nothing
//holding the console in place: the grid starts one preview per .mss file and
//awaits none of them, so closing the game resets the console underneath them.
//
//Unlike its NES sibling this case has no deterministic half, because the crash
//needs the console alive when the preview builds its filter and gone when the
//filter runs. That is the whole window, and it is an interleaving rather than a
//call order: with no console Emulator::GetVideoFilter answers a
//NesDefaultVideoFilter, so a preview that starts after Emulator::Stop() renders
//through the NES filter and never touches this one. The test therefore runs the
//sequence repeatedly, and a failure is the process dying rather than an
//assertion. Measured on 2026-10-04 against a core without the guard: SIGSEGV,
//KERN_INVALID_ADDRESS at 0x80 - Gameboy::IsCgb() reading `_model` on a null
//console - with the stack SaveStateManager::GetSaveStatePreview ->
//BaseVideoFilter::SendFrame -> GbDefaultVideoFilter::OnBeforeApplyFilter ->
//Gameboy::IsCgb(), and the test host reporting exit code 139.
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
[Collection(NativeCoreCollection.Name)]
public class GameboyPreviewFilterTests : IDisposable
{
	//Enough rounds that the reported interleaving is not a coin flip, and few
	//enough that the case stays a test rather than a soak.
	private const int Rounds = 6;
	private const int PreviewsPerRound = 16;

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-831-" + Guid.NewGuid().ToString("N"));

	public GameboyPreviewFilterTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		//Core-aware: on the CI runner the case below skips before it touches
		//anything, and this would be the one call to name a library the
		//checkout does not have - which xUnit reports as the case failing.
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
		}
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	[AvaloniaFact]
	public void A_gameboy_preview_in_flight_when_the_game_closes_does_not_crash()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string rom = Path.Combine(_folder, "synthetic-gb.gb");
		string state = Path.Combine(_folder, "synthetic-gb.mss");
		File.WriteAllBytes(rom, SyntheticGbRom.Build());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);

		try {
			for(int round = 0; round < Rounds; round++) {
				LoadGameboyState(rom, state);

				//The grid's shape: one preview per slot, each on its own worker,
				//none awaited. They are in flight when the game goes away.
				Task<Bitmap?>[] previews = new Task<Bitmap?>[PreviewsPerRound];
				for(int i = 0; i < previews.Length; i++) {
					previews[i] = Task.Run(() => EmuApi.GetSaveStatePreview(state));
				}

				EmuApi.Stop();

				//WaitAll faults the test if a preview threw, and the process
				//would already be gone if one of them had crashed.
				Assert.True(Task.WaitAll(previews, 30000), "the previews never returned");

				if(round == 0) {
					//Read after the wait, so it cannot change the race it is
					//here to observe: the fix has to leave the previews working,
					//not merely silent, so at least one renders a frame - the
					//answer is a bitmap whether it ran before or after the stop.
					Assert.Contains(previews, p => p.Result != null);
				}
			}
		} finally {
			EmuApi.Stop();
		}
	}

	//A Game Boy game loaded, running, and saved - the console the previews
	//borrow their filter from, and the state they render. Asserting the console
	//type keeps this case honest: it is a Game Boy filter only while the core
	//really did load a Game Boy.
	private static void LoadGameboyState(string rom, string state)
	{
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning());
		Assert.Equal(ConsoleType.Gameboy, EmuApi.GetRomInfo().ConsoleType);

		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused());

		EmuApi.SaveStateFile(state);
		Assert.True(WaitFor(() => File.Exists(state) && new FileInfo(state).Length > 0), $"no state was written to {state}");
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
