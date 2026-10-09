using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Xunit;

namespace Mesen.HeadlessTests;

//#1112: the Menu tick row and the sheet's height read ONE availability value
//(ConfigViewModel.MenuTickAimable), refreshed when the pad in hand changes, so
//an open sheet never shows a 388 px sheet with no row or a 340 px one with four.
[NativeCoreFree("Drives the Controls view-model through injected pad and device sources and a swapped aimable seam; no config write reaches the native core.")]
[Collection(PlayerSettingsSeamsCollection.Name)]
public class MenuTickSheetSeamTests : System.IDisposable
{
	private readonly System.Func<bool> _original = PlayerSettingsEssentials.MenuTickAimable;

	public void Dispose() => PlayerSettingsEssentials.MenuTickAimable = _original;

	private static void AssertRowMatchesHeight(ConfigViewModel model, bool aimable)
	{
		Assert.NotNull(model.PlayerControls);
		Assert.Equal(aimable, model.PlayerControls!.MenuTickAvailable);
		Assert.Equal(aimable ? 388 : 340, model.PlayerSheetHeight);
	}

	[Theory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void Row_and_height_follow_the_pad_in_hand_while_the_sheet_stays_open(bool before, bool after)
	{
		bool aimable = before;
		PlayerSettingsEssentials.MenuTickAimable = () => aimable;
		ConfigViewModel model = new(ConfigWindowTab.Input, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 1);
		AssertRowMatchesHeight(model, before);

		aimable = after;
		model.RefreshMenuTickAimable();
		AssertRowMatchesHeight(model, after);
		model.Dispose();
	}

	[Fact]
	public void A_pad_in_hand_change_reaches_an_open_sheet_through_the_event()
	{
		bool aimable = false;
		PlayerSettingsEssentials.MenuTickAimable = () => aimable;
		ConfigViewModel model = new(ConfigWindowTab.Input, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 1);
		aimable = true;
		PlayerSettingsEssentials.RaiseMenuTickAimableChanged();
		Avalonia.Threading.Dispatcher.UIThread.RunJobs();
		AssertRowMatchesHeight(model, true);
		model.Dispose();
	}
}
