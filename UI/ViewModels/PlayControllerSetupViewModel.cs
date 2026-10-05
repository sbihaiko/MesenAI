using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Mesen.ViewModels
{
	//One button on the sheet's picture: lit for the current step, ticked once
	//bound, placed where it sits on the pad (ADR-0249 W-P15).
	public sealed record ControllerSetupChip(string Name, bool IsCurrent, bool IsDone, SetupButton Button = SetupButton.A)
	{
		private PadKey Key => ControllerPadLayout.Of(Button);
		public double Left => Key.Left;
		public double Top => Key.Top;
		public double Width => Key.Width;
		public double Height => Key.Height;
		public bool IsDPad => Key.Shape == PadKeyShape.DPad;
		public bool IsPill => Key.Shape == PadKeyShape.Pill;
		public bool IsRound => Key.Shape == PadKeyShape.Round;
		public bool IsShoulder => Key.Shape == PadKeyShape.Shoulder;
		public bool ShowsLabel => ControllerPadLayout.ShowsLabel(Button);
	}

	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P15): the HUD pill for an unknown
	//pad and the pad-driven setup sheet. The rules (detection, steps, hold to
	//skip, silence to cancel, the free slot) are host-free in
	//UI/Logic/PlayControllerSetup; this maps them onto InputApi's polled key
	//state and port 1's KeyMapping slots. The owner calls Tick from a timer
	//only while a game runs in Player mode's Play workspace.
	public partial class PlayControllerSetupViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsPillVisible { get; private set; }
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial string Title { get; private set; } = "";
		[ObservableProperty] public partial string Prompt { get; private set; } = "";
		[ObservableProperty] public partial string StepText { get; private set; } = "";
		[ObservableProperty] public partial double Progress { get; private set; }
		[ObservableProperty] public partial string ErrorText { get; private set; } = "";
		[ObservableProperty] public partial IReadOnlyList<ControllerSetupChip> Chips { get; private set; } = Array.Empty<ControllerSetupChip>();

		private readonly UnknownControllerDetector _detector = new();
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private ControllerSetupSession? _session;
		private SetupConsole _console;
		private string _label = "";
		private bool _resumeOnClose;

		//Injectable for the headless tests; the Core's names by default.
		public Func<ushort, string> KeyName { get; set; } = InputApi.GetKeyName;
		//W-P15: the controller's own name for a key-code device index. macOS and
		//Linux number GetGamepadInfo like the key codes; Windows numbers XInput
		//and DirectInput pads apart from them, so there the key prefix is used.
		public Func<int, string> DeviceName { get; set; } = DefaultDeviceName;
		public Func<ConsoleType> CurrentConsole { get; set; } = () => EmuApi.GetRomInfo().ConsoleType;
		public Action Pause { get; set; } = EmuApi.Pause;
		public Action Resume { get; set; } = EmuApi.Resume;
		public Func<bool> IsPaused { get; set; } = EmuApi.IsPaused;

		//The sheet closed (done or cancelled); the owner shows the result.
		public event Action<string>? Finished;

		public TimeSpan Now => _clock.Elapsed;

		public void Tick(IReadOnlyCollection<ushort> pressed) => Tick(pressed, Now);

		public void Tick(IReadOnlyCollection<ushort> pressed, TimeSpan now)
		{
			if(_session != null) {
				_session.Tick(pressed, now);
				RefreshSession();
				return;
			}

			ControllerConfig? port = PortFor(CurrentConsole(), out _);
			IEnumerable<ushort> mapped = port == null ? Array.Empty<ushort>() : MappedKeys(CurrentConsole());
			switch(_detector.OnPressed(pressed, mapped, k => ControllerDevices.NamesStart(KeyName(k)), now)) {
				case DetectorEvent.ShowPill: IsPillVisible = true; break;
				case DetectorEvent.DismissPill: IsPillVisible = false; break;
				case DetectorEvent.OpenSheet: OpenSheet(_detector.SheetDevice, pressed, now); break;
			}
		}

		//#660: the owner stopped ticking (the game paused or quit): the pill
		//cannot time out without ticks, so it goes now.
		public void StopListening()
		{
			if(_session == null && _detector.StopListening() == DetectorEvent.DismissPill) {
				IsPillVisible = false;
			}
		}

		private void OpenSheet(int device, IReadOnlyCollection<ushort> pressed, TimeSpan now)
		{
			IsPillVisible = false;
			if(PortFor(CurrentConsole(), out SetupConsole console) == null) {
				return;
			}
			_console = console;
			ushort any = pressed.FirstOrDefault(k => ControllerDevices.DeviceOf(k) == device);
			_label = ControllerDevices.DisplayName(DeviceName(device), KeyName(any));
			if(string.IsNullOrEmpty(_label)) {
				_label = ResourceHelper.GetMessage("ControllerSetupUnnamed");
			}
			_session = new ControllerSetupSession(device, ControllerSetupSteps.For(console), now);
			Title = ResourceHelper.GetMessage("ControllerSetupTitle", _label);
			ErrorText = "";
			_resumeOnClose = !IsPaused();
			Pause();
			IsVisible = true;
			RefreshSession();
		}

		public void Skip()
		{
			_session?.Skip(Now);
			RefreshSession();
		}

		public void Cancel()
		{
			_session?.Cancel();
			RefreshSession();
		}

		private void RefreshSession()
		{
			if(_session is not ControllerSetupSession session) {
				return;
			}
			if(session.State != SetupState.Running) {
				Close(session);
				return;
			}
			SetupButton step = session.CurrentStep ?? SetupButton.A;
			Prompt = ResourceHelper.GetMessage("ControllerSetupPrompt", ButtonName(step));
			StepText = ResourceHelper.GetMessage("ControllerSetupStep", session.StepIndex + 1, session.Steps.Count, string.Join(", ", session.Steps.Select(ButtonName)));
			Progress = session.Progress;
			Chips = session.Steps.Select((b, i) => new ControllerSetupChip(ButtonName(b), i == session.StepIndex, session.Bindings.ContainsKey(b), b)).ToList();
		}

		private void Close(ControllerSetupSession session)
		{
			_session = null;
			string result = "";
			if(session.State == SetupState.Done && session.Bindings.Count > 0) {
				result = Save(session.Bindings);
			}
			IsVisible = false;
			if(_resumeOnClose) {
				Resume();
			}
			Finished?.Invoke(result);
		}

		//The first free mapping slot of the loaded console's port 1; never
		//overwrites a slot the user (or a preset) already bound.
		private string Save(IReadOnlyDictionary<SetupButton, ushort> bindings)
		{
			ControllerConfig? port = PortFor(CurrentConsole(), out _);
			if(port == null) {
				return "";
			}
			KeyMapping[] slots = { port.Mapping1, port.Mapping2, port.Mapping3, port.Mapping4 };
			if(ControllerSetupSteps.FirstFreeSlot(slots.Select((m, i) => HasKeys(m, port.Type, i)).ToList()) is not int free) {
				return ResourceHelper.GetMessage("ControllerSetupNoSlot");
			}
			KeyMapping mapping = slots[free];
			foreach((SetupButton button, ushort key) in bindings) {
				Assign(mapping, button, key);
			}
			ConfigManager.Config.ApplyConfig();
			ConfigManager.Config.Save();
			return ResourceHelper.GetMessage("ControllerSetupSaved", _label);
		}

		private static void Assign(KeyMapping m, SetupButton button, ushort key)
		{
			switch(button) {
				case SetupButton.A: m.A = key; break;
				case SetupButton.B: m.B = key; break;
				case SetupButton.Select: m.Select = key; break;
				case SetupButton.Start: m.Start = key; break;
				case SetupButton.Up: m.Up = key; break;
				case SetupButton.Down: m.Down = key; break;
				case SetupButton.Left: m.Left = key; break;
				case SetupButton.Right: m.Right = key; break;
				case SetupButton.L: m.L = key; break;
				case SetupButton.R: m.R = key; break;
			}
		}

		//Every binding the slot sends to the Core - U/D, turbo, the generic key
		//and the port type's custom buttons included - so a slot holding only
		//one of those is not taken for free.
		private static bool HasKeys(KeyMapping m, ControllerType type, int index)
		{
			InteropKeyMapping k = m.ToInterop(type, index);
			ushort[] fixedKeys = {
				k.A, k.B, k.X, k.Y, k.L, k.R, k.Up, k.Down, k.Left, k.Right, k.Start, k.Select, k.U, k.D,
				k.TurboA, k.TurboB, k.TurboX, k.TurboY, k.TurboL, k.TurboR, k.TurboSelect, k.TurboStart, k.GenericKey1
			};
			return fixedKeys.Any(key => key != 0) || (k.CustomKeys?.Any(key => key != 0) ?? false);
		}

		private static IEnumerable<ushort> Keys(KeyMapping m)
		{
			return new[] { m.A, m.B, m.X, m.Y, m.L, m.R, m.Up, m.Down, m.Left, m.Right, m.Start, m.Select };
		}

		//"Any port mapping": every slot of every port of the loaded console.
		private static IEnumerable<ushort> MappedKeys(ConsoleType type)
		{
			IEnumerable<ControllerConfig> ports = type switch {
				ConsoleType.Nes => new ControllerConfig[] { ConfigManager.Config.Nes.Port1, ConfigManager.Config.Nes.Port2 },
				ConsoleType.Gameboy => new ControllerConfig[] { ConfigManager.Config.Gameboy.Controller },
				ConsoleType.Sms => new ControllerConfig[] { ConfigManager.Config.Sms.Port1, ConfigManager.Config.Sms.Port2 },
				ConsoleType.Gba => new ControllerConfig[] { ConfigManager.Config.Gba.Controller },
				_ => Array.Empty<ControllerConfig>()
			};
			return ports.SelectMany(p => new[] { p.Mapping1, p.Mapping2, p.Mapping3, p.Mapping4 }).SelectMany(Keys).Where(k => k != 0);
		}

		private static ControllerConfig? PortFor(ConsoleType type, out SetupConsole console)
		{
			switch(type) {
				case ConsoleType.Nes: console = SetupConsole.Nes; return ConfigManager.Config.Nes.Port1;
				case ConsoleType.Gameboy: console = SetupConsole.GameBoy; return ConfigManager.Config.Gameboy.Controller;
				case ConsoleType.Sms: console = SetupConsole.MasterSystem; return ConfigManager.Config.Sms.Port1;
				case ConsoleType.Gba: console = SetupConsole.Gba; return ConfigManager.Config.Gba.Controller;
				default: console = SetupConsole.Nes; return null;
			}
		}

		//The console's own name for a control, off the one rule the Controller
		//sheet's REMAP rows read too (ControllerSheetRemap.ControlLabel): the two
		//surfaces bind the same controls, so they have to name them the same -
		//including the Master System's 1/2, which is the pair whose labels the
		//console's own button order settles.
		private string ButtonName(SetupButton button)
		{
			return _console == SetupConsole.MasterSystem
				? ControllerSheetRemap.ControlLabel(ConsoleType.Sms, button)
				: button.ToString();
		}

		private static string DefaultDeviceName(int device)
		{
			if(OperatingSystem.IsWindows() || device < 0) {
				return "";
			}
			return InputApi.GetGamepadInfo((uint)device, out GamepadInfo info) ? info.Name ?? "" : "";
		}
	}
}
