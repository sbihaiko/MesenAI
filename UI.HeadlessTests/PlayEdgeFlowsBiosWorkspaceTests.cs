using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249 final audit, group "no classic": the BIOS prompt is W-P13 in
//Player mode whatever the workspace. A load started from Remaster or Share
//(a project's ROM, a replay) used to fall through to the classic
//FirmwareHelper message box; the sheet now asks over the workspace that is
//showing, without switching to Play.
public partial class PlayEdgeFlowsTests
{
	[AvaloniaFact]
	public void A_bios_request_from_remaster_asks_with_the_player_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();

		using CoreBiosRequest request = new(window);
		WaitFor(() => model.BiosSheet.IsVisible, "a request raised from Remaster did not show the BIOS sheet");

		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
		Assert.Empty(window.OwnedWindows);
		Panel host = window.FindNamed<Panel>("BiosSheetBackdrop");
		Assert.True(host.IsOnScreen(), "the BIOS sheet is not on screen over Remaster");
		Assert.Equal("Drop disksys.rom here", window.FindNamed<TextBlock>("BiosSheetDropTitle").Text);
		Assert.Equal("Inter", window.FindNamed<TextBlock>("BiosSheetDropTitle").FontFamily.Name);
		PlayerRender.Save(PlayerRender.Capture(window), "W-P13-bios-from-remaster");

		//Esc cancels it there too.
		model.TogglePlayerOverlay();
		Assert.True(request.WaitAnswered(TimeSpan.FromSeconds(10)), "Esc left the Core's request waiting");
		Assert.False(model.BiosSheet.IsVisible);
		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
	}
}
