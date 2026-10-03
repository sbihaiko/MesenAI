using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//G.1 (PRD Part B §13.2, §13.8 Q4): ShowClassicMenuBar defaults to false
	//everywhere; an upgraded install gets the "your menus are under Tools ⋯"
	//toast once, a fresh install never needs it.
	public class ClassicMenuNoticeTests
	{
		[Fact]
		public void Fresh_install_starts_with_the_notice_already_done()
		{
			Assert.True(ClassicMenuNotice.ShownForMissingKey(settingsFileExists: false));
		}

		[Fact]
		public void Upgraded_install_still_owes_the_notice()
		{
			Assert.False(ClassicMenuNotice.ShownForMissingKey(settingsFileExists: true));
		}

		[Fact]
		public void Upgrade_shows_it_once_then_never_again()
		{
			bool shown = ClassicMenuNotice.ShownForMissingKey(settingsFileExists: true);
			Assert.True(ClassicMenuNotice.ShouldShow(shown, showClassicMenuBar: false));
			shown = true; //what the caller persists after showing it
			Assert.False(ClassicMenuNotice.ShouldShow(shown, showClassicMenuBar: false));
		}

		[Fact]
		public void Nothing_to_tell_when_the_classic_bar_is_on()
		{
			Assert.False(ClassicMenuNotice.ShouldShow(alreadyShown: false, showClassicMenuBar: true));
		}

		[Fact]
		public void Classic_bar_default_is_off()
		{
			Assert.False(ClassicMenuNotice.DefaultShowClassicMenuBar);
		}
	}
}
