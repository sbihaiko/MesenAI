using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//W-R0 › Recent projects (PRD Part B §13.5.3, docs/media/gui-redesign/W-R0.png,
//also behind W-R0b): the real RemasterWorkspaceView in a `player remaster`
//scope at the renders' content size, over a core-free model fed the render's
//fixture - Contra running with no project, Castlevania (3 recordings, a
//recent game's sibling folder) and The Legend of Zelda (1 recording, a folder
//Remaster showed before). Writes W-R0-recent.png / W-R0b-recent.png.
[Collection(NativeCoreCollection.Name)]
public class RemasterRecentProjectsRenderTests : IDisposable
{
	private const double ContentWidth = 1100;
	private const double ContentHeight = 660;
	private static readonly Color Text = Color.Parse("#1D1D1F");
	private static readonly Color Text2 = Color.Parse("#6E6E73");
	private static readonly Color Card = Colors.White;
	private static readonly Color RemasterTint = Color.Parse("#AF52DE");
	private static readonly RemasterFeasibility Ready = new(PythonGate.Found, "/usr/bin/python3", Array.Empty<string>(), "3.12.1", ToolsGate.Found, "/tools");

	private readonly string _root = Path.Combine(Path.GetTempPath(), "mesen-recent-r0-" + Guid.NewGuid().ToString("N"));

	public RemasterRecentProjectsRenderTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try {
			Directory.Delete(_root, true);
		} catch(IOException) {
		} catch(UnauthorizedAccessException) {
		}
	}

	private string Project(string relative, int recordings)
	{
		string folder = Path.Combine(_root, relative);
		Directory.CreateDirectory(Path.Combine(folder, "auto"));
		for(int i = 1; i <= recordings; i++) {
			string tex = Path.Combine(folder, "auto", RemasterProjectReader.FormatId(i), "textures");
			Directory.CreateDirectory(tex);
			File.WriteAllText(Path.Combine(tex, "hires.txt"), "<ver>107\n");
		}
		return folder;
	}

	private sealed class NoLauncher : IJobProcessLauncher
	{
		public IJobProcess Start(IReadOnlyList<string> argv, string workingDirectory, Action<string, bool> onLine, Action<int> onExit)
		{
			throw new XunitException("no job runs on W-R0");
		}
	}

	//Contra running, no project of its own: W-R0.
	private RemasterWorkspaceViewModel Model(RemasterFeasibility feasibility, RemasterConfig config, IReadOnlyList<string> recentRoms)
	{
		RemasterWorkspaceViewModel model = new(config, _ => feasibility, new NoLauncher(), hasHeadlessRecorder: false) {
			RecentRomPaths = () => recentRoms,
			RecentPacksFolder = () => Path.Combine(_root, "EnhancementPacks"),
		};
		string roms = Path.Combine(_root, "roms");
		model.UpdateGame(true, ConsoleType.Nes, "Contra (USA)", Path.Combine(roms, "Contra (USA).nes"), Path.Combine(roms, "Contra (USA)"), Path.Combine(_root, "EnhancementPacks"));
		model.EnsureFeasibilityMeasured();
		WaitFor(() => model.Feasibility != null, "the gate was never measured");
		Dispatcher.UIThread.RunJobs();
		return model;
	}

	//The render's two rows, in its order: Castlevania then Zelda.
	private (RemasterConfig Config, IReadOnlyList<string> Roms, string Castlevania, string Zelda) RenderFixture()
	{
		string castlevania = Project("roms/Castlevania (USA)", 3);
		string zelda = Project("art/The Legend of Zelda (USA)", 1);
		//Castlevania is both shown before and a recent game: listed once.
		RemasterConfig config = new() { RecentProjects = new List<string> { castlevania, zelda } };
		return (config, new List<string> { Path.Combine(_root, "roms", "Castlevania (USA).nes") }, castlevania, zelda);
	}

	private static (Window Window, RemasterWorkspaceView View) Host(RemasterWorkspaceViewModel model)
	{
		RemasterWorkspaceView view = new() { DataContext = model };
		Panel host = new() { Classes = { "player", "remaster" }, Children = { view } };
		Window window = new() { Content = host, Width = ContentWidth, Height = ContentHeight };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, view);
	}

	private static void WaitFor(Func<bool> condition, string failure, int timeoutMs = 30000)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > timeoutMs) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(20);
		}
		Dispatcher.UIThread.RunJobs();
	}

	private static void AssertText(TextBlock text, double size, FontWeight weight, Color color)
	{
		Assert.Equal("Inter", text.FontFamily.Name);
		Assert.Equal(size, text.FontSize);
		Assert.Equal(weight, text.FontWeight);
		Assert.Equal(color, PlayerRender.SolidColor(text.Foreground));
	}

	private static List<Button> Rows(Visual view) => view.FindNamed<ItemsControl>("RemasterRecentList").FindAll<Button>().Where(b => b.Classes.Contains("row")).ToList();

	//W-R0's "2 controls at rest": rows are list rows, not controls (W-R0: "2 + rows").
	private static int ControlsAtRest(Visual root) => root.FindAll<Button>()
		.Count(b => b.IsOnScreen() && b is not ToggleButton && b.FindAncestorOfType<RemasterRecentProjectsView>() == null && b.FindAncestorOfType<RemasterTileBrowserView>() == null);

	private static bool SeparatorShown(Button row) => row.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Separator").IsEffectivelyVisible;

	//The heading (15 semibold), one white group (radius 12), 52 px rows with
	//the 26 px purple brush badge, the game's name (13.5 medium) and "N
	//recordings" (13, secondary), a hairline between rows only.
	[AvaloniaFact]
	public void W_R0_recent_projects_render_as_the_wireframe()
	{
		var fixture = RenderFixture();
		RemasterWorkspaceViewModel model = Model(Ready, fixture.Config, fixture.Roms);
		(Window window, RemasterWorkspaceView view) = Host(model);

		Assert.True(view.FindNamed<StackPanel>("RemasterNoProject").IsOnScreen());
		Assert.True(view.FindNamed<RemasterRecentProjectsView>("RemasterRecentProjects").IsOnScreen());
		TextBlock header = view.FindNamed<TextBlock>("RemasterRecentHeader");
		Assert.Equal("Recent projects", header.Text);
		AssertText(header, 15, FontWeight.SemiBold, Text);
		Border group = view.FindNamed<Border>("RemasterRecentGroup");
		Assert.Equal(Card, PlayerRender.SolidColor(group.Background));
		Assert.Equal(new CornerRadius(12), group.CornerRadius);

		List<Button> rows = Rows(view);
		Assert.Equal(2, rows.Count);
		string[] titles = { "Castlevania (USA)", "The Legend of Zelda (USA)" };
		string[] details = { "3 recordings", "1 recording" };
		for(int i = 0; i < rows.Count; i++) {
			Assert.Equal(52, rows[i].Bounds.Height, 0.5);
			Border badge = rows[i].FindAll<Border>().Single(b => b.Classes.Contains("badge"));
			Assert.Equal(26, badge.Bounds.Width, 0.5);
			Assert.Equal(RemasterTint, PlayerRender.SolidColor(badge.Background));
			TextBlock title = rows[i].FindAll<TextBlock>().Single(t => t.Classes.Contains("title"));
			Assert.Equal(titles[i], title.Text);
			AssertText(title, 13.5, FontWeight.Medium, Text);
			TextBlock value = rows[i].FindAll<TextBlock>().Single(t => t.Classes.Contains("value"));
			Assert.Equal(details[i], value.Text);
			AssertText(value, 13, FontWeight.Normal, Text2);
			Assert.True(rows[i].FindAll<PathIcon>().Single(p => p.Classes.Contains("chevron")).IsOnScreen());
		}
		Assert.True(SeparatorShown(rows[0]));
		Assert.False(SeparatorShown(rows[1]));
		Assert.Equal(2, ControlsAtRest(view));

		Bitmap frame = PlayerRender.Capture(window);
		PlayerRender.Save(frame, "W-R0-recent");
		Point badgeCenter = rows[0].FindAll<Border>().Single(b => b.Classes.Contains("badge")).TranslatePoint(new Point(3, 13), window)!.Value;
		PlayerRender.AssertPixel(RemasterTint, frame, (int)badgeCenter.X, (int)badgeCenter.Y);
	}

	//W-R0b: the banner screen keeps the list under the two cards.
	[AvaloniaFact]
	public void W_R0b_banner_screen_keeps_the_recent_projects()
	{
		var fixture = RenderFixture();
		RemasterWorkspaceViewModel model = Model(Ready with { Python = PythonGate.Missing, PythonExecutable = "" }, fixture.Config, fixture.Roms);
		(Window window, RemasterWorkspaceView view) = Host(model);

		Assert.True(view.FindNamed<Border>("RemasterFeasibilityBanner").IsOnScreen());
		Assert.Equal(2, Rows(view).Count);
		Assert.Equal(4, ControlsAtRest(view));
		PlayerRender.Save(PlayerRender.Capture(window), "W-R0b-recent");
	}

	//A row opens its project exactly like Choose Folder…: W-R1 of that folder.
	[AvaloniaFact]
	public void A_row_opens_its_project_and_the_project_moves_to_the_front()
	{
		var fixture = RenderFixture();
		RemasterWorkspaceViewModel model = Model(Ready, fixture.Config, fixture.Roms);
		(_, RemasterWorkspaceView view) = Host(model);

		Button zelda = Rows(view).Single(r => r.FindAll<TextBlock>().Any(t => t.Text == "The Legend of Zelda (USA)"));
		zelda.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.True(model.IsProject);
		Assert.Equal(fixture.Zelda, model.ProjectFolder);
		Assert.Equal("The Legend of Zelda (USA)", model.ProjectName);
		Assert.False(view.FindNamed<RemasterRecentProjectsView>("RemasterRecentProjects").IsEffectivelyVisible);
		Assert.Equal(new[] { fixture.Zelda, fixture.Castlevania }, fixture.Config.RecentProjects);
	}

	//Nothing to list (or only folders that are gone): the heading and group
	//are hidden and W-R0 is unchanged.
	[AvaloniaFact]
	public void An_empty_list_hides_the_section()
	{
		string gone = Project("art/Gone (USA)", 1);
		Directory.Delete(gone, true);
		RemasterConfig config = new() { RecentProjects = new List<string> { gone } };
		RemasterWorkspaceViewModel model = Model(Ready, config, new List<string> { Path.Combine(_root, "roms", "Metroid (USA).nes") });
		(_, RemasterWorkspaceView view) = Host(model);

		Assert.False(model.HasRecentProjects);
		Assert.False(view.FindNamed<RemasterRecentProjectsView>("RemasterRecentProjects").IsEffectivelyVisible);
		Assert.Equal(2, ControlsAtRest(view));
	}
}
