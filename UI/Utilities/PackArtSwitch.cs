using System;

namespace Mesen.Utilities
{
	//#663: the core stops drawing a pack's art while the HD pack builder records
	//(NesConsole/Gameboy/SmsConsole::IsDrawingPackArt are false under a builder)
	//and draws it again when the recording stops - with the same game and the
	//same pack, so neither RomInfo nor CurrentPackName changes. Whoever starts
	//or stops a recording raises this, on the UI thread, once the core call has
	//returned; Settings › Look (ADR-0246 §3) re-reads its Pixels lock on it.
	public static class PackArtSwitch
	{
		public static event Action? Switched;

		public static void Raise()
		{
			Switched?.Invoke();
		}
	}
}
