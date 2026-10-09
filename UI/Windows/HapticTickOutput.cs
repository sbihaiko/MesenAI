using System;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.Windows
{
	//#1112: where a menu tick leaves the app - the host's per-pad call
	//(InputApi.TickGamepad) and its aimable query (InputApi.IsGamepadAimable, true
	//only for a macOS pad with haptics today). A headless case swaps both seams, so
	//no hardware is needed. PadInHand is kept by the pad bridge on every tick.
	public static class HapticTickOutput
	{
		private static Func<uint, bool> _isAimable = ToHost;
		private static Action<uint> _tick = index => InputApi.TickGamepad(index);

		//The device index of the pad the player last pressed, -1 for none.
		private static int _padInHand = -1;
		public static int PadInHand {
			get => _padInHand;
			set {
				if(_padInHand != value) {
					_padInHand = value;
					PlayerSettingsEssentials.RaiseMenuTickAimableChanged();
				}
			}
		}

		private static bool ToHost(uint index)
		{
			try {
				return InputApi.IsGamepadAimable(index);
			} catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException) {
				return false;
			}
		}

		public static bool IsAimable(uint index) => _isAimable(index);

		public static void Tick(uint index) => _tick(index);

		//Whether the pad in hand can be ticked on its own: what shows the row.
		public static bool PadInHandAimable() => PadInHand >= 0 && _isAimable((uint)PadInHand);

		//Null puts the host back.
		public static void SetSeamsForTest(Func<uint, bool>? isAimable, Action<uint>? tick)
		{
			_isAimable = isAimable ?? ToHost;
			_tick = tick ?? (index => InputApi.TickGamepad(index));
		}
	}
}
