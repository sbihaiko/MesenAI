using System;

namespace Mesen.Logic;

//P.7 ("16:9 stretch"), the on-window half. `Core/Shared/Video/AspectRatioMath.h`
//answers "what ratio should the picture be drawn at"; this answers the question
//the ratio leaves open: given a panel of an arbitrary shape, how large is the
//picture inside it and how much of the panel is left over as letterbox
//(horizontal bands) or pillarbox (vertical bands).
//
//Stateful partner (ADR-0127): `UI/Windows/MainWindow.axaml.cs`, whose
//`RendererPanel_LayoutUpdated` owns the Avalonia layout pass, the panel bounds,
//`_rendererSize`, the DPI scale and the assignment to `_renderer`/the view
//model. Everything it used to compute inline lives here, so the fit can be
//asserted in `UI.Tests` without a window, a display or a native core.
//
//Host-free per ADR-0123: no Avalonia types (Avalonia's `Size` would drag the
//whole framework into the dual-compile), only doubles.
public readonly struct RendererViewport
{
	//Logical (DIP) size of the picture inside the panel - what the renderer
	//control is sized to. Always RealWidth/RealHeight over the DPI scale, so
	//the control lands on whole physical pixels.
	public double Width { get; init; }
	public double Height { get; init; }

	//Physical size handed to the core via EmuApi.SetRendererSize. Always even
	//on both axes (upstream 3924215: an odd size puts a seam down the middle
	//of a shaded frame) and never larger than the panel in physical pixels.
	public uint RealWidth { get; init; }
	public uint RealHeight { get; init; }

	//What is left over on each axis, in logical units, split evenly between the
	//two bands the picture is centred in. Pillarbox = vertical bands on the
	//left/right; letterbox = horizontal bands on the top/bottom. Exactly one of
	//the two is non-zero for a picture that does not match the panel's shape.
	public double PillarboxWidth { get; init; }
	public double LetterboxHeight { get; init; }
}

public static class RendererViewportFit
{
	//`availableWidth/Height` is the space the picture may occupy (the renderer
	//panel's bounds, or the size the window forced on it in fullscreen);
	//`aspectRatio` is EmuApi.GetAspectRatio(); `dpiScale` is the layout scale;
	//`baseHeight` is EmuApi.GetBaseScreenSize().Height, used only by the
	//integer-scale rule; `forceIntegerScale` is
	//Config.Video.FullscreenForceIntegerScale AND a maximized/fullscreen window
	//- the caller resolves that conjunction, since window state is its business.
	public static RendererViewport Fit(double availableWidth, double availableHeight, double aspectRatio, double dpiScale, bool forceIntegerScale, double baseHeight)
	{
		if(!IsUsable(aspectRatio) || !IsUsable(dpiScale) || !IsUsable(availableWidth) || !IsUsable(availableHeight)) {
			//Degenerate inputs (an unknown aspect-ratio setting returns 0.0 from
			//AspectRatioMath, and the panel has no bounds before its first
			//layout pass). Fill what we were given rather than propagating a
			//NaN/Infinity into the control's Width/Height.
			return Fill(availableWidth, availableHeight, IsUsable(dpiScale) ? dpiScale : 1.0);
		}

		//Fit by height first, then fall back to fitting by width when that
		//would crop horizontally. Rounded comparison, so a sub-pixel excess
		//does not flip the axis the fit is driven by.
		double height = availableHeight;
		double width = availableHeight * aspectRatio;
		bool heightBinds = true;
		if(Math.Round(width) > Math.Round(availableWidth)) {
			width = availableWidth;
			height = width / aspectRatio;
			heightBinds = false;
		}

		//The picture may overflow the panel only when the integer-scale rule
		//clamps to 1x on a panel shorter than one emulated screen.
		bool mayOverflow = false;
		if(forceIntegerScale && baseHeight > 0) {
			//Only ever shrinks: a fractional scale is floored to the next whole
			//one (never below 1), which is what makes every emulated pixel the
			//same size on screen. The picture keeps its aspect ratio, so the
			//floored height widens the letterbox rather than distorting it.
			double scale = height * dpiScale / baseHeight;
			if(scale != Math.Floor(scale)) {
				mayOverflow = Math.Floor(scale / dpiScale) < 1;
				height = baseHeight * Math.Max(1, Math.Floor(scale / dpiScale));
				width = height * aspectRatio;
				heightBinds = true;
			}
		}

		double limitWidth = mayOverflow ? double.PositiveInfinity : availableWidth * dpiScale;
		double limitHeight = mayOverflow ? double.PositiveInfinity : availableHeight * dpiScale;
		long startWidth = EvenFloor(Math.Min(width * dpiScale, limitWidth));
		long startHeight = EvenFloor(Math.Min(height * dpiScale, limitHeight));
		(uint realWidth, uint realHeight) = SnapEven(startWidth, startHeight, EvenFloor(limitWidth), EvenFloor(limitHeight), aspectRatio, heightBinds);
		return Snapped(realWidth, realHeight, dpiScale, availableWidth, availableHeight);
	}

