using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Windows;
using System;

namespace Mesen;

public class MesenWindow : Window
{
	static MesenWindow()
	{
		PopupRoot.ClientSizeProperty.Changed.AddClassHandler<PopupRoot>((s, e) => {
			foreach(var v in s.GetVisualChildren()) {
				SetTextRenderingMode(v);
			}
		});
	}

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Focusable = true;
		SetTextRenderingMode(this);
		//#1255: while a GUI test hook runs, this window is shown without activating
		//(ShowActivated is read when the platform window comes up, so it is set
		//here, before anything shows it). Inert without --test-hook.
		TestHookWiring.WindowCreated(this);
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		//#1255: a window that has just opened is kept inside the primary display's
		//working area and reported to the run. Inert without --test-hook.
		TestHookWiring.WindowOpened(this);
	}

	private static void SetTextRenderingMode(Visual v)
	{
		switch(ConfigManager.Config.Preferences.FontAntialiasing) {
			case FontAntialiasing.Disabled:
				TextOptions.SetTextRenderingMode(v, TextRenderingMode.Alias);
				break;

			case FontAntialiasing.Antialias:
				TextOptions.SetTextRenderingMode(v, TextRenderingMode.Antialias);
				break;

			default:
			case FontAntialiasing.SubPixelAntialias:
				TextOptions.SetTextRenderingMode(v, TextRenderingMode.SubpixelAntialias);
				break;
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);

		if(DataContext is IDisposable disposable) {
			disposable.Dispose();
		}

		//This fixes (or just dramatically reduces?) a memory leak
		//Most windows don't get GCed properly if this isn't done, leading to large memory leaks
		DataContext = null;
	}
}
