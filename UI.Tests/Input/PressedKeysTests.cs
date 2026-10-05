using Mesen.Logic;
using System;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Input
{
	//What the host sees of the backend's pressed keys (#895): the native
	//GetPressedKeys export fills a buffer sized by PressedKeys.Capacity and
	//answers the size of the set it holds, and the host reads every non-zero slot
	//the buffer ends up with. It used to stop at three - the literal 3 written
	//into both UI/Interop/InputApi.cs and the native copy loop - so a fourth key
	//held at once vanished from the host while the backend still had it. Zero is
	//the empty sentinel; scan code 0 is never a pressed key.
	//
	//What these pin is the C# half of the contract: the buffer sizing, the growth
	//when the answer does not fit, and the read. The C++ half - the export
	//answering the set's size and stopping at the caller's bound - is not
	//reachable from here (UI.Tests runs with no core), and the fakes below stand
	//in for it, so a change to the native side is not caught by this file. The
	//round trip against a live core is the only thing that is.
	public class PressedKeysTests
	{
		[Fact]
		public void Decode_ThreeKeys_AllSeen()
		{
			ushort[] buffer = { 65, 66, 67 };
			Assert.Equal(new List<ushort> { 65, 66, 67 }, PressedKeys.Decode(buffer));
		}

		[Fact]
		public void Decode_FourKeys_AllSeen()
		{
			ushort[] buffer = { 65, 66, 67, 68 };
			Assert.Equal(new List<ushort> { 65, 66, 67, 68 }, PressedKeys.Decode(buffer));
		}

		[Fact]
		public void Decode_MoreThanThree_AllSeen()
		{
			//Shift + Ctrl + Alt plus a direction and a pad button: the case from
			//the issue, past the old hard cap.
			ushort[] buffer = { 0x1009, 116, 118, 120, 123, 126 };
			Assert.Equal(6, PressedKeys.Decode(buffer).Count);
		}

		[Fact]
		public void Decode_Empty_YieldsNothing()
		{
			ushort[] buffer = new ushort[PressedKeys.Capacity];
			Assert.Empty(PressedKeys.Decode(buffer));
		}

		//A stand-in for the native export: it fills the buffer it is handed, up to
		//the buffer's own end, and answers the size of the set it holds - which is
		//the number Read has to look at.
		private static Func<ushort[], int> Backend(ushort[] held, List<int> asks)
		{
			return (ushort[] buffer) => {
				asks.Add(buffer.Length);
				for(int i = 0; i < held.Length && i < buffer.Length; i++) {
					buffer[i] = held[i];
				}
				return held.Length;
			};
		}

		private static ushort[] Held(int count)
		{
			//Distinct, non-zero codes: 0 is the empty sentinel.
			ushort[] held = new ushort[count];
			for(int i = 0; i < count; i++) {
				held[i] = (ushort)(0x1000 + i);
			}
			return held;
		}

		[Fact]
		public void Read_Asks_Once_When_Everything_Fits()
		{
			List<int> asks = new List<int>();
			List<ushort> keys = PressedKeys.Read(Backend(new ushort[] { 65, 66, 67, 68 }, asks));

			Assert.Equal(new List<ushort> { 65, 66, 67, 68 }, keys);
			Assert.Equal(new List<int> { PressedKeys.Capacity }, asks);
		}

		[Fact]
		public void Read_Grows_When_The_Backend_Holds_More_Than_The_Buffer()
		{
			//Two pads held at once on macOS is 48 codes (24 buttons each), and the
			//backend pushes pad buttons before the keyboard, so a set past the
			//first ask is a real cabinet, not a hypothetical one. The review of the
			//first cut of this fix is what named it: Capacity alone still loses the
			//tail, silently.
			ushort[] held = Held(48);
			List<int> asks = new List<int>();
			List<ushort> keys = PressedKeys.Read(Backend(held, asks));

			Assert.Equal(48, keys.Count);
			Assert.Equal(held[47], keys[47]);
			//Asked again, with room for exactly what the backend said it had.
			Assert.Equal(new List<int> { PressedKeys.Capacity, 48 }, asks);
		}

		[Fact]
		public void Read_Sees_The_Whole_Set_A_Windows_Joystick_Can_Hold()
		{
			//WindowsKeyManager pushes up to 144 DirectInput buttons per joystick
			//(`j < 16 + 128`), which is the largest single-device answer in the
			//tree and the number the review used against "32 is past anything a
			//player can hold down". All of it has to come back.
			ushort[] held = Held(144);
			List<int> asks = new List<int>();
			List<ushort> keys = PressedKeys.Read(Backend(held, asks));

			Assert.Equal(144, keys.Count);
			Assert.Equal(held[143], keys[143]);
		}

		[Fact]
		public void Read_Keeps_Up_With_A_Set_That_Grows_Between_Asks()
		{
			//Every ask is a fresh snapshot, so the first answer is not a promise
			//about the second. A review of the two-ask cut caught this: it sized
			//the second buffer from the FIRST answer and threw the second one
			//away, so a set that grew in between was truncated to the number the
			//first ask reported - the original bug at a larger size.
			ushort[] forty = Held(40);
			ushort[] sixty = Held(60);
			List<int> asks = new List<int>();
			int call = 0;
			List<ushort> keys = PressedKeys.Read((ushort[] buffer) => {
				asks.Add(buffer.Length);
				call++;
				//40 keys on the first ask, 60 from the second one on: a key went
				//down between them.
				ushort[] held = call == 1 ? forty : sixty;
				for(int i = 0; i < held.Length && i < buffer.Length; i++) {
					buffer[i] = held[i];
				}
				return held.Length;
			});

			Assert.Equal(60, keys.Count);
			Assert.Equal(sixty[59], keys[59]);
			Assert.Equal(new List<int> { PressedKeys.Capacity, 40, 60 }, asks);
		}

		[Fact]
		public void Read_Stops_Asking_At_The_Cap()
		{
			//A set growing faster than it is read must not turn Read into an
			//unbounded loop: the cap is what ends it, and what fits is kept.
			List<int> asks = new List<int>();
			int reported = 0;
			PressedKeys.Read((ushort[] buffer) => {
				asks.Add(buffer.Length);
				reported += 64;
				return reported;
			});

			Assert.Equal(PressedKeys.MaxAsks, asks.Count);
		}

		[Fact]
		public void Read_Does_Not_Grow_Past_The_Sanity_Ceiling()
		{
			//A backend answering past MaxKeys is not describing pressed keys. The
			//host grows to the ceiling and stops, rather than allocating whatever
			//it was told - a stale core library paired with this caller is the way
			//that answer arrives.
			List<int> asks = new List<int>();
			PressedKeys.Read((ushort[] buffer) => {
				asks.Add(buffer.Length);
				return 100000;
			});

			Assert.Equal(new List<int> { PressedKeys.Capacity, PressedKeys.MaxKeys }, asks);
		}

		[Fact]
		public void Read_Keeps_What_Fits_When_The_Set_Shrinks_Between_Asks()
		{
			//The second ask is answered by the same live manager, which may have
			//lost a key in between: the slots past what it filled stay zero, and
			//zero is never a key.
			List<int> asks = new List<int>();
			int call = 0;
			List<ushort> keys = PressedKeys.Read((ushort[] buffer) => {
				asks.Add(buffer.Length);
				call++;
				if(call == 1) {
					return 48;
				}
				buffer[0] = 65;
				return 1;
			});

			Assert.Equal(new List<ushort> { 65 }, keys);
		}
	}
}
