using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249: the W-R restyle is Player-only. Advanced hosts the same views
//without the `player` class and keeps the text it showed before: the
//one-line banner, the ⚠-prefixed build title with caption-led rows, and the
//glyph badges and popover lines.
public partial class RemasterThemeRenderTests
{
	[AvaloniaFact]
	public void Advanced_keeps_the_one_line_feasibility_banner()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready with { Python = PythonGate.Missing, PythonExecutable = "" }, ContraProject(), projectOpen: false);
		(_, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model, player: false);

		TextBlock line = view.FindNamed<TextBlock>("RemasterFeasibilityLine");
		Assert.True(line.IsOnScreen());
		Assert.Equal("Painting needs Python 3.10 or newer, which MesenAI could not find. You can still record. Your figures are prepared once Python is available.", line.Text);
		Assert.False(view.FindNamed<TextBlock>("RemasterFeasibilityText").IsOnScreen());
		Assert.False(view.FindNamed<TextBlock>("RemasterFeasibilityDetail").IsOnScreen());
	}

	[AvaloniaFact]
	public void Player_splits_the_feasibility_banner_in_two_lines()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready with { Python = PythonGate.Missing, PythonExecutable = "" }, ContraProject(), projectOpen: false);
		(_, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model);

		Assert.False(view.FindNamed<TextBlock>("RemasterFeasibilityLine").IsOnScreen());
		Assert.True(view.FindNamed<TextBlock>("RemasterFeasibilityText").IsOnScreen());
		Assert.True(view.FindNamed<TextBlock>("RemasterFeasibilityDetail").IsOnScreen());
	}

	[AvaloniaFact]
	public void Advanced_keeps_the_glyph_title_and_caption_rows_of_a_failed_build()
	{
		(RemasterWorkspaceViewModel model, FakeLauncher launcher) = Model(Ready, ContraProject(), projectOpen: true);
		(_, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model, player: false);
		Click(view.FindNamed<Button>("RemasterBuildButton"));
		launcher.Last!.OnLine("steps: 4", false);
		launcher.Last.OnLine("recording: rec-001", false);
		launcher.Last.OnLine("== figures", false);
		launcher.Last.OnLine("error: usr000-figure.png: 384x128 is not a whole multiple of the 192x64 twin — the canvas was resized; repaint at the size you were given", true);
		launcher.Last.OnLine("FAIL figures", false);
		launcher.Last.OnExit(1);
		WaitFor(() => !model.IsJobRunning, "the failed build never ended");

		TextBlock title = view.FindNamed<TextBlock>("RemasterBuildProblemsClassicTitle");
		Assert.True(title.IsOnScreen());
		Assert.Equal("⚠ 1 problem stopped the build.", title.Text);
		Assert.False(view.FindNamed<TextBlock>("RemasterBuildProblemsTitle").IsOnScreen());
		Assert.False(view.FindNamed<PathIcon>("RemasterBuildProblemsIcon").IsOnScreen());
		ItemsControl list = view.FindNamed<ItemsControl>("RemasterBuildProblemList");
		string[] shown = list.FindAll<TextBlock>().Where(t => t.IsOnScreen()).Select(t => t.Text ?? "").ToArray();
		Assert.Contains("· \"run\" — the canvas was resized (was 384×128). Undo the resize and save again.", shown);
		Assert.DoesNotContain(list.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Classes.Contains("problem-name") || t.Classes.Contains("problem-text")));
	}

	[AvaloniaFact]
	public void Advanced_keeps_the_glyph_badges_and_popover_lines()
	{
		(RemasterWorkspaceViewModel model, _) = Model(Ready, ContraProject(), projectOpen: true);
		(_, RemasterWorkspaceView view) = Host(new RemasterWorkspaceView(), model, player: false);
		Button tile = view.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().First(b => b.Classes.Contains("tile"));
		TextBlock badge = tile.FindAll<TextBlock>().Single(t => t.Classes.Contains("badges-classic"));
		Assert.True(badge.IsOnScreen());
		Assert.Equal("✎", badge.Text);
		Assert.DoesNotContain(tile.FindAll<TextBlock>(), t => t.IsOnScreen() && t.Text == "Painted");
		Assert.Contains("✔ Seen in the game", Avalonia.Controls.ToolTip.GetTip(tile) as string);

		Button details = view.FindNamed<ItemsControl>("RemasterTileList").FindAll<Button>().First(b => b.Classes.Contains("details"));
		Flyout flyout = Assert.IsType<Flyout>(details.Flyout);
		flyout.ShowAt(details);
		Dispatcher.UIThread.RunJobs();
		StackPanel content = Assert.IsType<StackPanel>(flyout.Content);
		WaitFor(() => content.FindAncestorOfType<FlyoutPresenter>() != null, "the popover never opened");
		TextBlock header = content.FindAll<TextBlock>().Single(t => t.Classes.Contains("header-line"));
		Assert.True(header.IsOnScreen());
		Assert.Equal("\"run\" · 6 cells · from recording 1", header.Text);
		Assert.DoesNotContain(content.FindAll<TextBlock>(), t => t.IsOnScreen() && (t.Classes.Contains("header") || t.Classes.Contains("header-detail")));
		string[] lines = content.FindAll<TextBlock>().Where(t => t.IsOnScreen() && t.Classes.Contains("line-classic")).Select(t => t.Text ?? "").ToArray();
		Assert.Contains("✔ Seen in the game", lines);
		Assert.Contains("✎ Painted", lines);
		Assert.DoesNotContain(content.FindAll<PathIcon>(), p => p.Classes.Contains("line-icon") && p.IsOnScreen());
		flyout.Hide();
	}
}
