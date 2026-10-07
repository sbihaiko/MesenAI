using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//#1007, W-S2: Play's Fullscreen row prints its shortcut - ⌃⌘F on macOS,
	//the platform rule's Ctrl elsewhere (as the switcher's ⌘1 / Ctrl+1).
	public class WorkspaceMenuShortcutTests
	{
		[Fact]
		public void Fullscreen_shortcut_on_macOS_is_control_command_F()
		{
			Assert.Equal("⌃⌘F", WorkspaceMenu.FullscreenShortcut(true));
		}

		[Fact]
		public void Fullscreen_shortcut_elsewhere_is_Ctrl_F()
		{
			Assert.Equal("Ctrl+F", WorkspaceMenu.FullscreenShortcut(false));
		}
	}
}
