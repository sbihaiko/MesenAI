using System;
using System.Collections.Generic;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0255 slice 4: a shortcut may carry a *third* binding, and the Play
//Controller sheet's EXTRA BUTTONS section is built on that. The engine used to
//hold two key sets per shortcut (EmuSettings::SetShortcutKeys filled 0 and 1 and
//ShortcutKeyHandler polled exactly those), so a shortcut that already had two
//key combinations - and Rewind, FastForward and ToggleOverlay ship with two
//each - had its pad slot dropped *silently*: PreferencesConfig pushed it, the
//engine overwrote the second key with it, and the sheet went on showing it
//bound. The three actions the fourth requirement names
//("retroceder, avancar, home") are exactly the three that were dropped.
//
//Two things have to hold, and they are separate: the engine must *keep* three
//bindings (asserted through the read-back below, which is why GetShortcutKey is
//exported at all), and it must *poll* the set the third one lands in (asserted
//by pressing it and watching the shortcut come out of the engine as an
//ExecuteShortcut notification, the only way a shortcut becomes an action -
//CheckMappedKeys notifies, the GUI acts).
//
//SetKeyState takes keyboard scancodes only (MacOSKeyManager::SetKeyState
//rejects anything at or above 0x205) and drives ProcessKeys synchronously, so
//this presses keys and reads the notification the engine sends; it cannot press
//a pad button. Nothing here needs a ROM: the key sets are settings, and the
//handler thread runs from the emulator's construction.
//
//Skips when the native core is not built (ADR-0150 §3), which is every CI run.
[Collection(NativeCoreCollection.Name)]
public class ShortcutKeySetTests
{
	//InitializeEmu only builds a key manager when both handles are non-null
	//(InteropDLL/EmuApiWrapper.cpp), and InputApi.SetKeyState does nothing without
	//one - so this is what a real window passes (MainWindow hands over its own
	//platform handle and its renderer's). The values are never dereferenced on
	//this path: no video is requested, so the renderer handle is only stored.
	private static readonly IntPtr FakeWindow = new(1);
	private static readonly IntPtr FakeRenderer = new(2);

