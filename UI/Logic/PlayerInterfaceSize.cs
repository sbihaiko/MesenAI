using System;

namespace Mesen.Logic;

//#1111 (spec #1102, PRD Part B §13.3): Settings › Display › Interface size.
public enum InterfaceSize { Standard, Large, ExtraLarge }

//The factor scales Play's chrome (MainWindow's PlayChromeRoot) and nothing
//else: not the emulated picture and not the Scale row, which keep their own
//meaning. Pure, so the rule is pinned host-free (UI.Tests/Play/InterfaceSizeTests).
public static class PlayerInterfaceSize
{
	public static double Factor(InterfaceSize size) => size switch {
		InterfaceSize.Large => 1.25,
		InterfaceSize.ExtraLarge => 1.5,
		_ => 1.0
	};

	//One step, stopping at both ends (no wrap), like the pad's other rows.
	public static InterfaceSize Step(InterfaceSize size, int delta)
	{
		return (InterfaceSize)Math.Clamp((int)size + delta, (int)InterfaceSize.Standard, (int)InterfaceSize.ExtraLarge);
	}
}
