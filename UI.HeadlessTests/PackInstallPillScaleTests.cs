using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1124 (ADR-0269 Decisions 2 and 3): the pack-install pill is Play's toast, and
//the one exception ADR-0269 left open ("stays at 1.0 for now; scaling it is
//deferred to #1124"). It is a separate top-level Popup
//(ShouldUseOverlayLayer=False) outside the four chrome layers, so it cannot
//inherit the scale from any of them: its own content carries the layout
//transform, reading Interface size through the same Play-only converter.
//The factor itself is host-free (UI.Tests/Play/InterfaceSizeTests); this pins
//that the realized pill reads it, in Play only, without losing its host.
[Collection(NativeCoreCollection.Name)]
public class PackInstallPillScaleTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly InterfaceSize _size = ConfigManager.Config.Preferences.InterfaceSize;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.Preferences.InterfaceSize = _size;
	}

	private static (MainWindow Window, MainWindowViewModel Model) Show(Workspace workspace, InterfaceSize size)
	{
		ConfigManager.Config.Preferences.UiMode = WorkspaceShell.UiModeFor(workspace);
		ConfigManager.Config.Preferences.Workspace = workspace;
		ConfigManager.Config.Preferences.InterfaceSize = size;
		//The window's default size on a desktop; the headless host's own is smaller.
		MainWindow window = new() { Width = 1024, Height = 640 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		return (window, Assert.IsType<MainWindowViewModel>(window.DataContext));
	}

	private static Popup PillPopup(MainWindow window) => window.FindNamed<Popup>("PackInstallPillPopup");

	//The transform sits on the Popup's own content, so the popup keeps its
	//placement, its host and its hit-test behaviour whatever the factor is.
	private static LayoutTransformControl PillScale(Popup popup)
	{
		LayoutTransformControl? found = popup.Child as LayoutTransformControl
			?? popup.Child?.GetVisualDescendants().OfType<LayoutTransformControl>().FirstOrDefault();
		Assert.NotNull(found);
		return found!;
	}

	private static double FactorOf(Popup popup) => ((ScaleTransform)PillScale(popup).LayoutTransform!).Value.M11;

	[AvaloniaTheory]
	[InlineData(InterfaceSize.Standard, 1.0)]
	[InlineData(InterfaceSize.Large, 1.25)]
	[InlineData(InterfaceSize.ExtraLarge, 1.5)]
	public void The_install_pill_scales_with_interface_size_in_play(InterfaceSize size, double factor)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, size);
		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();

		Popup popup = PillPopup(window);
		Assert.True(popup.IsOpen, "no install pill while the pack installs (W-P9)");
		Assert.Equal(factor, FactorOf(popup));
		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
	}

	//A layout transform, not a render transform: the popup takes its size from
	//its content (it is its own native window on the desktop platforms), so the
	//pill has to be *measured* at the scaled size - a render-only scale with the
	//popup still sized at 1.0 would clip the bigger text.
	[AvaloniaFact]
	public void The_pill_is_measured_at_the_scaled_size()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		double atStandard = PillWidth(InterfaceSize.Standard);
		double atExtraLarge = PillWidth(InterfaceSize.ExtraLarge);

		Assert.True(atStandard > 0, "the pill measured as nothing");
		Assert.Equal(1.5, atExtraLarge / atStandard, 2);
	}

	private static double PillWidth(InterfaceSize size)
	{
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, size);
		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		double width = PillScale(PillPopup(window)).Bounds.Width;
		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
		return width;
	}

	//The pill is a Play surface - it never shows outside Play - and the factor
	//is Play's alone (ADR-0269 Decision 2), so its content runs at 1.0 there.
	[AvaloniaTheory]
	[InlineData(Workspace.Remaster)]
	[InlineData(Workspace.Share)]
	public void The_install_pill_stays_at_size_one_outside_play(Workspace workspace)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(workspace, InterfaceSize.ExtraLarge);
		Popup popup = PillPopup(window);
		//Opened by hand: the pill itself never shows outside Play, and the
		//transform its content would carry is the one measured here.
		popup.IsOpen = true;
		Dispatcher.UIThread.RunJobs();

		Assert.False(model.IsPackInstallPillShown, "the Play pill is shown outside Play");
		Assert.Equal(1.0, FactorOf(popup));
	}

	//ADR-0269 Decision 2, the reasons the pill is its own popup at all: it floats
	//above the native game picture, takes no click and no keyboard, and it is not
	//inside the chrome root - so the factor is applied once, not twice.
	[AvaloniaFact]
	public void The_scaled_pill_keeps_its_popup_host_and_its_hit_test_behaviour()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(Workspace.Play, InterfaceSize.ExtraLarge);
		model.OnPackInstallStarted("Contra 80s");
		Dispatcher.UIThread.RunJobs();

		Popup popup = PillPopup(window);
		Assert.False(popup.ShouldUseOverlayLayer, "the install pill would draw under the native game picture");
		Assert.False(popup.TakesFocusFromNativeControl, "the install pill takes the keyboard from the game");
		Border pill = Assert.IsAssignableFrom<Border>(PillScale(popup).Child);
		Assert.Equal("PackInstallPill", pill.Name);
		Assert.False(pill.IsHitTestVisible, "the install pill takes a click meant for the game");

		LayoutTransformControl chrome = window.FindNamed<LayoutTransformControl>("PlayChromeRoot");
		Assert.DoesNotContain(chrome, popup.GetVisualAncestors());
		Assert.Equal(1.5, ((ScaleTransform)chrome.LayoutTransform!).Value.M11);

		model.OnPackInstallFinished(installed: true, silent: false);
		Dispatcher.UIThread.RunJobs();
	}
}
