using Mesen.Logic;
using Mesen.Utilities;
using Xunit;

namespace Mesen.HeadlessTests;

//#987: DropRoute is host-free (dual-compiled into UI.Tests), so it cannot name
//FileDialogHelper's extension constants. This cross-check, on the UI side,
//keeps a dropped file and the open dialogs agreeing on what a save state and
//a movie are.
public class DropRouteExtensionsTests
{
	private static readonly byte[] RomHeader = { (byte)'N', (byte)'E', (byte)'S', 0x1A, 0x02 };

	[Theory]
	[InlineData(FileDialogHelper.MesenSaveStateExt)]
	public void A_save_state_the_dialogs_offer_loads_as_a_state_when_dropped(string ext)
	{
		Assert.Equal(DropAction.LoadState, DropRoute.Decide("/states/Contra." + ext, true, RomHeader, false));
	}

	[Theory]
	[InlineData(FileDialogHelper.MesenMovieExt)]
	[InlineData(FileDialogHelper.BizHawkMovieExt)]
	[InlineData(FileDialogHelper.GbaHawkMovieExt)]
	public void A_movie_the_dialogs_offer_plays_when_dropped_on_a_running_game(string ext)
	{
		Assert.Equal(DropAction.PlayMovie, DropRoute.Decide("/movies/Contra." + ext, true, RomHeader, true));
	}
}
