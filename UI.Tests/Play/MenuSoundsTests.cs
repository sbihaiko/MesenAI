using System;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1105 (spec #1102 slice 6): optional soft sounds on move / confirm / back.
	public class MenuSoundsTests
	{
		[Theory]
		[InlineData(PadNavAction.Up, MenuSoundKind.Move)]
		[InlineData(PadNavAction.Down, MenuSoundKind.Move)]
		[InlineData(PadNavAction.Left, MenuSoundKind.Move)]
		[InlineData(PadNavAction.Right, MenuSoundKind.Move)]
		[InlineData(PadNavAction.Confirm, MenuSoundKind.Confirm)]
		[InlineData(PadNavAction.Back, MenuSoundKind.Back)]
		public void Each_pad_action_has_its_sound(PadNavAction action, MenuSoundKind expected)
		{
			Assert.Equal(expected, MenuSounds.For(action));
		}

		[Fact]
		public void No_action_has_no_sound()
		{
			Assert.Null(MenuSounds.For(PadNavAction.None));
		}

		[Fact]
		public void Silent_while_the_row_is_off()
		{
			Assert.False(MenuSounds.ShouldPlay(enabled: false, gameRunningUnpaused: false));
		}

		[Fact]
		public void Silent_while_a_game_runs_unpaused_even_with_the_row_on()
		{
			Assert.False(MenuSounds.ShouldPlay(enabled: true, gameRunningUnpaused: true));
		}

		[Fact]
		public void Plays_with_the_row_on_and_no_game_running_unpaused()
		{
			Assert.True(MenuSounds.ShouldPlay(enabled: true, gameRunningUnpaused: false));
		}

		[Theory]
		[InlineData(MenuSoundKind.Move)]
		[InlineData(MenuSoundKind.Confirm)]
		[InlineData(MenuSoundKind.Back)]
		public void Bundled_sounds_are_short_stereo_and_low(MenuSoundKind kind)
		{
			short[] pcm = MenuSounds.Render(kind);
			Assert.Equal(0, pcm.Length % 2);
			double seconds = pcm.Length / 2.0 / MenuSounds.SampleRate;
			Assert.InRange(seconds, 0.03, 0.25);
			int peak = pcm.Max(s => Math.Abs((int)s));
			Assert.InRange(peak, 1, (int)(short.MaxValue * 0.2));
		}

		[Fact]
		public void The_three_sounds_differ()
		{
			Assert.NotEqual(MenuSounds.Render(MenuSoundKind.Move), MenuSounds.Render(MenuSoundKind.Confirm));
			Assert.NotEqual(MenuSounds.Render(MenuSoundKind.Confirm), MenuSounds.Render(MenuSoundKind.Back));
		}
	}
}
