using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #832. A save state carries its own console in its header -
//SaveStateManager's preview reads those four bytes and then threw them away with
//`stream.seekg(4, ios::cur)` - while the frame was rendered through
//Emulator::GetVideoFilter, which is the *loaded* console's filter. So a Game Boy
//state sitting in the folder the slot grid lists, while an NES game is loaded,
//previewed as noise: a GB frame decoded through the NES default filter's
//palette.
//
//Reachable since #829 stopped the same path from crashing on a console that is
//no longer there; before that, previewing the state of a game that had been
//closed took the process down.
//
//The state below is a real one written by the core, with its header's console
//type rewritten: the payload is an NES frame, which is what makes this a test of
//the header - the file claims to come from a Game Boy while the loaded console
//is an NES, so there is no filter to render it through. Runs with the core
//injected and self-skips on the CI runner like its siblings (#786, ADR-0131).
[Collection(NativeCoreCollection.Name)]
public class ForeignStatePreviewTests : IDisposable
{
	//Where the console type sits: "MSS" (3), then the emulator version and the
	//file format version, four bytes each - the field the preview used to skip.
	private const int ConsoleTypeOffset = 3 + 4 + 4;

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-832-" + Guid.NewGuid().ToString("N"));

	public ForeignStatePreviewTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		//Core-aware: on the CI runner the cases below skip before they touch
		//anything, and this would be the one call that names a library the
		//checkout does not have - which xUnit reports as the case failing.
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
		}
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	//The deterministic case, and the one that is evidence.
	[AvaloniaFact]
	public void A_state_of_another_console_has_no_preview_while_a_game_is_loaded()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string state = WriteStateOfAnotherConsole(foreign: true);

		//The console is loaded and running, so the preview has a filter - the NES
		//one, which is not what the state's game ran on. Before the fix this
		//rendered the GB-labelled frame through it and answered a bitmap.
		Assert.True(EmuApi.IsRunning(), "this case is about a loaded console");
		Assert.Null(EmuApi.GetSaveStatePreview(state));
	}

	//The same refusal with no game loaded, and the case that keeps the guard from
	//being written as "only when a console exists". The filter a preview gets with
	//no console is the NES default one - Emulator::GetVideoFilter's own answer for
	//that case, not the last console's, which is what Emulator::GetConsoleType
	//still answers and why it is not the field the guard reads - so a Game
	//Boy-labelled state has nothing to render it with here either. Before the fix
	//this line answered a bitmap, the same noise as above rendered through the NES
	//palette.
	[AvaloniaFact]
	public void A_state_of_another_console_has_no_preview_with_no_game_loaded()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string state = WriteStateOfAnotherConsole(foreign: true);

		EmuApi.Stop();
		Assert.False(EmuApi.IsRunning());
		Assert.Null(EmuApi.GetSaveStatePreview(state));
	}

	//The other half: the state the console actually wrote still previews, so the
	//guard refuses foreign states and nothing else. This is the case that fails if
	//the check is written the wrong way round - refusing every state, or reading
	//the type off a field that is not there.
	[AvaloniaFact]
	public void The_loaded_consoles_own_state_still_previews()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string state = WriteStateOfAnotherConsole(foreign: false);

		Assert.NotNull(EmuApi.GetSaveStatePreview(state));
	}

	//The third case, and the one that keeps the guard honest about the header's
	//shape: a file at the oldest format the preview accepts (v3) carries a 40-byte
	//SHA1 field between the format version and the console type - the field
	//`LoadState` skips before it reads the type, so a preview that reads the type
	//without skipping reads the SHA1's first four bytes as the console and, 40
	//bytes further on, a frame length that is not a length. These states had no
	//thumbnail before the skip rather than a wrong one: GetVideoData refused the
	//garbage size. This case is what says the skip is there and in the right
	//place, not merely that the guard accepts a state.
	[AvaloniaFact]
	public void An_old_format_state_still_previews()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string state = WriteStateOfAnotherConsole(foreign: false);

		byte[] bytes = File.ReadAllBytes(state);
		//The format version is the second uint32: "MSS", then emuVersion.
		bytes[3 + 4] = 3;
		bytes[3 + 5] = 0;
		bytes[3 + 6] = 0;
		bytes[3 + 7] = 0;
		//And v3's own header: the old SHA1 field sits between it and the console
		//type, so the file grows by those 40 bytes.
		byte[] downgraded = new byte[bytes.Length + 40];
		Array.Copy(bytes, 0, downgraded, 0, ConsoleTypeOffset);
		Array.Copy(bytes, ConsoleTypeOffset, downgraded, ConsoleTypeOffset + 40,
			bytes.Length - ConsoleTypeOffset);
		File.WriteAllBytes(state, downgraded);

		Assert.NotNull(EmuApi.GetSaveStatePreview(state));
	}

	//A real state written by the core, with the header's console type rewritten to
	//Gameboy when `foreign` - the payload is untouched, and those four bytes are
	//the whole difference between the two cases above.
	private string WriteStateOfAnotherConsole(bool foreign)
	{
		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		string state = Path.Combine(_folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, SyntheticNrom.Build());

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning());
		EmuApi.Resume();
		WaitFor(() => !EmuApi.IsPaused());

		EmuApi.SaveStateFile(state);
		Assert.True(WaitFor(() => File.Exists(state) && new FileInfo(state).Length > 0), $"no state was written to {state}");

		if(foreign) {
			byte[] bytes = File.ReadAllBytes(state);
			Assert.True(bytes.Length > ConsoleTypeOffset + 4, "the state is too short to carry a console type");
			//Little-endian uint32, the order SaveStateManager::WriteValue writes.
			bytes[ConsoleTypeOffset] = (byte)ConsoleType.Gameboy;
			bytes[ConsoleTypeOffset + 1] = 0;
			bytes[ConsoleTypeOffset + 2] = 0;
			bytes[ConsoleTypeOffset + 3] = 0;
			File.WriteAllBytes(state, bytes);
		}
		return state;
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
