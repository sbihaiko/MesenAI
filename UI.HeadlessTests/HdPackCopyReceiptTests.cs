using System;
using Mesen.Debugger.Utilities;
using Mesen.Interop;
using Xunit;

namespace Mesen.HeadlessTests;

//Issue #340, item 4, and ADR-0215's "anything that refuses needs to say why on
//screen". Every one of the 28 F12.2 cold-read sessions reported the same thing:
//nothing after the copy said it had worked or which tile it took, and the first
//confirmation was a `build` line two steps later.
//
//The old behaviour is the one this fails on: both copy entry points returned
//`void` and said nothing at all, so there was no receipt to assert. These cases
//need no ROM and no native core - the Tile Viewer's refusal is decided before
//any DebugApi call, which is itself the point: a viewer with no frame context
//never has to ask the core anything.
public class HdPackCopyReceiptTests
{
	private static readonly UInt32[] Palette = new UInt32[32];

	//ADR-0215 OPEN 3: the Tile Viewer reading PPU memory has neither a row nor a
	//frame, so it refuses - out loud.
	[Fact]
	public void A_refusal_carries_a_receipt_that_says_why()
	{
		HdPackCopyResult result = HdPackCopyHelper.CopyAsMepSheetCell(
			0x1FC0, MemoryType.NesPpuMemory, Palette, 0, false, HdPackCopyContext.None());

		Assert.False(result.Copied);
		Assert.Equal("", result.Text);
		Assert.NotEqual("", result.Receipt);
		Assert.Contains("not copied", result.Receipt);
		Assert.Contains("no row and no frame context", result.Receipt);
	}

	//The inherited Copy tile action needs the receipt as much as the MEP one, and
	//names itself so the two are told apart on screen.
	[Fact]
	public void Both_copy_actions_name_themselves_in_their_receipt()
	{
		HdPackCopyResult sheet = HdPackCopyHelper.CopyAsMepSheetCell(
			0x1FC0, MemoryType.NesPpuMemory, Palette, 0, false, HdPackCopyContext.None());
		HdPackCopyResult hdPack = HdPackCopyHelper.CopyToHdPackFormat(
			0x1FC0, MemoryType.NesPpuMemory, Palette, 0, false, HdPackCopyContext.None());

		Assert.StartsWith("MEP sheet cell", sheet.Receipt);
		Assert.StartsWith("HD pack tile", hdPack.Receipt);
	}
}