	//Upstream 3924215's two rules, adapted so the picture still fits the panel.
	//Upstream rounds the physical size UP to even and derives the logical size
	//from it, which overflows an odd panel by a pixel. Here the binding axis is
	//the largest even pixel count not above the exact fit (`start*`: the fit
	//in physical pixels, clamped to the panel) and the other axis is the even
	//count nearest to binding * ratio (or / ratio), allowed up to the panel's
	//own even size (`max*`), so the aspect is off by at most one physical
	//pixel on the derived axis (AspectErrorPixels <= 1). When the derived axis
	//would pass the panel, the other axis binds instead; stepping the binding
	//axis down 2 px at a time is the last resort.
	private static (uint Width, uint Height) SnapEven(long startWidth, long startHeight, long maxWidth, long maxHeight, double aspectRatio, bool heightBinds)
	{
		(long Width, long Height)? size = TryBind(maxWidth, maxHeight, aspectRatio, heightBinds, heightBinds ? startHeight : startWidth)
			?? TryBind(maxWidth, maxHeight, aspectRatio, !heightBinds, heightBinds ? startWidth : startHeight);
		for(long binding = (heightBinds ? startHeight : startWidth) - 2; size == null && binding > 0; binding -= 2) {
			size = TryBind(maxWidth, maxHeight, aspectRatio, heightBinds, binding);
		}
		//Nothing fits only when a cap is under 2 px: draw nothing.
		(long width, long height) = size ?? (0, 0);
		return ((uint)width, (uint)height);
	}

	private static (long Width, long Height)? TryBind(long maxWidth, long maxHeight, double aspectRatio, bool heightBinds, long binding)
	{
		if(heightBinds) {
			long width = NearestEven(binding * aspectRatio);
			return width <= maxWidth ? (width, binding) : null;
		}
		long height = NearestEven(binding / aspectRatio);
		return height <= maxHeight ? (binding, height) : null;
	}

	private static RendererViewport Fill(double width, double height, double dpiScale)
	{
		double safeWidth = IsUsable(width) ? width : 0;
		double safeHeight = IsUsable(height) ? height : 0;
		return Snapped((uint)EvenFloor(safeWidth * dpiScale), (uint)EvenFloor(safeHeight * dpiScale), dpiScale, safeWidth, safeHeight);
	}

	private static RendererViewport Snapped(uint realWidth, uint realHeight, double dpiScale, double availableWidth, double availableHeight)
	{
		double width = ToLogical(realWidth, dpiScale);
		double height = ToLogical(realHeight, dpiScale);
		return new RendererViewport {
			Width = width,
			Height = height,
			RealWidth = realWidth,
			RealHeight = realHeight,
			PillarboxWidth = Math.Max(0, (availableWidth - width) / 2),
			LetterboxHeight = Math.Max(0, (availableHeight - height) / 2)
		};
	}

	//Upstream's exact expression: truncate to 8 decimals, then nudge up by
	//1e-5 so a quotient like 401.99999999 is not laid out one pixel short.
	private static double ToLogical(uint real, double dpiScale)
	{
		return real == 0 ? 0 : Math.Round(real / dpiScale, 8, MidpointRounding.ToZero) + 0.00001;
	}

	//1e-6 absorbs float noise such as 375 * 1.1 = 412.50000000000006 or
	//299.99999999999994 - a physical size is never meant to be that close to
	//the next whole pixel.
	//An infinite limit (the integer-scale 1x overflow) maps to "no limit".
	private static long EvenFloor(double value)
	{
		if(double.IsPositiveInfinity(value)) {
			return long.MaxValue;
		}
		return value > 0 ? (long)Math.Floor(value + 1e-6) & ~1L : 0;
	}

	private static long NearestEven(double value) => value > 0 ? 2 * (long)Math.Round(value / 2, MidpointRounding.AwayFromZero) : 0;

	//How far a physical size is from `aspectRatio`, in physical pixels on
	//whichever axis was derived from the other: min(|W - H*r|, |H - W/r|).
	//The P.7 aspect contract is that this is at most 1 for every viewport
	//`Fit` returns from a usable aspect ratio (the even-size snap may cost the
	//derived axis one pixel, never more).
	public static double AspectErrorPixels(uint realWidth, uint realHeight, double aspectRatio)
	{
		return Math.Min(Math.Abs(realWidth - realHeight * aspectRatio), Math.Abs(realHeight - realWidth / aspectRatio));
	}

	private static bool IsUsable(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
