using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Logic;

//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P15): a controller nobody has set up.
//A pad neither gamepad preset matches sends keys no mapping uses, so today it
//does nothing in game or in the menus, and fixing it needs the keyboard
//(rule 9). These are the host-free rules: which device a key belongs to, when
//the HUD pill shows, the pad-driven setup steps, and the slot the result goes to.
public static class ControllerDevices
{
	//IKeyManager::BaseGamepadIndex: every platform's key manager numbers a
	//gamepad key as 0x1000 + device * 0x100 + button.
	public const int BaseGamepadIndex = 0x1000;

	public static int? DeviceOf(ushort keyCode)
	{
		return keyCode >= BaseGamepadIndex ? (keyCode - BaseGamepadIndex) >> 8 : null;
	}

	//The detection rule: a device none of whose keys appear in any mapping.
	public static bool IsUnknown(int device, IEnumerable<ushort> mappedKeys)
	{
		foreach(ushort key in mappedKeys) {
			if(DeviceOf(key) == device) {
				return false;
			}
		}
		return true;
	}

	//"Pad2 But3" → "Pad2": the key manager's device prefix - the fallback when
	//the platform does not name the controller (DisplayName).
	public static string Label(string keyName)
	{
		if(string.IsNullOrEmpty(keyName)) {
			return "";
		}
		int space = keyName.IndexOf(' ');
		return space < 0 ? keyName : keyName.Substring(0, space);
	}

	//W-P15's title: the controller's own name ("8BitDo SN30") when the
	//platform reports it for that device, else the key-name prefix.
	public static string DisplayName(string? deviceName, string keyName)
	{
		string name = (deviceName ?? "").Trim();
		return name.Length > 0 ? name : Label(keyName);
	}

	//The pad's own Start button, when its key name says so.
	public static bool NamesStart(string keyName)
	{
		if(string.IsNullOrEmpty(keyName)) {
			return false;
		}
		string button = keyName.Substring(keyName.IndexOf(' ') + 1);
		return button.Equals("Start", StringComparison.OrdinalIgnoreCase)
			|| button.Equals("Menu", StringComparison.OrdinalIgnoreCase)
			|| button.Equals("Options", StringComparison.OrdinalIgnoreCase);
	}
}

public enum DetectorEvent
{
	None,
	ShowPill,
	DismissPill,
	OpenSheet
}

//The pill: "New controller. Press Start on it to set it up." Shown once per
//device per session on that device's first press; Start on that pad opens the
//sheet, any other key dismisses it, and it goes away by itself after 8 s. A
//pad whose key names carry no Start (DirectInput/HID buttons are numbered)
//opens the sheet with a second press of the button that showed the pill.
public sealed class UnknownControllerDetector
{
	public static readonly TimeSpan PillDuration = TimeSpan.FromSeconds(8);

	private readonly HashSet<int> _asked = new();
	private HashSet<ushort> _previous = new();
	private bool _pillShown;
	private ushort _trigger;
	private TimeSpan _shownAt;

	public int PillDevice { get; private set; } = -1;
	public int SheetDevice { get; private set; } = -1;
	public bool IsPillShown => _pillShown;

	//pressed: every key down now (InputApi.GetPressedKeys); mappedKeys: every
	//key any mapping uses; isStart: whether a key's name is the pad's Start.
	public DetectorEvent OnPressed(IReadOnlyCollection<ushort> pressed, IEnumerable<ushort> mappedKeys, Func<ushort, bool> isStart, TimeSpan now)
	{
		List<ushort> newPresses = pressed.Where(k => !_previous.Contains(k)).ToList();
		_previous = new HashSet<ushort>(pressed);

		if(_pillShown) {
			if(now - _shownAt >= PillDuration) {
				_pillShown = false;
				return DetectorEvent.DismissPill;
			}
			foreach(ushort key in newPresses) {
				_pillShown = false;
				if(ControllerDevices.DeviceOf(key) == PillDevice && (isStart(key) || key == _trigger)) {
					SheetDevice = PillDevice;
					return DetectorEvent.OpenSheet;
				}
				return DetectorEvent.DismissPill;
			}
			return DetectorEvent.None;
		}

		List<ushort> mapped = mappedKeys.ToList();
		foreach(ushort key in newPresses) {
			int? device = ControllerDevices.DeviceOf(key);
			if(device is int d && !_asked.Contains(d) && ControllerDevices.IsUnknown(d, mapped)) {
				_asked.Add(d);
				_pillShown = true;
				PillDevice = d;
				_trigger = key;
				_shownAt = now;
				return DetectorEvent.ShowPill;
			}
		}
		return DetectorEvent.None;
	}

	//#660: the 8 s above only advance on ticks, and the owner stops ticking
	//with the game (paused, quit, another workspace). The pill goes when
	//listening stops; the device keeps its one ask per session.
	public DetectorEvent StopListening()
	{
		if(!_pillShown) {
			return DetectorEvent.None;
		}
		_pillShown = false;
		return DetectorEvent.DismissPill;
	}
}

public enum SetupConsole
{
	Nes,
	GameBoy,
	MasterSystem,
	Gba
}

