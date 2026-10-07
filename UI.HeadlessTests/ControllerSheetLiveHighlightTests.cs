using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//#952 (W-P15's picture, lit live on the W-P17 sheet): a synthetic pad goes
//through ApplyPad - the host-free half of the sheet's poll, split from the
//read so a test can hold the tester's list still - and the drawn key is read
//back as pixels, not only as a class. The sheet is hosted on its own with the
//reads switched off (IsPaused answers false, so the poll never reaches the
//host tester), but its constructor still wires EmuApi.IsPaused as the default
//before the override, so the class joins the serial native-core collection.
[Collection(NativeCoreCollection.Name)]
public class ControllerSheetLiveHighlightTests
{
	private static readonly Color PlayTint = Color.Parse("#007AFF");
	private static readonly Color PadButton = Color.Parse("#747479");

	private static (Window Window, ControllerSheetViewModel Sheet, GamepadTestItem Pad) ShowSheet()
	{
		ControllerSheetViewModel sheet = new() {
			IsPaused = () => false,
			Pause = () => { },
			CurrentConsole = () => ConsoleType.Nes,
			KeyName = k => "",
			PressedKeys = () => Array.Empty<ushort>(),
		};
		GamepadTestItem pad = new(0) { Name = "Wireless Controller", Backend = GamepadBackend.GameController };
		sheet.Tester.Gamepads.Add(pad);
		sheet.IsVisible = true;
		Window window = new() {
			Width = 1100, Height = 740,
			Content = new Panel { Classes = { "player" }, Background = new SolidColorBrush(Color.Parse("#0B1430")), Children = { new PlayerControllerSheetView { DataContext = sheet } } }
		};
		window.Show();
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
		return (window, sheet, pad);
	}

	private static void Press(ControllerSheetViewModel sheet, GamepadTestItem pad, SetupButton button, bool pressed)
	{
		pad.Buttons[ControllerLivePad.BitOf(button, GamepadBackend.GameController)!.Value].IsPressed = pressed;
		sheet.ApplyPad();
		Dispatcher.UIThread.RunJobs();
	}

	//A point inside the drawn key, clear of its centred label and its border.
	private static (int X, int Y) InsideKey(Window window, SetupButton button)
	{
		Border key = window.FindAll<Border>().Single(b => b.Name == "ControllerSheetKey" && b.DataContext is ControllerPadLight { Button: var k } && k == button);
		Point p = key.TranslatePoint(new Point(key.Bounds.Width * 0.25, key.Bounds.Height / 2), window)!.Value;
		return ((int)p.X, (int)p.Y);
	}

	[AvaloniaFact]
	public void A_synthetic_press_lights_the_matching_key_and_its_release_clears_it()
	{
		(Window window, ControllerSheetViewModel sheet, GamepadTestItem pad) = ShowSheet();
		try {
			(int ax, int ay) = InsideKey(window, SetupButton.A);
			(int bx, int by) = InsideKey(window, SetupButton.B);
			Bitmap rest = PlayerRender.Capture(window);
			PlayerRender.AssertPixel(PadButton, rest, ax, ay, 6);

			Press(sheet, pad, SetupButton.A, true);
			Bitmap held = PlayerRender.Capture(window);
			PlayerRender.Save(held, "W-P17-live-A");
			PlayerRender.AssertPixel(PlayTint, held, ax, ay, 6);
			//Only the pressed key lights: its neighbour keeps the pad's grey.
			PlayerRender.AssertPixel(PadButton, held, bx, by, 6);

			Press(sheet, pad, SetupButton.A, false);
			Bitmap released = PlayerRender.Capture(window);
			PlayerRender.AssertPixel(PadButton, released, ax, ay, 6);
		} finally {
			sheet.IsVisible = false;
			window.Close();
		}
	}
}
