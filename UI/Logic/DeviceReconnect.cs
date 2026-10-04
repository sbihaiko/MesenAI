using Mesen.Interop;
using System.Collections.Generic;

namespace Mesen.Logic
{
	//ADR-0255 slice 5, the host-free rule: a device index is a connection
	//ordering, not an identity. Unplug a pad, plug it back, and the host can hand
	//it device 0 where it was device 1 - its keys (base + device * 0x100 + button)
	//still sit under the old index, so the port it was assigned to now speaks for
	//a different pad. What survives the reconnect is the pad's identity, and this
	//rule moves its keys when that identity appears at a different index.
	//
	//The identity is (Backend, VendorId, ProductId) - NOT VID:PID alone, because
	//Windows numbers two pad families (XInput at BaseGamepadIndex, DirectInput at
	//BaseDirectInputIndex) and the same pair means a different key block in each.
	//A pad whose pair is 0:0000 is unidentified and is never moved: it cannot be
	//told from any other unidentified pad.
	//
	//Honest scope (ADR-0255, "The answers, against the code"): TWO of the three
	//backends report no VID:PID - macOS (GameController) always, Windows XInput
	//always - so on those backends EVERY pad is unidentified and this repair never
	//fires. It is inert on macOS, the platform the tagged release ships on, and on
	//every XInput pad on Windows. It can only fire on Windows DirectInput and on
	//Linux evdev, which are the only backends that report a real VID:PID. This is
	//a best-effort repair on the backends that can support it, not a promise that
	//a pad keeps its player everywhere.
	//
	//Free of Avalonia/EmuApi so UI.Tests pins it; the config walk that rewrites the
	//KeyMapping slots lives host-aware in UI/Config/ControllerKeyMigration.
	public readonly record struct PadIdentity(GamepadBackend Backend, int DeviceIndex, uint VendorId, uint ProductId);

	//One identified pad's keys leaving the device index they were bound under for
	//the index the pad enumerates as now. Both indexes are within `Backend`'s own
	//family (an XInput slot, a DirectInput ordinal), which is what its key codes
	//carry. See ShortcutKeyRules::DirectInputDeviceOf (#813).
	public readonly record struct DeviceMove(GamepadBackend Backend, int FromIndex, int ToIndex);

	public static class DeviceReconnect
	{
		//A pad with no VID:PID cannot be keyed - it is indistinguishable from
		//every other unidentified pad - so it is never moved.
		public static bool IsIdentified(PadIdentity pad) => pad.VendorId != 0 || pad.ProductId != 0;

		//A pad the host left unresolved (`GamepadBackend.None`) has no known key
		//family, so the index its keys carry cannot be known either. It is refused
		//the same way 0:0000 is, rather than read as the one-family default.
		public static bool IsTrackable(PadIdentity pad) =>
			pad.Backend != GamepadBackend.None && IsIdentified(pad);

		//The key-code base a backend numbers its pad keys from. Every backend
		//shares BaseGamepadIndex except Windows DirectInput, whose joysticks sit
		//at BaseDirectInputIndex - the same split ShortcutKeyRules reads (see
		//IKeyManager::BaseDirectInputIndex / the PadFamilies note).
		public static int FamilyBase(GamepadBackend backend) =>
			backend == GamepadBackend.DirectInput ? ControllerDevices.BaseDirectInputIndex : ControllerDevices.BaseGamepadIndex;

		//The device index a key code carries WITHIN `backend`'s family, or null
		//when the key is not a pad key of that family (a keyboard key, or a key
		//from the other Windows family). This is `Slot`'s numbering: the index a
		//mapping's key code carries, which #813 made `Slot` mean for DirectInput.
		public static int? DeviceOf(ushort keyCode, GamepadBackend backend)
		{
			int baseIndex = FamilyBase(backend);
			return keyCode >= baseIndex ? (keyCode - baseIndex) >> 8 : null;
		}

		//The key code with its device index replaced and its button bits untouched.
		//A key that is not a pad key of `backend`'s family belongs to no device here
		//and is returned as is.
		public static ushort MoveKey(ushort keyCode, GamepadBackend backend, int from, int to)
		{
			if(from == to || DeviceOf(keyCode, backend) != from) {
				return keyCode;
			}
			return (ushort)(keyCode - from * 0x100 + to * 0x100);
		}

		//A key code read through the moves: its device index moves if one of the
		//moves names it within its own backend, its button bits are untouched. The
		//first matching move wins, which is enough because a key belongs to exactly
		//one family and a move's source is unique within it; a key is always read
		//from its original code, so a swap of two indexes moves both sides.
		public static ushort RemapKey(ushort keyCode, IReadOnlyList<DeviceMove> moves)
		{
			foreach(DeviceMove move in moves) {
				if(move.FromIndex != move.ToIndex && DeviceOf(keyCode, move.Backend) == move.FromIndex) {
					return MoveKey(keyCode, move.Backend, move.FromIndex, move.ToIndex);
				}
			}
			return keyCode;
		}
	}

