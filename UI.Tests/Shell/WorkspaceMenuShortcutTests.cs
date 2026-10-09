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

		private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
		private const ShortcutModifiers CtrlMeta = ShortcutModifiers.Control | ShortcutModifiers.Meta;

		//The printed text and the handled keys come from one rule.
		[Fact]
		public void Control_meta_F_toggles_fullscreen_on_macOS()
		{
			Assert.True(WorkspaceMenu.IsFullscreenShortcut("F", CtrlMeta, true));
		}

		[Fact]
		public void Ctrl_F_toggles_fullscreen_elsewhere()
		{
			Assert.True(WorkspaceMenu.IsFullscreenShortcut("F", Ctrl, false));
		}

		[Fact]
		public void Ctrl_F_does_not_toggle_on_macOS()
		{
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("F", Ctrl, true));
		}

		[Fact]
		public void Control_meta_F_does_not_toggle_elsewhere()
		{
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("F", CtrlMeta, false));
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void Extra_or_missing_modifiers_do_not_toggle(bool isMacOS)
		{
			ShortcutModifiers exact = isMacOS ? CtrlMeta : Ctrl;
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("F", exact | ShortcutModifiers.Alt, isMacOS));
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("F", exact | ShortcutModifiers.Shift, isMacOS));
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("F", ShortcutModifiers.None, isMacOS));
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void A_wrong_key_does_not_toggle(bool isMacOS)
		{
			Assert.False(WorkspaceMenu.IsFullscreenShortcut("G", isMacOS ? CtrlMeta : Ctrl, isMacOS));
		}

		//#1007 ruling (a): the footer hint is Play's alone.
		[Theory]
		[InlineData(Workspace.Play, true)]
		[InlineData(Workspace.Remaster, false)]
		[InlineData(Workspace.Share, false)]
		[InlineData(Workspace.Classic, false)]
		public void Only_Play_shows_the_tools_hint(Workspace door, bool expected)
		{
			Assert.Equal(expected, WorkspaceMenu.HasToolsHint(door));
		}
	}
}
