using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0249 (W-S1) and ADR-0255: the shell status line's four port lamps. The rule
//is host-free (UI.Tests/Shell/PadPortLampsTests); this is the wiring gate - the
//strip draws four lamps in the real MainWindow in Player mode, the count the
//poll feeds lights them, and the frame is written as a PNG with two pads so a
//person can look at the strip without a pad plugged in.
[Collection(NativeCoreCollection.Name)]
public class PadPortLampsRenderTests : IDisposable
{
	//The PNG the coordinator asked to look at: a scratchpad outside the repo, so
	//the file is evidence to read, not a build artifact to commit.
	private const string EvidencePath = "/private/tmp/claude-503/-Users-bihaiko-VSCodeProjects-MesenCE/b37bbfac-925a-47f5-a113-0146191cb6b2/scratchpad/lamps.png";

	//PlayerTintShareBrush (the status dot's own live tint) and PlayerText3Brush
	//(its dim one): the strip reuses the status line's palette, it does not
	//invent one.
	private static readonly Color Lit = Color.Parse("#34C759");
	private static readonly Color Dim = Color.Parse("#A1A1A6");

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
	}

	//recentGames seeds W-P2's home (an empty .rgd per name is what the tile
	//reads); none leaves W-P1's first-run home, which is the one the PNG shows.
	private static (MainWindow Window, MainWindowViewModel Model) Show(params string[] recentGames)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		foreach(string stale in Directory.GetFiles(ConfigManager.RecentGamesFolder, "*.rgd")) {
			File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in recentGames) {
			string file = Path.Combine(ConfigManager.RecentGamesFolder, game + ".rgd");
			File.WriteAllText(file, "");
			File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}

		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus");
		model.RecentGames.Init(GameScreenMode.RecentGames);
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(30);
		while(!condition()) {
			if(DateTime.UtcNow > deadline) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	//The lamps the strip draws, in port order. Named elements inside a
	//DataTemplate live in the template's own name scope, so they are found by
	//walking the realized visual tree (VisualTreeHelper).
	private static Border[] Lamps(MainWindow window) =>
		window.FindAll<Border>().Where(b => b.Name == "ShellPadLampDot" && b.IsOnScreen()).ToArray();

	private static string[] Labels(MainWindow window) =>
		window.FindAll<TextBlock>().Where(t => t.Name == "ShellPadLampLabel" && t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();

	[AvaloniaFact]
	public void Two_connected_pads_light_two_of_the_four_port_lamps()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();

		//The strip exists and holds four lamps whatever the backend reports.
		Assert.True(window.FindNamed<StackPanel>("ShellPadStrip").IsOnScreen());
		Assert.Equal(4, Lamps(window).Length);

		//No pad: four dim lamps, the normal state of a machine with none.
		model.ConnectedGamepadCount = () => 0;
		model.RefreshPadLamps();
		Dispatcher.UIThread.RunJobs();
		Assert.All(Lamps(window), l => Assert.Equal(Dim, PlayerRender.SolidColor(l.Background)));

		//More pads than ports: still four lamps, never a fifth, and the strip says
		//so in the note beside them.
		model.ConnectedGamepadCount = () => 6;
		model.RefreshPadLamps();
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(4, Lamps(window).Length);
		Assert.All(Lamps(window), l => Assert.Equal(Lit, PlayerRender.SolidColor(l.Background)));
		TextBlock note = window.FindNamed<TextBlock>("ShellPadLampNote");
		Assert.True(note.IsVisible);
		Assert.Contains("beyond the four ports", note.Text ?? "");

		//Two pads, the state the PNG is written in: P1 and P2 lit, P3 and P4 dim,
		//and the note gone.
		model.ConnectedGamepadCount = () => 2;
		model.GamepadName = index => "Pad " + (index + 1) + " (test)";
		model.RefreshPadLamps();
		Dispatcher.UIThread.RunJobs();

		Border[] lamps = Lamps(window);
		Assert.Equal(4, lamps.Length);
		Assert.Equal(Lit, PlayerRender.SolidColor(lamps[0].Background));
		Assert.Equal(Lit, PlayerRender.SolidColor(lamps[1].Background));
		Assert.Equal(Dim, PlayerRender.SolidColor(lamps[2].Background));
		Assert.Equal(Dim, PlayerRender.SolidColor(lamps[3].Background));
		Assert.Equal(new[] { "P1", "P2", "P3", "P4" }, Labels(window));
		Assert.False(window.FindNamed<TextBlock>("ShellPadLampNote").IsVisible);

		Bitmap frame = PlayerRender.Capture(window);
		Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath)!);
		frame.Save(EvidencePath);
		Xunit.TestContext.Current.TestOutputHelper?.WriteLine("lamps png: " + EvidencePath);
		Console.WriteLine("lamps png: " + EvidencePath);
	}

	//Both Play home layouts, W-P1 and W-P2: the strip lives in the shell bar,
	//which is the same bar over either home, so it is there on both.
	[AvaloniaFact]
	public void The_strip_shows_on_the_home_with_recents_too()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show("Contra (USA)");

		model.ConnectedGamepadCount = () => 2;
		model.RefreshPadLamps();
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<StackPanel>("ShellPadStrip").IsOnScreen());
		Assert.Equal(2, Lamps(window).Count(l => PlayerRender.SolidColor(l.Background) == Lit));
	}

	//#925: the same poll that lights the strip sends each pad's light in its
	//port's colour. The rule (which port, which colour, when to re-send) is
	//host-free (UI.Tests/Shell/PadLightsTests); this pins the wiring - the poll
	//reads the ports and the pads and hands the plan to the core's light call,
	//once, not on every tick.
	[AvaloniaFact]
	public void The_poll_sends_each_pad_its_port_colour_once()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show();

		ushort[] none = Array.Empty<ushort>();
		SheetPort p1 = new("Port1", "P1", 0, new[] { none, none, none, none }, new[] { none, none, none, none });
		SheetPort p2 = new("Port2", "P2", 1, new[] { new ushort[] { 0x1001 }, none, none, none }, new[] { none, none, none, none });
		System.Collections.Generic.List<PadLight> sent = new();
		model.ConnectedGamepadCount = () => 1;
		model.GamepadBlock = index => 0x1000 + (int)index * 0x100;
		model.PadLightPorts = () => new[] { p1, p2 };
		model.SendPadLight = sent.Add;

		model.RefreshPadLamps();
		model.RefreshPadLamps();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new[] { new PadLight(0, 0x1000, new PlayerColor(0xFF, 0x3B, 0x30)) }, sent);
		Assert.Equal(1, Lamps(window).Count(l => PlayerRender.SolidColor(l.Background) == Lit));
	}
}