	//ADR-0255 slice 5: the reconnect history. It remembers, per (Backend,VID,PID),
	//the device index the pad was last seen at - across the ticks where the pad is
	//unplugged and gone, because forgetting it there is forgetting the one fact the
	//repair needs - and answers which pads' keys must move on this observation.
	//
	//An unidentified pad is never keyed and never moved. Two present pads sharing
	//an identity make the move ambiguous (which is which cannot be known), and an
	//ambiguous identity stops being tracked rather than being guessed at.
	public sealed class DeviceReconnectHistory
	{
		private readonly Dictionary<Identity, int> _lastIndex = new();

		//Which pads' keys move, given every pad connected right now. The moves are
		//simultaneous, not sequential: the config walk applies them as a set.
		public IReadOnlyList<DeviceMove> Observe(IReadOnlyList<PadIdentity> pads)
		{
			//How many pads carry each identity right now. Two of the same model
			//are indistinguishable, so an identity present twice is ambiguous: no
			//move may be guessed for it (ADR-0255, second correction, point 4).
			Dictionary<Identity, int> presentCount = new();
			foreach(PadIdentity pad in pads) {
				if(!DeviceReconnect.IsTrackable(pad)) {
					continue;
				}
				Identity id = new(pad.Backend, pad.VendorId, pad.ProductId);
				presentCount[id] = presentCount.TryGetValue(id, out int seen) ? seen + 1 : 1;
			}

			//An ambiguous identity stops being tracked: which pad is which cannot
			//be known, so there is no index to remember and no future move can be
			//guessed from a stale one. The next unambiguous sighting is a baseline.
			foreach(KeyValuePair<Identity, int> entry in presentCount) {
				if(entry.Value > 1) {
					_lastIndex.Remove(entry.Key);
				}
			}

			//Every (backend, index) a tracked identity was last seen at. A move into
			//an index whose keys were not this pad's must not overwrite them.
			HashSet<(GamepadBackend, int)> recorded = new();
			foreach(KeyValuePair<Identity, int> entry in _lastIndex) {
				recorded.Add((entry.Key.Backend, entry.Value));
			}

			List<(Identity Id, DeviceMove Move)> candidates = new();
			HashSet<(GamepadBackend, int)> vacating = new();
			Dictionary<(GamepadBackend, int), int> sourceCount = new();
			Dictionary<Identity, int> firstSighting = new();
			foreach(PadIdentity pad in pads) {
				if(!DeviceReconnect.IsTrackable(pad)) {
					continue;
				}
				Identity id = new(pad.Backend, pad.VendorId, pad.ProductId);
				if(presentCount[id] > 1) {
					continue; //ambiguous: neither moved nor remembered
				}
				if(!_lastIndex.TryGetValue(id, out int last)) {
					//Nothing recorded to move from: this sighting IS the baseline.
					firstSighting[id] = pad.DeviceIndex;
					continue;
				}
				if(last == pad.DeviceIndex) {
					continue;
				}
				candidates.Add((id, new DeviceMove(pad.Backend, last, pad.DeviceIndex)));
				vacating.Add((pad.Backend, last));
				(GamepadBackend, int) source = (pad.Backend, last);
				sourceCount[source] = sourceCount.TryGetValue(source, out int count) ? count + 1 : 1;
			}

			//A move is kept only when it is unambiguous and touches no other pad's
			//keys: two pads recorded at the same source index (whose keys sat there
			//is unknown) drop, and a target whose keys belonged to another pad that
			//is not vacating that index drops.
			List<DeviceMove> moves = new();
			HashSet<Identity> advanced = new();
			HashSet<(GamepadBackend, int)> targets = new();
			foreach((Identity id, DeviceMove move) in candidates) {
				(GamepadBackend, int) source = (move.Backend, move.FromIndex);
				(GamepadBackend, int) target = (move.Backend, move.ToIndex);
				bool ambiguousSource = sourceCount[source] > 1;
				bool targetForeign = recorded.Contains(target) && !vacating.Contains(target);
				bool targetTaken = !targets.Add(target);
				if(ambiguousSource || targetForeign || targetTaken) {
					continue;
				}
				moves.Add(move);
				advanced.Add(id);
			}

			//Remember the ordering for the next observation - but only where the
			//pad's keys now actually are. A dropped move leaves them at the index
			//they were at, so the baseline stays there too: advancing it past a drop
			//would claim an index this pad's keys do not occupy and let a later,
			//otherwise-legal move rewrite the pad that does occupy it.
			foreach(KeyValuePair<Identity, int> entry in firstSighting) {
				_lastIndex[entry.Key] = entry.Value;
			}
			foreach((Identity id, DeviceMove move) in candidates) {
				if(advanced.Contains(id)) {
					_lastIndex[id] = move.ToIndex;
				}
			}
			return moves;
		}

		private readonly record struct Identity(GamepadBackend Backend, uint VendorId, uint ProductId);
	}
}
