using Mesen.Logic;
using System.Collections.Generic;
using Xunit;

namespace Mesen.Tests.Input
{
	//What the host sees of the backend's pressed keys (#895): the native
	//GetPressedKeys export fills a buffer sized by PressedKeys.Capacity and the
	//host reads every non-zero slot. It used to stop at three - the literal 3
	//written into both UI/Interop/InputApi.cs and the native copy loop - so a
	//fourth key held at once vanished from the host while the backend still had
	//it. Zero is the empty sentinel; scan code 0 is never a pressed key.
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
	}
}
