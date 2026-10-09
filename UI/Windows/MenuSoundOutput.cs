using System;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.Windows
{
	//#1105: where a menu sound leaves the app - the host entry point
	//(EmuApi.PlayMenuSound), which answers "not available" until the audio path
	//exists. A headless case swaps the sink to assert the call.
	public static class MenuSoundOutput
	{
		private static Action<MenuSoundKind> _sink = ToDevice;

		private static void ToDevice(MenuSoundKind kind)
		{
			short[] pcm = MenuSounds.Render(kind);
			EmuApi.PlayMenuSound(pcm, (uint)(pcm.Length / 2), MenuSounds.SampleRate);
		}

		//The host capability; a build without the core answers "not available".
		public static bool HostAvailable()
		{
			try {
				return EmuApi.MenuSoundsAvailable();
			} catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException) {
				return false;
			}
		}

		public static void Play(MenuSoundKind kind) => _sink(kind);

		//Null puts the device back.
		public static void SetSinkForTest(Action<MenuSoundKind>? sink)
		{
			_sink = sink ?? ToDevice;
		}
	}
}
