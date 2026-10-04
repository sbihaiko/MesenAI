using Mesen.Interop;
using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0255 slice 5: the reconnect repair. A device index is a connection
	//ordering, not an identity - unplug a pad, plug it back, and the host can
	//hand it a different index while its keys stay under the old one. The pad's
	//identity (Backend, VID, PID) is what survives, so the history records the
	//index each identity was last seen at and answers which pads' keys must move.
	//Host-free, so these pin the rule with no Avalonia and no EmuApi; the config
	//walk is elsewhere.
	public class DeviceReconnectTests
	{
		private const uint Vid = 0x054C, Pid = 0x0CE6; //DualSense
		private const uint OtherVid = 0x045E, OtherPid = 0x028E; //Xbox 360
		private const uint ThirdVid = 0x1532, ThirdPid = 0x0A29; //a third model

		//A one-family key code (evdev, macOS, XInput): base + device * 0x100 + button.
		private static ushort Key(int device, int button) => (ushort)(0x1000 + device * 0x100 + button);

		//A Windows DirectInput joystick key: 0x2000 + device * 0x100 + button.
		private static ushort DiKey(int device, int button) => (ushort)(0x2000 + device * 0x100 + button);

		//Evdev is a one-family identified backend, which is the shape the review's
		//reconnect rule is about.
		private static PadIdentity Pad(int index, uint vid, uint pid) => new(GamepadBackend.Evdev, index, vid, pid);

		private static readonly PadIdentity[] _nothing = System.Array.Empty<PadIdentity>();

		[Fact]
		public void First_sighting_records_the_index_and_moves_nothing()
		{
			DeviceReconnectHistory history = new();

			Assert.Empty(history.Observe(new[] { Pad(1, Vid, Pid) }));
			//Still nothing when it stays put on the next observation.
			Assert.Empty(history.Observe(new[] { Pad(1, Vid, Pid) }));
		}

		[Fact]
		public void A_pad_returning_at_another_index_moves_its_keys()
		{
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(1, Vid, Pid) });

			DeviceMove move = Assert.Single(history.Observe(new[] { Pad(0, Vid, Pid) }));
			Assert.Equal(new DeviceMove(GamepadBackend.Evdev, 1, 0), move);
			//And the move is remembered: the next observation finds it settled.
			Assert.Empty(history.Observe(new[] { Pad(0, Vid, Pid) }));
		}

		[Fact]
		public void The_remembered_index_survives_the_ticks_where_the_pad_is_gone()
		{
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(1, Vid, Pid) });

			//Unplugged: no pads at all, then the pad comes back on another index.
			history.Observe(_nothing);

			DeviceMove move = Assert.Single(history.Observe(new[] { Pad(0, Vid, Pid) }));
			Assert.Equal(new DeviceMove(GamepadBackend.Evdev, 1, 0), move);
		}

		[Fact]
		public void An_unidentified_pad_is_never_moved()
		{
			DeviceReconnectHistory history = new();
			//VID:PID 0:0000 cannot be keyed - every pad on macOS and every XInput
			//pad on Windows reports it, so this is the normal path there, not an
			//edge case.
			Assert.Empty(history.Observe(new[] { Pad(1, 0, 0) }));
			Assert.Empty(history.Observe(new[] { Pad(0, 0, 0) }));
		}

		[Fact]
		public void A_move_into_an_index_another_pad_was_bound_at_is_dropped()
		{
			DeviceReconnectHistory history = new();
			//The Xbox pad's keys were bound at index 0, then it was unplugged.
			history.Observe(new[] { Pad(0, OtherVid, OtherPid) });
			//The DualSense arrives and lands on index 1, then reconnects onto 0 -
			//where the Xbox pad's keys still are. It is not this pad's index, so
			//the move is dropped rather than overwriting those keys.
			history.Observe(new[] { Pad(1, Vid, Pid) });

			Assert.Empty(history.Observe(new[] { Pad(0, Vid, Pid) }));
		}

		[Fact]
		public void A_dropped_move_does_not_rebaseline_the_pad_at_an_index_its_keys_are_not_at()
		{
			//Found in review: the history recorded the pad's new index even when the
			//move had been dropped, so the baseline claimed an index the pad's keys do
			//not occupy - and a later, otherwise-legal move then rewrote the other
			//pad's keys.
			DeviceReconnectHistory history = new();
			//The DualSense's keys are at device 1 and the Xbox pad's at device 0.
			history.Observe(new[] { Pad(1, Vid, Pid) });
			history.Observe(new[] { Pad(1, Vid, Pid), Pad(0, OtherVid, OtherPid) });

			//The DualSense comes back on 0, where the Xbox pad's keys are: the move
			//is dropped, so its keys are still at 1.
			Assert.Empty(history.Observe(new[] { Pad(0, Vid, Pid) }));

			//Both pads are back on the index their keys are actually at: nothing
			//reconnected, so nothing may move. A baseline advanced past the dropped
			//move rewrites the Xbox pad's Port1 keys onto device 1 here.
			Assert.Empty(history.Observe(new[] { Pad(0, OtherVid, OtherPid), Pad(1, Vid, Pid) }));
		}

		[Fact]
		public void A_move_onto_an_index_a_dropped_ambiguous_source_did_not_vacate_is_refused()
		{
			//A candidate dropped because its source is ambiguous never moves, so its
			//source index is NOT vacated: its keys are still exactly where they were.
			//A later candidate whose target is that index must be refused, not land on
			//keys another pad still occupies (ADR-0255 Decision 5).
			DeviceReconnectHistory history = new();
			//Two pads both recorded at index 1 - whose keys sat there is unknown, so a
			//move out of 1 is ambiguous for both.
			history.Observe(new[] { Pad(1, Vid, Pid) });
			history.Observe(new[] { Pad(1, OtherVid, OtherPid) });
			history.Observe(new[] { Pad(3, ThirdVid, ThirdPid) });

			//Both ambiguous pads reconnect to fresh indexes (their moves drop), and
			//the third pad reconnects onto index 1, where the first two pads' keys
			//still are.
			Assert.Empty(history.Observe(new[] { Pad(2, Vid, Pid), Pad(4, OtherVid, OtherPid), Pad(1, ThirdVid, ThirdPid) }));
		}

		[Fact]
		public void A_move_onto_an_index_a_dropped_target_taken_move_did_not_vacate_is_refused()
		{
			//A candidate dropped because another candidate already claimed its target
			//never moves either, so its source is not vacated. A later candidate
			//targeting that source must be refused.
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(1, Vid, Pid) });
			history.Observe(new[] { Pad(2, OtherVid, OtherPid) });
			history.Observe(new[] { Pad(0, ThirdVid, ThirdPid) });

			//The third pad reconnects to 5; the DualSense also targets 5, so its move
			//is dropped for a taken target - leaving its keys at index 1. The Xbox
			//pad's move onto 1 must be refused; only the third pad's move survives.
			IReadOnlyList<DeviceMove> moves = history.Observe(new[] { Pad(5, ThirdVid, ThirdPid), Pad(5, Vid, Pid), Pad(1, OtherVid, OtherPid) });

			DeviceMove move = Assert.Single(moves);
			Assert.Equal(new DeviceMove(GamepadBackend.Evdev, 0, 5), move);
		}

		[Fact]
		public void A_pad_whose_backend_is_unresolved_is_never_moved()
		{
			//A pad with no resolved backend has no known key family, so the index its
			//keys carry cannot be known either - it is refused the same way 0:0000 is
			//rather than being read as the one-family default.
			DeviceReconnectHistory history = new();
			PadIdentity atOne = new(GamepadBackend.None, 1, Vid, Pid);
			PadIdentity atZero = new(GamepadBackend.None, 0, Vid, Pid);

			Assert.Empty(history.Observe(new[] { atOne }));
			Assert.Empty(history.Observe(new[] { atZero }));
		}

		[Fact]
		public void Two_identities_recorded_at_the_same_index_move_neither()
		{
			DeviceReconnectHistory history = new();
			//The DualSense was last seen at index 1, then the Xbox pad took the
			//index when the DualSense was gone - so whose keys sat there is now
			//unknown, and neither pad's move can claim them.
			history.Observe(new[] { Pad(1, Vid, Pid) });
			history.Observe(new[] { Pad(1, OtherVid, OtherPid) });

			Assert.Empty(history.Observe(new[] { Pad(0, Vid, Pid), Pad(2, OtherVid, OtherPid) }));
		}

		[Fact]
		public void Two_pads_sharing_an_identity_make_the_move_ambiguous_and_it_is_dropped()
		{
			//The review's finding 1: the history was keyed by VID:PID alone, so two
			//pads of the same model collapsed to the last index and the survivor's
			//keys were spuriously "moved" onto the other pad's device.
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(1, Vid, Pid) });

			//Both of the same model are present: which is which cannot be known, so
			//no move may be guessed.
			Assert.Empty(history.Observe(new[] { Pad(0, Vid, Pid), Pad(1, Vid, Pid) }));

			//One is unplugged; the survivor at the old index must not be read as a
			//reconnect of the other one.
			Assert.Empty(history.Observe(new[] { Pad(1, Vid, Pid) }));
		}

		[Fact]
		public void Two_pads_swapping_indexes_move_both()
		{
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(0, OtherVid, OtherPid), Pad(1, Vid, Pid) });

			//Both renumbered: the Xbox pad now enumerates as 1 and the DualSense
			//as 0. Each target is the other's source, so the moves swap the two
			//pads' keys instead of clobbering one of them.
			IReadOnlyList<DeviceMove> moves = history.Observe(new[] { Pad(0, Vid, Pid), Pad(1, OtherVid, OtherPid) });

			Assert.Equal(2, moves.Count);
			Assert.Contains(new DeviceMove(GamepadBackend.Evdev, 1, 0), moves);
			Assert.Contains(new DeviceMove(GamepadBackend.Evdev, 0, 1), moves);
		}

		[Fact]
		public void A_new_pad_with_an_unknown_identity_moves_nothing()
		{
			DeviceReconnectHistory history = new();
			history.Observe(new[] { Pad(0, OtherVid, OtherPid) });

			//The first sighting of the DualSense is not a reconnect: it has no
			//recorded index to move from.
			Assert.Empty(history.Observe(new[] { Pad(0, OtherVid, OtherPid), Pad(1, Vid, Pid) }));
		}

		[Fact]
		public void MoveKey_replaces_the_device_index_and_keeps_the_button()
		{
			//Device 1, button 3 -> device 0, button 3.
			Assert.Equal(Key(0, 3), DeviceReconnect.MoveKey(Key(1, 3), GamepadBackend.Evdev, 1, 0));
			//A key of another device is untouched by a move that is not its own.
			Assert.Equal(Key(2, 5), DeviceReconnect.MoveKey(Key(2, 5), GamepadBackend.Evdev, 1, 0));
			//A keyboard key (below the gamepad base) belongs to no device.
			Assert.Equal((ushort)0x0020, DeviceReconnect.MoveKey(0x0020, GamepadBackend.Evdev, 0, 1));
			//A move to the same index (or a key not on the moved device) is a no-op.
			Assert.Equal(Key(1, 3), DeviceReconnect.MoveKey(Key(1, 3), GamepadBackend.Evdev, 1, 1));
		}

		[Fact]
		public void RemapKey_moves_a_DirectInput_key_within_the_joystick_family()
		{
			//The review's finding 3: a DirectInput key is read within its own
			//family (0x2000 + ordinal * 0x100 + button), not as "device 16" of the
			//XInput family. A move on the joystick family moves its own key.
			IReadOnlyList<DeviceMove> moves = new[] { new DeviceMove(GamepadBackend.DirectInput, 1, 0) };

			Assert.Equal(DiKey(0, 3), DeviceReconnect.RemapKey(DiKey(1, 3), moves));
			Assert.Equal(DiKey(0, 6), DeviceReconnect.RemapKey(DiKey(1, 6), moves));
			//A different joystick ordinal is not this pad's key.
			Assert.Equal(DiKey(2, 3), DeviceReconnect.RemapKey(DiKey(2, 3), moves));
		}

		[Fact]
		public void A_DirectInput_move_leaves_the_XInput_family_alone()
		{
			//The same ordinal exists in both Windows families; the move names the
			//backend so only its own family is rewritten - an XInput key at device 1
			//is not the joystick at ordinal 1.
			IReadOnlyList<DeviceMove> moves = new[] { new DeviceMove(GamepadBackend.DirectInput, 1, 0) };

			Assert.Equal(Key(1, 3), DeviceReconnect.RemapKey(Key(1, 3), moves));

			//And the reverse: an evdev move does not touch a DirectInput key.
			IReadOnlyList<DeviceMove> evdev = new[] { new DeviceMove(GamepadBackend.Evdev, 1, 0) };
			Assert.Equal(Key(0, 3), DeviceReconnect.RemapKey(Key(1, 3), evdev));
			Assert.Equal(DiKey(1, 3), DeviceReconnect.RemapKey(DiKey(1, 3), evdev));
		}

		[Fact]
		public void An_empty_move_list_changes_nothing()
		{
			Assert.Equal(Key(1, 3), DeviceReconnect.RemapKey(Key(1, 3), System.Array.Empty<DeviceMove>()));
		}
	}
}
