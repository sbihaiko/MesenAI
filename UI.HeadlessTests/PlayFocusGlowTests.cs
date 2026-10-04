using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Mesen.HeadlessTests;

//The Play door is operable from a pad (ADR-0256), and a pad has no pointer: the
//focus ring is the only thing on screen saying where the player is. That makes
//it load-bearing rather than decorative, and these are the rules it has to keep -
//that it is a glow that carries at a distance, that every button answers focus
//with one, and that a list row glows *inward*, because a row is clipped by the
//group around it and an outward bloom would be cut off exactly where it is
//needed.
//
//Core-free: plain windows, no MainWindow.
public class PlayFocusGlowTests
{
	private static BoxShadows Ring => (BoxShadows)Application.Current!.FindResource("PlayerFocusRing")!;
	private static BoxShadows InsetRing => (BoxShadows)Application.Current!.FindResource("PlayerFocusGlowInset")!;

	//The layer the :focus-visible rules set the shadow on. Every Play button
	//template names it PART_Background.
	private static Border BackgroundOf(Control control)
		=> control.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Background");

	private static Window Show(Control content)
	{
		Window window = new() { Width = 480, Height = 300, Content = new StackPanel { Classes = { "player", "play" }, Children = { content } } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	//The decision, encoded: two layers or more, the innermost a crisp ring (no
	//blur, so it reads as an edge) and the rest blurred (so it reads as light).
	//A revert to the flat 3 px ring - the shape this replaced - fails here.
	[AvaloniaFact]
	public void The_focus_ring_is_a_glow_and_not_a_flat_ring()
	{
		Assert.True(Ring.Count >= 2, $"the focus ring is a single layer again: {Ring.Count}");
		Assert.Equal(0, Ring[0].Blur);
		Assert.True(Ring[0].Spread > 0, "the innermost layer is not a ring");
		Assert.Contains(Enumerable.Range(1, Ring.Count - 1), i => Ring[i].Blur > 0);
	}

	//A row sits inside Border.group with ClipToBounds, so its glow has to point
	//inward. An outward ring on a row is invisible - which is the failure this
	//asserts against, not a cosmetic preference.
	[AvaloniaFact]
	public void The_row_glow_is_inset_because_a_row_is_clipped()
	{
		Assert.True(InsetRing.Count > 0);
		Assert.All(Enumerable.Range(0, InsetRing.Count), i => Assert.True(InsetRing[i].IsInset, $"layer {i} of the row glow is not inset"));
	}

	[AvaloniaFact]
	public void A_focused_play_button_carries_the_glow()
	{
		Button button = new() { Content = "Resume", Classes = { "primary" } };
		Window window = Show(button);

		Border background = BackgroundOf(button);
		Assert.NotEqual(Ring, background.BoxShadow);

		//NavigationMethod.Tab is what makes this :focus-visible rather than plain
		//:focus - a pointer click focuses without glowing, which is the point of
		//the pseudo-class.
		button.Focus(NavigationMethod.Tab);
		Dispatcher.UIThread.RunJobs();

		Assert.True(button.IsFocused);
		Assert.Equal(Ring, background.BoxShadow);
		window.Close();
	}

	//The rule is one rule, not a list of the five button classes that happened to
	//have a focus rule when it was written: a button the pad can land on but that
	//shows nothing is a dead end the player cannot see they are in.
	[AvaloniaFact]
	public void Every_button_class_glows_not_only_the_five_that_had_a_rule()
	{
		//Every button class the theme actually defines. An invented class, or a
		//bare Button, would fail for the wrong reason - see
		//A_classless_button_is_not_play_themed below.
		Button[] buttons = {
			new() { Content = "a", Classes = { "primary" } },
			new() { Content = "b", Classes = { "secondary" } },
			new() { Content = "c", Classes = { "plain" } },
			new() { Content = "d", Classes = { "tinted" } },
			new() { Content = "e", Classes = { "destructive" } },
			new() { Content = "f", Classes = { "chip" } },
			new() { Content = "g", Classes = { "drop-zone" } }
		};
		Window window = Show(new StackPanel { Children = { buttons[0], buttons[1], buttons[2], buttons[3], buttons[4], buttons[5], buttons[6] } });

		foreach(Button button in buttons) {
			button.Focus(NavigationMethod.Tab);
			Dispatcher.UIThread.RunJobs();
			Assert.True(button.IsFocused);
			//Named, not First(): which class is missing the part is the whole
			//answer, and a bare "sequence contains no matching element" hides it.
			Border? background = button.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Background");
			Assert.True(background != null, $"Button.{string.Join('.', button.Classes)} has no Border#PART_Background for a focus rule to reach");
			Assert.Equal(Ring, background!.BoxShadow);
		}
		window.Close();
	}

	//The boundary of the rule above, asserted rather than assumed: a Button with
	//no class keeps the stock Avalonia template, which has no PART_Background, so
	//no focus rule can reach it however general the selector is. Nothing in the
	//Play views is classless today (every <Button> in Player*.axaml / Play*.axaml
	//carries a class), so this is a warning for the next one, not a live bug.
	[AvaloniaFact]
	public void A_classless_button_is_not_play_themed()
	{
		Button button = new() { Content = "?" };
		Window window = Show(button);

		Assert.Empty(button.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_Background"));
		window.Close();
	}

	//A row's glow comes from the inset resource, and the row is still a Border so
	//that it can carry one at all: the template used to be a Panel, which has no
	//BoxShadow, so rows could never glow however the styles were written.
	[AvaloniaFact]
	public void A_focused_row_glows_inward()
	{
		Button row = new() { Content = "Resume", Classes = { "row" } };
		Border group = new() { Classes = { "group" }, Child = new StackPanel { Children = { row } } };
		Window window = Show(group);

		row.Focus(NavigationMethod.Tab);
		Dispatcher.UIThread.RunJobs();

		Assert.True(row.IsFocused);
		Border background = BackgroundOf(row);
		Assert.Equal(InsetRing, background.BoxShadow);
		window.Close();
	}
}
