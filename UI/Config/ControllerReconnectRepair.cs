using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Collections.Generic;

namespace Mesen.Config
{
	//ADR-0255 slice 5, the driver: notice a pad that came back at a different
	//device index, move its keys, and write the result through the same
	//ConfigManager/ApplyConfig path the classic Input page uses - so the sheet and
	//the classic page never disagree about which slot a pad is in. The decision is
	//host-free (UI/Logic/DeviceReconnect); this is the part that reads the host
	//and touches the configuration.
	//
	//Honest scope: on macOS and on Windows XInput every pad reports 0:0000, so
	//every pad is unidentified and this driver observes nothing to move. It fires
	//only on Windows DirectInput and on Linux evdev, the backends that report a
	//real VID:PID. That is expected (ADR-0255's second correction), not a bug; it
	//is why the repair makes no promise of stable membership on the other
	//backends.
	//
	//It runs off the window's existing 50 ms Play poll: a reconnect is noticed on
	//the tick after it happens, and the first observation of a session is the
	//baseline (there is nothing recorded yet to move from).
	public sealed class ControllerReconnectRepair
	{
		private readonly DeviceReconnectHistory _history = new();

		//Injectable for the headless tests: read the pads, and where the result
		//goes. The defaults are the host reads and ConfigManager/ApplyConfig.
		public Func<IReadOnlyList<PadIdentity>> ReadPads { get; set; } = ReadHostPads;
		public Action Apply { get; set; }
		public Action Save { get; set; }

		//The configuration the repair writes. The app's own by default; a test can
		//hand it a fresh one so it never touches the player's settings.
		private Configuration? _target;
		public Configuration Target
		{
			get => _target ??= ConfigManager.Config;
			set => _target = value;
		}

		public ControllerReconnectRepair()
		{
			Apply = () => Target.ApplyConfig();
			Save = () => Target.Save();
		}

		//One observation. Returns the moves it made (empty when nothing
		//reconnected, or when the moves touched no key). Only writes the
		//configuration when at least one key actually moved.
		public IReadOnlyList<DeviceMove> Check()
		{
			IReadOnlyList<DeviceMove> moves = _history.Observe(ReadPads());
			if(moves.Count == 0) {
				return moves;
			}
			if(ControllerKeyMigration.Apply(Target, moves) > 0) {
				Apply();
				Save();
			}
			return moves;
		}

		//Every connected pad as a (backend, family-relative device index, VID:PID)
		//identity. The device index is `Slot` - the index a mapping's key code
		//carries - NOT the ordinal GetGamepadInfo was handed: on Windows that
		//ordinal is global across the XInput slots and the joysticks, and #813 is
		//the fix that makes Slot the family's own numbering. A pad the host cannot
		//describe (GetGamepadInfo false) is skipped rather than guessed.
		public static IReadOnlyList<PadIdentity> ReadHostPads()
		{
			uint count = InputApi.GetConnectedGamepadCount();
			List<PadIdentity> pads = new((int)count);
			for(uint i = 0; i < count; i++) {
				if(InputApi.GetGamepadInfo(i, out GamepadInfo info)) {
					pads.Add(new PadIdentity(info.Backend, (int)info.Slot, info.VendorId, info.ProductId));
				}
			}
			return pads;
		}
	}
}
