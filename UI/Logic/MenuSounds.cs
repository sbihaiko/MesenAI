using System;

namespace Mesen.Logic;

public enum MenuSoundKind { Move, Confirm, Back }

//#1105 (spec #1102 slice 6): the optional soft sounds on move / confirm / back
//(Settings › Audio › Menu sounds, off on a new install). The set is rendered
//here - three short sine blips, so nothing binary ships - at one fixed low
//level, and goes out through the host entry point, which answers "not available" until the audio path exists (its own ADR). Pure, so the
//rules are pinned host-free in UI.Tests/Play/MenuSoundsTests.
public static class MenuSounds
{
	public const int SampleRate = 48000;

	//Fixed, not a setting: 15 % of full scale, and the master volume still
	//applies on top of it.
	public const double Level = 0.15;

	public static MenuSoundKind? For(PadNavAction action) => action switch {
		PadNavAction.Up or PadNavAction.Down or PadNavAction.Left or PadNavAction.Right => MenuSoundKind.Move,
		PadNavAction.Confirm => MenuSoundKind.Confirm,
		PadNavAction.Back => MenuSoundKind.Back,
		_ => null
	};

	//Never over a game that is running: the pad belongs to the console then
	//(ADR-0256 Decision 1), and the sound would mix into the game's audio.
	public static bool ShouldPlay(bool enabled, bool gameRunningUnpaused) => enabled && !gameRunningUnpaused;

	//Interleaved stereo, 16-bit.
	public static short[] Render(MenuSoundKind kind)
	{
		(double hz, double seconds) = kind switch {
			MenuSoundKind.Move => (880.0, 0.04),
			MenuSoundKind.Confirm => (1320.0, 0.09),
			_ => (440.0, 0.09)
		};
		int frames = (int)(SampleRate * seconds);
		short[] pcm = new short[frames * 2];
		for(int i = 0; i < frames; i++) {
			//A linear fade-out keeps the end from clicking.
			double envelope = 1.0 - (double)i / frames;
			short sample = (short)(Math.Sin(2 * Math.PI * hz * i / SampleRate) * envelope * Level * short.MaxValue);
			pcm[i * 2] = sample;
			pcm[i * 2 + 1] = sample;
		}
		return pcm;
	}
}
