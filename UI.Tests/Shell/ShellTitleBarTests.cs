using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//G.1 (PRD Part B §13.5.1 W-S1, user's choice 2026-10-02 "Integrar agora"):
	//on macOS the shell bar IS the title bar; Windows/Linux keep the in-window
	//strip under the system title bar.
	public class ShellTitleBarTests
	{
		[Fact]
		public void Only_macOS_extends_the_client_area_into_the_title_bar()
		{
			Assert.True(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: true));
			Assert.False(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: false));
		}

		//ADR-0250: Classic has no shell bar, so it keeps the plain title bar.
		[Fact]
		public void Classic_keeps_the_plain_title_bar()
		{
			Assert.False(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: true, Workspace.Classic));
			Assert.True(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: true, Workspace.Play));
			Assert.True(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: true, Workspace.Remaster));
			Assert.True(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: true, Workspace.Share));
			Assert.False(ShellTitleBar.ExtendsIntoTitleBar(isMacOS: false, Workspace.Play));
		}

		[Fact]
		public void Title_bar_height_is_the_W_S1_bar_height()
		{
			Assert.Equal(52, ShellTitleBar.Height);
		}

		[Fact]
		public void Extended_windowed_bar_leaves_room_for_the_traffic_lights()
		{
			Assert.True(ShellTitleBar.LeadingInset(extended: true, fullScreen: false) >= 70);
		}

		[Fact]
		public void Fullscreen_or_a_plain_strip_needs_no_traffic_light_inset()
		{
			Assert.Equal(0, ShellTitleBar.LeadingInset(extended: true, fullScreen: true));
			Assert.Equal(0, ShellTitleBar.LeadingInset(extended: false, fullScreen: false));
			Assert.Equal(0, ShellTitleBar.LeadingInset(extended: false, fullScreen: true));
		}
	}
}