//The buttons a setup step binds, in KeyMapping's own names.
public enum SetupButton
{
	A,
	B,
	Select,
	Start,
	Up,
	Down,
	Left,
	Right,
	L,
	R
}

public static class ControllerSetupSteps
{
	private static readonly SetupButton[] _nes = { SetupButton.A, SetupButton.B, SetupButton.Select, SetupButton.Start, SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right };
	//The Master System pad has buttons 1 and 2 (KeyMapping A/B) and a D-pad.
	private static readonly SetupButton[] _sms = { SetupButton.A, SetupButton.B, SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right };
	private static readonly SetupButton[] _gba = { SetupButton.A, SetupButton.B, SetupButton.Select, SetupButton.Start, SetupButton.Up, SetupButton.Down, SetupButton.Left, SetupButton.Right, SetupButton.L, SetupButton.R };

	//8 steps for NES and GB, 6 for SMS, 10 for GBA (with L/R).
	public static IReadOnlyList<SetupButton> For(SetupConsole console)
	{
		return console switch {
			SetupConsole.MasterSystem => _sms,
			SetupConsole.Gba => _gba,
			_ => _nes
		};
	}

	//slotHasKeys: port 1's four mapping slots, true when a slot binds anything.
	//Null when all four are in use - the sheet then says so instead of
	//overwriting a binding the user made.
	public static int? FirstFreeSlot(IReadOnlyList<bool> slotHasKeys)
	{
		for(int i = 0; i < slotHasKeys.Count; i++) {
			if(!slotHasKeys[i]) {
				return i;
			}
		}
		return null;
	}
}

public enum SetupState
{
	Running,
	Done,
	Cancelled
}

//Driven by the pad being set up: each step's first press becomes the binding
//(on release), a 2-second hold skips the step, and 10 seconds of silence
//cancel. Keys from every other device are ignored, so the keyboard keeps
//working and cannot bind by accident. The button that opened the sheet must be
//released before the first step listens.
public sealed class ControllerSetupSession
{
	public static readonly TimeSpan HoldToSkip = TimeSpan.FromSeconds(2);
	public static readonly TimeSpan SilenceToCancel = TimeSpan.FromSeconds(10);

	private readonly int _device;
	private readonly IReadOnlyList<SetupButton> _steps;
	private readonly Dictionary<SetupButton, ushort> _bindings = new();
	private bool _armed;
	private ushort? _held;
	private TimeSpan _heldSince;
	private bool _skippedThisHold;
	private TimeSpan _lastActivity;

	public SetupState State { get; private set; } = SetupState.Running;
	public int StepIndex { get; private set; }
	public IReadOnlyList<SetupButton> Steps => _steps;
	public IReadOnlyDictionary<SetupButton, ushort> Bindings => _bindings;
	public SetupButton? CurrentStep => StepIndex < _steps.Count ? _steps[StepIndex] : null;
	public double Progress => _steps.Count == 0 ? 1 : (double)StepIndex / _steps.Count;

	public ControllerSetupSession(int device, IReadOnlyList<SetupButton> steps, TimeSpan now)
	{
		_device = device;
		_steps = steps;
		_lastActivity = now;
		if(_steps.Count == 0) {
			State = SetupState.Done;
		}
	}

	public void Tick(IReadOnlyCollection<ushort> pressed, TimeSpan now)
	{
		if(State != SetupState.Running) {
			return;
		}
		List<ushort> keys = pressed.Where(k => ControllerDevices.DeviceOf(k) == _device).ToList();

		if(!_armed) {
			if(keys.Count == 0) {
				_armed = true;
			} else {
				_lastActivity = now;
			}
			CheckSilence(now);
			return;
		}

		if(_held is ushort held) {
			if(keys.Contains(held)) {
				_lastActivity = now;
				if(!_skippedThisHold && now - _heldSince >= HoldToSkip) {
					_skippedThisHold = true;
					Advance();
				}
				return;
			}
			//Released.
			if(!_skippedThisHold && !_bindings.ContainsValue(held) && CurrentStep is SetupButton step) {
				_bindings[step] = held;
				Advance();
			}
			_held = null;
			_skippedThisHold = false;
			_lastActivity = now;
			return;
		}

		if(keys.Count > 0) {
			_held = keys[0];
			_heldSince = now;
			_lastActivity = now;
			return;
		}
		CheckSilence(now);
	}

	private void CheckSilence(TimeSpan now)
	{
		if(State == SetupState.Running && now - _lastActivity >= SilenceToCancel) {
			State = SetupState.Cancelled;
		}
	}

	//The sheet's Skip button (mouse/keyboard).
	public void Skip(TimeSpan now)
	{
		if(State != SetupState.Running) {
			return;
		}
		_lastActivity = now;
		Advance();
	}

	public void Cancel()
	{
		if(State == SetupState.Running) {
			State = SetupState.Cancelled;
		}
	}

	private void Advance()
	{
		StepIndex++;
		if(StepIndex >= _steps.Count) {
			State = SetupState.Done;
		}
	}
}
