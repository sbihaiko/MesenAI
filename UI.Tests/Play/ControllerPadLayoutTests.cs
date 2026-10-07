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

		//#940: each console draws its own pad - only the buttons it has, where
		//they sit on that console - and every key fits inside that console's body.
		[Theory]
		[InlineData(SetupConsole.Nes)]
		[InlineData(SetupConsole.GameBoy)]
		[InlineData(SetupConsole.MasterSystem)]
		[InlineData(SetupConsole.Gba)]
		public void Every_consoles_keys_fit_inside_its_own_body_without_overlap(SetupConsole console)
		{
			PadBody body = ControllerPadLayout.BodyOf(console);
			PadKey[] keys = ControllerSetupSteps.For(console).Select(b => ControllerPadLayout.Of(console, b)).Append(body.DPadCentre).ToArray();
			foreach(PadKey k in keys) {
				Assert.InRange(k.Left, 0, body.Width - k.Width);
				Assert.InRange(k.Top, 0, body.Height - k.Height);
			}
			for(int i = 0; i < keys.Length; i++) {
				for(int j = i + 1; j < keys.Length; j++) {
					bool apart = keys[i].Left + keys[i].Width <= keys[j].Left || keys[j].Left + keys[j].Width <= keys[i].Left
						|| keys[i].Top + keys[i].Height <= keys[j].Top || keys[j].Top + keys[j].Height <= keys[i].Top;
					Assert.True(apart, $"{console}: key {i} overlaps key {j}");
				}
			}
		}

		[Fact]
		public void The_NES_pad_is_the_wide_body_without_shoulders_or_screen()
		{
			PadBody body = ControllerPadLayout.BodyOf(SetupConsole.Nes);
			Assert.Equal((340d, 140d), (body.Width, body.Height));
			Assert.Null(body.Screen);
			Assert.Equal(new PadKey(279, 45, 34, 34, PadKeyShape.Round), ControllerPadLayout.Of(SetupConsole.Nes, SetupButton.A));
			Assert.Equal(new PadKey(124, 84, 40, 12, PadKeyShape.Pill), ControllerPadLayout.Of(SetupConsole.Nes, SetupButton.Select));
		}

		[Fact]
		public void The_Game_Boy_is_an_upright_handheld_with_its_buttons_below_the_screen()
		{
			PadBody body = ControllerPadLayout.BodyOf(SetupConsole.GameBoy);
			Assert.True(body.Height > body.Width, "the Game Boy is drawn upright");
			PadKey screen = Assert.IsType<PadKey>(body.Screen);
			foreach(SetupButton b in ControllerSetupSteps.For(SetupConsole.GameBoy)) {
				Assert.True(ControllerPadLayout.Of(SetupConsole.GameBoy, b).Top >= screen.Top + screen.Height, $"{b} is not below the screen");
			}
			PadKey a = ControllerPadLayout.Of(SetupConsole.GameBoy, SetupButton.A);
			PadKey bKey = ControllerPadLayout.Of(SetupConsole.GameBoy, SetupButton.B);
			Assert.True(a.Left > bKey.Left && a.Top < bKey.Top, "A sits right of and above B");
			PadKey select = ControllerPadLayout.Of(SetupConsole.GameBoy, SetupButton.Select);
			Assert.Equal(PadKeyShape.Pill, select.Shape);
			Assert.True(select.Top > a.Top + a.Height, "Select/Start sit below the face buttons");
		}

		[Fact]
		public void The_Master_System_pad_has_its_1_and_2_side_by_side()
		{
			PadBody body = ControllerPadLayout.BodyOf(SetupConsole.MasterSystem);
			Assert.Null(body.Screen);
			//Button 1 is KeyMapping B, button 2 is A (ControllerSheetRemap.ControlLabel).
			PadKey one = ControllerPadLayout.Of(SetupConsole.MasterSystem, SetupButton.B);
			PadKey two = ControllerPadLayout.Of(SetupConsole.MasterSystem, SetupButton.A);
			Assert.Equal(PadKeyShape.Round, one.Shape);
			Assert.Equal(one.Top, two.Top);
			Assert.True(one.Left + one.Width <= two.Left, "1 sits left of 2");
			Assert.DoesNotContain(SetupButton.Select, ControllerSetupSteps.For(SetupConsole.MasterSystem));
			Assert.DoesNotContain(SetupButton.Start, ControllerSetupSteps.For(SetupConsole.MasterSystem));
		}

		[Fact]
		public void The_GBA_keeps_its_shoulders_along_the_top()
		{
			Assert.Equal(new PadKey(28, 6, 64, 18, PadKeyShape.Shoulder), ControllerPadLayout.Of(SetupConsole.Gba, SetupButton.L));
			Assert.Equal(new PadKey(248, 6, 64, 18, PadKeyShape.Shoulder), ControllerPadLayout.Of(SetupConsole.Gba, SetupButton.R));
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
