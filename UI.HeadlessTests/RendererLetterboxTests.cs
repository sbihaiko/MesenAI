using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Avalonia.Layout;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//P.7 "16:9 stretch", the item docs/validation/manual-validation-automation-plan.md
//still listed as "the on-window letterbox fit ... stays untested geometry".
//
//The rule is now host-free in UI/Logic/RendererViewportFit.cs and asserted by
//UI.Tests/Config/RendererViewportFitTests.cs. This is strictly the wiring half
//(ADR-0150 scope rule): that MainWindow's real layout pass runs that rule over
//the real RendererPanel bounds and sizes the real renderer control with the
//result - which is what "the picture is letterboxed inside the window" means.
//
//A capture of the emulator frame (Core/Shared/Video/FrameCapture.h) cannot
//answer this: the video filter's output buffer is the picture at its own base
//size, and the letterbox only exists in the host window's layout.
[Collection(NativeCoreCollection.Name)]
public class RendererLetterboxTests
{
	//Deliberately not 4:3 and not 16:9, so whatever aspect ratio the core
	//reports the panel cannot happen to match it and the fit must leave a band.
	private const int WindowWidth = 900;
	private const int WindowHeight = 400;

	private static (MainWindow Window, MainWindowViewModel Model, Panel Panel) ShowWindow()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Advanced;
		ConfigManager.Config.Video.FullscreenForceIntegerScale = false;

		MainWindow window = new();
		window.Width = WindowWidth;
		window.Height = WindowHeight;
		window.Show();
		Dispatcher.UIThread.RunJobs();

		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		return (window, model, window.GetControl<Panel>("RendererPanel"));
	}

	private static RendererViewport Expected(MainWindow window, Panel panel)
	{
		return RendererViewportFit.Fit(
			panel.Bounds.Width, panel.Bounds.Height,
			EmuApi.GetAspectRatio(), LayoutHelper.GetLayoutScale(window),
			false, EmuApi.GetBaseScreenSize().Height
		);
	}

	//The layout pass really consumes the shared rule. If RendererPanel_LayoutUpdated
	//ever grows its own copy of the geometry again, these two drift and this fails.
	[AvaloniaFact]
	public void The_renderer_is_sized_by_the_shared_fit_rule()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, MainWindowViewModel model, Panel panel) = ShowWindow();
		Assert.True(panel.Bounds.Width > 0 && panel.Bounds.Height > 0, "RendererPanel was never laid out");

		//One physical pixel of tolerance (OnePixel): the even-size snap is
		//deterministic, but a pixel is the resolution the contract is stated
		//in, and anything wider than that is a real divergence.
		RendererViewport expected = Expected(window, panel);
		double onePixel = OnePixel(window);
		Assert.InRange(window.Renderer.Width, expected.Width - onePixel, expected.Width + onePixel);
		Assert.InRange(window.Renderer.Height, expected.Height - onePixel, expected.Height + onePixel);
		Assert.InRange(model.RendererSize.Width, expected.RealWidth - 1.0, expected.RealWidth + 1.0);
		Assert.InRange(model.RendererSize.Height, expected.RealHeight - 1.0, expected.RealHeight + 1.0);
	}

	//Upstream 3924215's shader-seam rule, as it reaches the core: the size
	//handed to EmuApi.SetRendererSize is even on both axes, and the renderer
	//control sits on whole physical pixels at the window's render scaling.
	[AvaloniaFact]
	public void The_core_gets_an_even_size_on_whole_physical_pixels()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, MainWindowViewModel model, _) = ShowWindow();
		double dpi = LayoutHelper.GetLayoutScale(window);
		uint realWidth = (uint)model.RendererSize.Width;
		uint realHeight = (uint)model.RendererSize.Height;
		Assert.True(realWidth > 0 && realHeight > 0, $"the core was never given a size ({realWidth}x{realHeight})");
		Assert.True(realWidth % 2 == 0 && realHeight % 2 == 0, $"odd renderer size {realWidth}x{realHeight}");
		Assert.Equal(realWidth, window.Renderer.Width * dpi, 3);
		Assert.Equal(realHeight, window.Renderer.Height * dpi, 3);
	}

	private static double OnePixel(MainWindow window) => 1.0 / LayoutHelper.GetLayoutScale(window);

	//The check a human used to make by looking at the window: the picture is
	//contained in the panel and keeps the core's aspect ratio, rather than being
	//stretched to fill it. A regression that assigned the panel size straight to
	//the renderer would pass the test above and fail this one.
	[AvaloniaFact]
	public void The_picture_is_letterboxed_inside_the_panel_and_keeps_its_ratio()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, MainWindowViewModel model, Panel panel) = ShowWindow();
		double aspectRatio = EmuApi.GetAspectRatio();
		Assert.True(aspectRatio > 0, $"the core reported an unusable aspect ratio ({aspectRatio})");

		double width = window.Renderer.Width;
		double height = window.Renderer.Height;
		Assert.True(width > 0 && height > 0, $"the renderer was never sized ({width}x{height})");

		//No crop on either axis (the snap rounds down, so the only slack is
		//upstream's 1e-5 nudge; one physical pixel is the tolerance)...
		double onePixel = OnePixel(window);
		Assert.True(width <= panel.Bounds.Width + onePixel, $"renderer width {width} overflows the panel ({panel.Bounds.Width})");
		Assert.True(height <= panel.Bounds.Height + onePixel, $"renderer height {height} overflows the panel ({panel.Bounds.Height})");
		//...the ratio is the core's, not the window's, to within the one
		//physical pixel the even-size snap may cost the derived axis...
		double aspectError = RendererViewportFit.AspectErrorPixels((uint)model.RendererSize.Width, (uint)model.RendererSize.Height, aspectRatio);
		Assert.True(aspectError <= 1.0 + 1e-9, $"{model.RendererSize.Width}x{model.RendererSize.Height} is {aspectError} px off the core's ratio {aspectRatio}");
		//...and, since the window is deliberately not that shape, a band is
		//genuinely left over. Without this the two asserts above would also
		//hold for a renderer that simply filled the panel.
		double leftover = (panel.Bounds.Width - width) + (panel.Bounds.Height - height);
		Assert.True(leftover > 1, $"no letterbox/pillarbox band: {width}x{height} in {panel.Bounds.Width}x{panel.Bounds.Height}");
	}

	//FullscreenForceIntegerScale is a fullscreen/maximized-only rule, so the
	//window state is part of the wiring. In a Normal window it must be inert
	//even when the setting is on - i.e. the conjunction MainWindow resolves
	//before calling Fit is the right one.
	[AvaloniaFact]
	public void Force_integer_scale_does_not_apply_to_a_normal_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");

		(MainWindow window, _, Panel panel) = ShowWindow();
		double before = window.Renderer.Height;

		ConfigManager.Config.Video.FullscreenForceIntegerScale = true;
		Assert.Equal(WindowState.Normal, window.WindowState);
		window.GetControl<Panel>("RendererPanel").InvalidateArrange();
		Dispatcher.UIThread.RunJobs();

		double onePixel = OnePixel(window);
		Assert.InRange(window.Renderer.Height, before - onePixel, before + onePixel);
		double expected = Expected(window, panel).Height;
		Assert.InRange(window.Renderer.Height, expected - onePixel, expected + onePixel);

		ConfigManager.Config.Video.FullscreenForceIntegerScale = false;
	}
}
