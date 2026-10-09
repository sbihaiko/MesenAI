using System;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.Windows
{
	//#1105: where a menu sound leaves the app - the core's existing audio device
	//(EmuApi.PlayMenuSound), not a second mixer. A headless case swaps the sink
	//to assert the call, since a headless build has no device to hear.
	public static class MenuSoundOutput
	{
		private static Action<MenuSoundKind> _sink = ToDevice;

		private static void ToDevice(MenuSoundKind kind)
		{
			short[] pcm = MenuSounds.Render(kind);
			EmuApi.PlayMenuSound(pcm, (uint)(pcm.Length / 2), MenuSounds.SampleRate);
		}

		public static void Play(MenuSoundKind kind) => _sink(kind);

		//Null puts the device back.
		public static void SetSinkForTest(Action<MenuSoundKind>? sink)
		{
			_sink = sink ?? ToDevice;
		}
	}
}
