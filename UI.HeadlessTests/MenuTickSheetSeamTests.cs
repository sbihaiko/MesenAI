using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1112: the Menu tick row and the sheet's height read one answer. The aimable
//seam follows the pad in hand, which a press can change while Settings is open,
//so two reads at two moments (the row's visibility at build, the height on every
//tab change) could disagree: a 388 px sheet with no row, or a 340 px one with four.
[NativeCoreFree("Drives the Controls view-model through injected pad and device sources and a swapped aimable seam; no config write reaches the native core.")]
[Collection("PlayerSettingsSeams")]
public class MenuTickSheetSeamTests : System.IDisposable
{
	private readonly System.Func<bool> _original = PlayerSettingsEssentials.MenuTickAimable;

	public void Dispose() => PlayerSettingsEssentials.MenuTickAimable = _original;

	private static void AssertRowMatchesHeight(ConfigViewModel model)
	{
		Assert.NotNull(model.PlayerControls);
		Assert.Equal(model.PlayerControls!.MenuTickAvailable, model.PlayerSheetHeight == 388);
	}

	[Theory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void The_row_is_visible_exactly_when_the_sheet_is_388_after_the_pad_changes_between_tab_visits(bool before, bool after)
	{
		PlayerSettingsEssentials.MenuTickAimable = () => before;
		ConfigViewModel model = new(ConfigWindowTab.Input, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 1);
		AssertRowMatchesHeight(model);

		model.PlayerTabIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Display);
		PlayerSettingsEssentials.MenuTickAimable = () => after;
		model.PlayerTabIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input);

		AssertRowMatchesHeight(model);
		model.Dispose();
	}
}
