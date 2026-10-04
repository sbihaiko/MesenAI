using System;
using System.Runtime.InteropServices;
using Mesen.Interop;

namespace Mesen.HeadlessTests;

//ADR-0150 §3 ("P/Invoke containment"). UI/UI.csproj carries UI/Interop/EmuApi.cs
//and its ~60 DllImports against the native MesenCore library. Some of the XAML
//under test cannot be reached without at least one of them running:
//`MainWindow`'s constructor calls EmuApi.InitDll() before its XAML is loaded,
//and the input Test tab calls InputApi.GetConnectedGamepadCount() the moment the
//tab is selected.
//
//Rather than faking the core (a stub library would answer with values the real
//core never returns, and the test would then be asserting the stub), this loads
//the REAL library when the repo happens to have one built, and skips the test
//with an explicit reason when it does not - which is the case on the CI runner,
//where building MesenCore is out of scope for unit-tests.yml (ADR-0131).
//
//Which file that is comes from CoreLibraryLocator, kept out of this type so its
//rule can be asserted without naming the core (#786).
public static class NativeCore
{
	private static bool _initialized;
	private static string? _skipReason;

	public static string? SkipReason
	{
		get {
			Initialize();
			return _skipReason;
		}
	}

	public static bool IsAvailable => SkipReason == null;

	private static void Initialize()
	{
		if(_initialized) {
			return;
		}
		_initialized = true;

		string? library = CoreLibraryLocator.Find();
		if(library == null) {
			_skipReason = "MesenCore is not built in this checkout (looked for MESEN_CORE_LIB and bin/<rid>/{Release,Debug}/, InteropDLL/obj.<rid>/). " +
				"unit-tests.yml never builds the native core (ADR-0131), so this XAML-wiring check runs locally after `make` and is skipped in CI.";
			return;
		}

		try {
			IntPtr handle = NativeLibrary.Load(library);
			NativeLibrary.SetDllImportResolver(typeof(EmuApi).Assembly, (name, assembly, path) => name == EmuApi.DllName ? handle : IntPtr.Zero);
			//Fails loudly here rather than half-way through a window constructor.
			EmuApi.TestDll();
		} catch(Exception ex) {
			_skipReason = $"MesenCore at '{library}' could not be loaded: {ex.GetType().Name} - {ex.Message}";
		}
	}
}
