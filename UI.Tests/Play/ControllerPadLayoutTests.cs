using Mesen.Logic;
using System.Linq;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0249 (W-P15): the setup sheet draws the console's pad - a 340 x 140
	//body with the D-pad on the left, Select/Start pills in the middle and the
	//B/A circles on the right - and each step's button sits where it is on a
	//real pad, so the lit one shows which to press.
	public class ControllerPadLayoutTests
	{
		[Fact]
		public void The_A_and_B_circles_sit_right_with_A_higher()
		{
			PadKey a = ControllerPadLayout.Of(SetupButton.A);
			PadKey b = ControllerPadLayout.Of(SetupButton.B);
			Assert.Equal(new PadKey(279, 45, 34, 34, PadKeyShape.Round), a);
			Assert.Equal(new PadKey(227, 67, 34, 34, PadKeyShape.Round), b);
		}

		[Theory]
		[InlineData(SetupButton.Up, 58, 34, 24, 24)]
		[InlineData(SetupButton.Down, 58, 82, 24, 24)]
		[InlineData(SetupButton.Left, 34, 58, 24, 24)]
		[InlineData(SetupButton.Right, 82, 58, 24, 24)]
		public void The_d_pad_arms_surround_its_centre(SetupButton button, double left, double top, double width, double height)
		{
			Assert.Equal(new PadKey(left, top, width, height, PadKeyShape.DPad), ControllerPadLayout.Of(button));
		}

		[Fact]
		public void Select_and_Start_are_pills_either_side_of_the_middle()
		{
			Assert.Equal(new PadKey(124, 84, 40, 12, PadKeyShape.Pill), ControllerPadLayout.Of(SetupButton.Select));
			Assert.Equal(new PadKey(176, 84, 40, 12, PadKeyShape.Pill), ControllerPadLayout.Of(SetupButton.Start));
		}

		[Fact]
		public void Every_steps_key_fits_inside_the_pad_without_overlap()
		{
			foreach(SetupConsole console in new[] { SetupConsole.Nes, SetupConsole.MasterSystem, SetupConsole.Gba }) {
				PadKey[] keys = ControllerSetupSteps.For(console).Select(ControllerPadLayout.Of).ToArray();
				foreach(PadKey k in keys) {
					Assert.InRange(k.Left, 0, ControllerPadLayout.Width - k.Width);
					Assert.InRange(k.Top, 0, ControllerPadLayout.Height - k.Height);
				}
				for(int i = 0; i < keys.Length; i++) {
					for(int j = i + 1; j < keys.Length; j++) {
						bool apart = keys[i].Left + keys[i].Width <= keys[j].Left || keys[j].Left + keys[j].Width <= keys[i].Left
							|| keys[i].Top + keys[i].Height <= keys[j].Top || keys[j].Top + keys[j].Height <= keys[i].Top;
						Assert.True(apart, $"{console}: key {i} overlaps key {j}");
					}
				}
			}
		}

		[Theory]
		[InlineData(SetupButton.A, true)]
		[InlineData(SetupButton.B, true)]
		[InlineData(SetupButton.L, true)]
		[InlineData(SetupButton.Up, false)]
		[InlineData(SetupButton.Start, false)]
		public void Only_the_face_and_shoulder_buttons_carry_their_letter(SetupButton button, bool labelled)
		{
			Assert.Equal(labelled, ControllerPadLayout.ShowsLabel(button));
		}
	}
}