	[Fact]
	public void Three_bindings_for_one_shortcut_all_survive_in_the_engine()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		WithCore(() => {
			ushort f1 = InputApi.GetKeyCode("F1");
			ushort f2 = InputApi.GetKeyCode("F2");
			ushort f3 = InputApi.GetKeyCode("F3");
			Assert.NotEqual(0, f1 | f2 | f3);

			Push(EmulatorShortcut.ToggleFps, f1, f2, f3);

			Assert.Equal((UInt32)f1, ConfigApi.GetShortcutKey(EmulatorShortcut.ToggleFps, 0).Key1);
			Assert.Equal((UInt32)f2, ConfigApi.GetShortcutKey(EmulatorShortcut.ToggleFps, 1).Key1);
			//The third set is the whole point: a two-set engine answers f2 here,
			//because the third binding overwrote the second instead of joining it.
			Assert.Equal((UInt32)f3, ConfigApi.GetShortcutKey(EmulatorShortcut.ToggleFps, 2).Key1);
		});
	}

	[Fact]
	public void The_third_key_set_is_polled_so_its_binding_fires()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		WithCore(() => {
			ushort f1 = InputApi.GetKeyCode("F1");
			ushort f2 = InputApi.GetKeyCode("F2");
			ushort f3 = InputApi.GetKeyCode("F3");
			Push(EmulatorShortcut.ToggleFps, f1, f2, f3);

			using(ShortcutRecorder recorder = new()) {
				Press(f1);
				//The first two sets are the behaviour the engine always had, so
				//they are the control: if these did not fire the third proving
				//anything would be a coincidence.
				Assert.Contains(EmulatorShortcut.ToggleFps, recorder.Executed);

				recorder.Clear();
				Press(f2);
				Assert.Contains(EmulatorShortcut.ToggleFps, recorder.Executed);

				recorder.Clear();
				Press(f3);
				Assert.Contains(EmulatorShortcut.ToggleFps, recorder.Executed);
			}
		});
	}

	//A shortcut whose third set is empty must stay silent on a key that names
	//nothing: the engine must not answer a set the config never filled.
	[Fact]
	public void A_shortcut_with_two_bindings_does_not_fire_on_a_third_key()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		WithCore(() => {
			ushort f1 = InputApi.GetKeyCode("F1");
			ushort f2 = InputApi.GetKeyCode("F2");
			ushort f3 = InputApi.GetKeyCode("F3");
			Push(EmulatorShortcut.ToggleFps, f1, f2, 0);

			using(ShortcutRecorder recorder = new()) {
				Press(f2);
				Assert.Contains(EmulatorShortcut.ToggleFps, recorder.Executed);

				recorder.Clear();
				Press(f3);
				Assert.DoesNotContain(EmulatorShortcut.ToggleFps, recorder.Executed);
			}
		});
	}

	//The one thing the third set could have broken. The engine keeps a fourth
	//set that is not a shortcut set at all: ClearShortcutKeys seeds a fake
	//Exit = Alt-F4 there so Alt-F4 shadows any shortcut it contains (the plain-F4
	//bindings - "load save state 4" is the comment's own example) instead of
	//firing it. That shadow is wired by SetShortcutKey's sweep over *every* set,
	//which is why the sweep covers the guard's set and the poll loop does not -
	//if the guard moved into the polled range the engine would quit on Alt-F4
	//itself, and if the sweep stopped covering it the guard would stop working.
	[Fact]
	public void The_alt_f4_guard_still_shadows_a_shortcut_bound_to_f4()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		WithCore(() => {
			ushort f4 = InputApi.GetKeyCode("F4");
			ushort alt = InputApi.GetKeyCode("Left Alt");
			Push(EmulatorShortcut.ToggleFps, f4);

			using(ShortcutRecorder recorder = new()) {
				//The control: F4 on its own is the binding, so it fires. Without
				//this the case below would pass on a shortcut that never fired.
				Press(f4);
				Assert.Contains(EmulatorShortcut.ToggleFps, recorder.Executed);

				recorder.Clear();
				InputApi.SetKeyState(alt, true);
				InputApi.SetKeyState(f4, true);
				InputApi.SetKeyState(f4, false);
				//Alt is still down, so this is not a release: the guard is what
				//has to hold here, not the key-up rule.
				Assert.DoesNotContain(EmulatorShortcut.ToggleFps, recorder.Executed);
				//...and the guard's own set is not one the engine polls, so Alt-F4
				//does not come out as the Exit it is seeded as either. Moving the
				//guard into the polled range would make the engine quit on the
				//chord it keeps only to shadow.
				Assert.DoesNotContain(EmulatorShortcut.Exit, recorder.Executed);
				InputApi.SetKeyState(alt, false);
			}
		});
	}

	//One shortcut's own combination, in the engine's own field order.
	private static void Push(EmulatorShortcut shortcut, ushort key1, ushort key2 = 0, ushort key3 = 0)
	{
		List<InteropShortcutKeyInfo> pushed = new();
		foreach(ushort key in new[] { key1, key2, key3 }) {
			if(key != 0) {
				pushed.Add(new InteropShortcutKeyInfo(shortcut, new InteropKeyCombination() { Key1 = key }));
			}
		}
		ConfigApi.SetShortcutKeys(pushed.ToArray(), (UInt32)pushed.Count);
	}

	//Down then up, so the next press is a *new* press: DetectKeyPress only fires
	//a shortcut that was not down on the previous pass.
	private static void Press(ushort key)
	{
		InputApi.SetKeyState(key, true);
		InputApi.SetKeyState(key, false);
	}

	private static void WithCore(Action body)
	{
		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, FakeWindow, FakeRenderer, true, true, true, false);
		//#1023: the engine is process-global and ShortcutKeyHandler::ProcessKeys
		//returns at once while EmulationFlags.InBackground is set (IsInputEnabled).
		//MainWindow.OnActiveChanged sets that flag whenever none of the app's
		//windows is active - always the case for a headless window that a class
		//ran before this one - and nothing clears it when the window closes. A
		//fresh process starts with it clear, so this states what the case needs
		//instead of inheriting it.
		ConfigApi.SetEmulationFlag(EmulationFlags.InBackground, false);
		try {
			body();
		} finally {
			InputApi.ResetKeyState();
			//The shortcut list this case wrote is not the player's: the next case
			//in the assembly must not inherit three bindings for ToggleFps.
			ConfigApi.SetShortcutKeys(Array.Empty<InteropShortcutKeyInfo>(), 0);
			EmuApi.Stop();
		}
	}

	//The engine sends ExecuteShortcut and the GUI is what acts on it (see
	//CheckMappedKeys), so the notification is the observable end of a shortcut.
	//Recording it is enough: no window, no dispatcher, and nothing here has to
	//know what the action does.
	private sealed class ShortcutRecorder : IDisposable
	{
		private readonly NotificationListener _listener = new();

		public List<EmulatorShortcut> Executed { get; } = new();

		public ShortcutRecorder()
		{
			_listener.OnNotification += e => {
				if(e.NotificationType == ConsoleNotificationType.ExecuteShortcut) {
					lock(Executed) {
						Executed.Add(System.Runtime.InteropServices.Marshal.PtrToStructure<ExecuteShortcutParams>(e.Parameter).Shortcut);
					}
				}
			};
		}

		public void Clear()
		{
			lock(Executed) {
				Executed.Clear();
			}
		}

		public void Dispose() => _listener.Dispose();
	}
}
