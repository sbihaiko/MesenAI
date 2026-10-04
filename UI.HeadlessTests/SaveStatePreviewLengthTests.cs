using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #833. `SaveStateManager::GetSaveStatePreview` writes a PNG into the
//caller's buffer and then returns `frameData.size()` - the decompressed PPU
//frame's byte count - instead of `data.size()`, the PNG's own. The managed
//wrapper (EmuApi.GetSaveStatePreview) resizes the buffer to that return value
//and hands it to the PNG decoder, so it is handed a buffer longer than the PNG
//and, today, decodes anyway: a 256x240 uint16 frame (~123 KB) is comfortably
//bigger than the PNG written from it.
//
//The wrapper's own contract is asserted here rather than the decode, because
//the decode is what hides the defect: this asks the native call how many bytes
//it wrote, and checks that they end where a PNG ends. The test declares the
//P/Invoke itself - EmuApi's is private, and the point is the native contract,
//not the managed wrapper's use of it - and loads whichever core this checkout
//built (CoreLibraryLocator, #786) so it self-skips on the CI runner exactly like
//the class it sits beside.
[Collection(NativeCoreCollection.Name)]
public class SaveStatePreviewLengthTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-833-" + Guid.NewGuid().ToString("N"));

	private const int MaxPreviewBytes = 512 * 478 * 4;

	//EmuApi's own entry point and library name: the same DllImport the wrapper
	//compiles, so this cannot drift from the call it is checking.
	[DllImport(EmuApi.DllName, EntryPoint = "GetSaveStatePreview")]
	private static extern int GetSaveStatePreviewNative([MarshalAs(UnmanagedType.LPUTF8Str)] string saveStatePath, [Out] byte[] imgData);

	public SaveStatePreviewLengthTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		//Core-aware: on the CI runner the cases above skip before they touch
		//anything, and this would then be the one call that names a library the
		//checkout does not have - which xUnit reports as the case failing, with the
		//skip it really was wrapped inside the failure.
		if(NativeCore.IsAvailable) {
			EmuApi.Stop();
		}
		try {
			Directory.Delete(_folder, true);
		} catch(IOException) {
		}
	}

	//The deterministic case, and the one that is evidence: the length the call
	//answers with is the length of the PNG it wrote.
	[AvaloniaFact]
	public void The_preview_call_answers_with_the_length_of_the_PNG_it_wrote()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RegisterCoreResolver();

		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		string state = Path.Combine(_folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		WriteRealState(rom, state);

		//Filled with 0xFF rather than left at zero: the byte past the declared
		//length is only evidence that the length is where writing stopped if it
		//is a byte nothing would have written by itself.
		byte[] buffer = new byte[MaxPreviewBytes];
		Array.Fill(buffer, (byte)0xFF);

		int size = GetSaveStatePreviewNative(state, buffer);

		Assert.True(size > 0, $"the preview call answered {size} for a state it can render");

		//A PNG ends with the IEND chunk: four bytes of zero length, then the
		//type "IEND" and its CRC. So the four bytes ending eight from the
		//declared length are the type. Before the fix the answer was the frame's
		//byte count (tens of thousands past the PNG), so those four bytes were
		//not a chunk at all - which is what this assertion caught.
		Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(buffer, size - 8, 4));
		//And nothing was written past the declared length: were the answer a
		//length short of what was written, the buffer's own fill would still be
		//there.
		Assert.Equal(0xFF, buffer[size]);
	}

	//The consequence the wrapper carries, asserted through the wrapper: the
	//bitmap still decodes, so the two halves of the call agree on the same
	//bytes. This one passes before and after the fix - it is here because the
	//fix must not break it, not as evidence of the defect.
	[AvaloniaFact]
	public void The_managed_wrapper_still_decodes_the_preview()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		RegisterCoreResolver();

		string rom = Path.Combine(_folder, "synthetic-nrom.nes");
		string state = Path.Combine(_folder, "synthetic-nrom.mss");
		File.WriteAllBytes(rom, SyntheticNrom.Build());
		WriteRealState(rom, state);

		Assert.NotNull(EmuApi.GetSaveStatePreview(state));
	}

	//The headless build has no DllImport resolver of its own - NativeCore
	//registers one for the UI assembly, not for this test assembly - so the
	//call above would look for MesenCore.dll on the system paths and throw.
	//Set once per process: the runtime refuses a second resolver for an assembly,
	//and this class's cases run one after the other in the same one.
	private static bool _resolverSet;

	private static void RegisterCoreResolver()
	{
		if(_resolverSet) {
			return;
		}
		_resolverSet = true;

		IntPtr handle = NativeLibrary.Load(CoreLibraryLocator.Find()!);
		NativeLibrary.SetDllImportResolver(typeof(SaveStatePreviewLengthTests).Assembly,
			(name, assembly, searchPath) => name == EmuApi.DllName ? handle : IntPtr.Zero);
	}

	//A real state, written by the core itself, so the preview gets past the
	//header and the version guards and reaches the render.
	private static void WriteRealState(string rom, string state)
	{
		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
		WaitFor(() => EmuApi.IsRunning());
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
