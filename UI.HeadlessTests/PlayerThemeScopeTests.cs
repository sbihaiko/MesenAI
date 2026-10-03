using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249 Decision 3: UI/Styles/PlayerTheme.axaml applies only under the
//`player` style class. A classic window's Button keeps MesenStyles' corner
//radius 0 and MesenFont; a primary button inside the scope gets the theme's
//radius, Inter and the workspace tint. Core-free: plain windows only (the
//MainWindow side of the boundary is in PlayerThemeRenderTests).
public class PlayerThemeScopeTests
{
	private static readonly Color PlayTint = Color.Parse("#007AFF");

	private static TextBlock LabelOf(Button button) => button.FindAll<TextBlock>().First();

	[AvaloniaFact]
	public void A_classic_window_button_keeps_the_classic_style()
	{
		Button button = new() { Content = "OK" };
		Window window = new() { Content = new StackPanel { Children = { button } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new CornerRadius(0), button.CornerRadius);
		FontFamily classic = Assert.IsType<FontFamily>(Application.Current!.FindResource("MesenFont"));
		Assert.Equal(classic.Name, LabelOf(button).FontFamily.Name);
		Assert.NotEqual("Inter", LabelOf(button).FontFamily.Name);
		window.Close();
	}

	[AvaloniaFact]
	public void A_classed_button_outside_the_scope_is_not_themed()
	{
		//The component classes alone do nothing: only `.player` turns them on.
		Button button = new() { Content = "OK", Classes = { "primary" } };
		Window window = new() { Content = new StackPanel { Children = { button } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new CornerRadius(0), button.CornerRadius);
		Assert.NotEqual("Inter", LabelOf(button).FontFamily.Name);
		window.Close();
	}

	[AvaloniaFact]
	public void A_primary_button_inside_the_scope_has_the_theme_radius_font_and_tint()
	{
		Button button = new() { Content = "Open a ROM…", Classes = { "primary", "large" } };
		StackPanel scope = new() { Classes = { "player", "play" }, Children = { button } };
		Window window = new() { Content = scope };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new CornerRadius(11), button.CornerRadius);
		Assert.Equal(44, button.Height);
		TextBlock label = LabelOf(button);
		Assert.Equal("Inter", label.FontFamily.Name);
		Assert.Equal(16, label.FontSize);
		Assert.Equal(PlayTint, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
		Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
		window.Close();
	}

	[AvaloniaFact]
	public void The_tint_follows_the_workspace_class()
	{
		Button remaster = new() { Content = "Build", Classes = { "primary" } };
		Button share = new() { Content = "Send", Classes = { "primary" } };
		Window window = new() {
			Content = new StackPanel {
				Classes = { "player" },
				Children = {
					new Border { Classes = { "remaster" }, Child = remaster },
					new Border { Classes = { "share" }, Child = share },
				}
			}
		};
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(Color.Parse("#AF52DE"), Assert.IsAssignableFrom<ISolidColorBrush>(remaster.Background).Color);
		Assert.Equal(Color.Parse("#34C759"), Assert.IsAssignableFrom<ISolidColorBrush>(share.Background).Color);
		Assert.Equal(new CornerRadius(8), share.CornerRadius);
		window.Close();
	}

	//#716: a sheet sets its own text colour from the tokens (TEXT on CARD), so
	//no screen restyled onto it renders black text on a dark sheet.
	[AvaloniaFact]
	public void A_sheet_sets_dark_text_on_its_light_card()
	{
		TextBlock text = new() { Text = "Save states" };
		Border sheet = new() { Classes = { "sheet" }, Child = text };
		Window window = new() { Content = new Panel { Classes = { "player" }, Children = { sheet } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Color background = Assert.IsAssignableFrom<ISolidColorBrush>(sheet.Background).Color;
		Color foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
		Assert.True(PlayerRender.Contrast(foreground, background) >= 4.5, $"contrast {PlayerRender.Contrast(foreground, background):0.0} of {foreground} on {background}");
		Assert.Equal(new CornerRadius(14), sheet.CornerRadius);
		window.Close();
	}
}
