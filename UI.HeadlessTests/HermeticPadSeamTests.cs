using Mesen.Logic;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1112 review round 3: the headless host must not answer the Menu tick seam from
//real hardware. The pad bridge sets HapticTickOutput.PadInHand on every tick, and
//App wires PlayerSettingsEssentials.MenuTickAimable to it, so a pad-tick class that
//leaves PadInHand at 0 would make a later player-mode ConfigViewModel ask the real
//InputApi.IsGamepadAimable - true on a Mac with a haptic pad, false in CI.
[NativeCoreFree("Swaps the haptic seams only; the default under test answers without reaching the native core.")]
[Collection(PlayerSettingsSeamsCollection.Name)]
public class HermeticPadSeamTests
{
	[Fact]
	public void A_leaked_pad_in_hand_does_not_make_the_menu_tick_row_aimable()
	{
		try {
			HapticTickOutput.PadInHand = 0;
			Assert.False(HapticTickOutput.IsAimable(0), "the headless host answers 'not aimable' without asking real hardware");
			Assert.False(PlayerSettingsEssentials.MenuTickAimable());
		} finally {
			HapticTickOutput.PadInHand = -1;
		}
	}

	[Fact]
	public void The_hermetic_default_survives_a_test_that_restores_the_seams()
	{
		TestAppBuilder.ResetPadSeams();
		HapticTickOutput.PadInHand = 0;
		try {
			Assert.False(HapticTickOutput.IsAimable(0));
		} finally {
			HapticTickOutput.PadInHand = -1;
		}
	}
}
