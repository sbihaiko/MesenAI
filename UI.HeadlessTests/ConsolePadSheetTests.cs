using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//#940 (PRD §13.5.2 W-P15): the setup sheet draws the loaded game's console's own
//pad - the NES pad, the upright Game Boy with its screen, the Master System pad
//with 1/2 and no Select/Start - with one key per step, placed by
//ControllerPadLayout for that console, and the first step's key lit.
//
//Core-free: the sheet is opened through injected key names and console, and
//cancelled before it could write a mapping, so no native call is made.
public class ConsolePadSheetTests
{
	private static readonly Color PlayTint = Color.Parse("#007AFF");

	[AvaloniaTheory]
	[InlineData(ConsoleType.Nes, SetupConsole.Nes)]
	[InlineData(ConsoleType.Gameboy, SetupConsole.GameBoy)]
	[InlineData(ConsoleType.Sms, SetupConsole.MasterSystem)]
	public void The_sheet_draws_the_consoles_own_pad(ConsoleType type, SetupConsole console)
	{
		const int device = 7;
		ushort Key(int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);
		PlayControllerSetupViewModel setup = new() {
			KeyName = k => k == Key(9) ? "Pad8 Start" : "Pad8 But" + (k & 0xFF),
			DeviceName = d => d == device ? "Test Pad" : "",
			CurrentConsole = () => type,
			IsPaused = () => true,
			Pause = () => { },
			Resume = () => { },
		};
		PlayControllerSetupView view = new() { DataContext = setup };
		Window window = new() {
			Width = 1100, Height = 900,
			Content = new Panel { Classes = { "player" }, Background = new SolidColorBrush(Color.Parse("#0B1430")), Children = { view } }
		};
		window.Show();
		try {
			TimeSpan t = setup.Now;
			setup.Tick(new[] { Key(1) }, t);
			setup.Tick(Array.Empty<ushort>(), t += TimeSpan.FromMilliseconds(100));
			setup.Tick(new[] { Key(9) }, t += TimeSpan.FromMilliseconds(100));
			Dispatcher.UIThread.RunJobs();
			Assert.True(setup.IsVisible, "the sheet did not open on Start");

			PadBody body = ControllerPadLayout.BodyOf(console);
			Border pad = window.FindNamed<Border>("ControllerSetupPad");
			Assert.Equal(body.Width, pad.Bounds.Width, 0.5);
			Assert.Equal(body.Height, pad.Bounds.Height, 0.5);
			Assert.Equal(body.Screen != null, window.FindNamed<Border>("ControllerSetupScreen").IsVisible);

			//Only the console's own buttons, each where that console has it.
			SetupButton[] steps = ControllerSetupSteps.For(console).ToArray();
			Border[] keys = pad.FindAll<Border>().Where(b => b.Name == "ControllerSetupKey").ToArray();
			Assert.Equal(steps.Length, keys.Length);
			for(int i = 0; i < steps.Length; i++) {
				PadKey expected = ControllerPadLayout.Of(console, steps[i]);
				Point at = keys[i].TranslatePoint(new Point(0, 0), pad)!.Value;
				Assert.Equal((expected.Left, expected.Top, expected.Width, expected.Height), (at.X, at.Y, keys[i].Bounds.Width, keys[i].Bounds.Height));
			}
			Assert.Equal(PlayTint, PlayerRender.SolidColor(keys[0].Background));
			Assert.NotEqual(PlayTint, PlayerRender.SolidColor(keys[1].Background));
			PlayerRender.Save(PlayerRender.Capture(window), "W-P15-" + console);
		} finally {
			setup.Cancel();
			window.Close();
		}
	}
}
