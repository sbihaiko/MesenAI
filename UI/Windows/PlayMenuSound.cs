using System;
using Avalonia.Input;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.Windows
{
	//#1105 / #1127 (spec #1102 slice 6, ADR-0270 D10): the one place a menu blip
	//leaves the GUI. Both input paths that navigate the Play surfaces are handed
	//to it - the pad (PlayPadNavigationWiring's bridge) and the keyboard (the
	//window's own key handler) - so a keyboard move sounds through the same gate
	//and the same sink a pad move does. No second submit path, no second gate, no
	//new sink.
	//
	//The gate is judged here and only here, on the state the press LEFT: the press
	//that starts or resumes a game has already left the game running unpaused, and
	//a blip must not mix into it (MenuSounds.ShouldPlay; the rule itself is pure
	//and pinned in UI.Tests/Play/MenuSoundsTests).
	//
	//That makes WHEN a caller submits part of the rule, not a detail of it: the pad
	//submits after Apply, and the window's key handler - which runs in the tunnel,
	//before the focused control has activated - submits after the press has run.
	public static class PlayMenuSound
	{
		//The action an input path resolved, by the rules of its own device
		//(PlayPadNavigation.Next for the pad, OfNavigateKey for the keyboard).
		public static void For(PadNavAction action)
		{
			MenuSoundKind? kind = MenuSounds.For(action);
			if(kind is MenuSoundKind sound
				&& MenuSounds.ShouldPlay(ConfigManager.Config.Audio.MenuSounds, EmuApi.IsRunning() && !EmuApi.IsPaused())) {
				MenuSoundOutput.Play(sound);
			}
		}

		//The keyboard's move and confirm, as the keys the Play GUI acts on: the
		//arrows walk the surface the ring is on and Enter activates what it is on,
		//which are the pad's D-pad and its A. Back is Esc and it is NOT here: that
		//key is the window's overlay key, taken (or not) by its own Esc arm, which
		//submits it as Back where it takes the press.
		//
		//Nothing else is a navigation press: a letter the focused box is typing, a
		//Tab, a modifier chord - all answer PadNavAction.None, which sounds
		//nothing.
		public static PadNavAction OfNavigateKey(Key key)
		{
			return key switch {
				Key.Up => PadNavAction.Up,
				Key.Down => PadNavAction.Down,
				Key.Left => PadNavAction.Left,
				Key.Right => PadNavAction.Right,
				Key.Enter => PadNavAction.Confirm,
				_ => PadNavAction.None
			};
		}
	}
}
