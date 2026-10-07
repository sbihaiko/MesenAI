using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Mesen.Logic;

namespace Mesen.Windows
{
	//ADR-0262: what the on-screen pad keyboard looks like - built, painted and
	//placed in the window's overlay layer. PlayPadNavigationWiring decides when
	//it opens and closes; this only draws a PadKeyboard. Split out of the bridge
	//by the #994 review (the bridge's file size).
	internal static class PadKeyboardPanel
	{
		//Nothing in the panel is focusable: the focus stays on the field, and
		//the cursor is drawn by Paint instead.
		public static Border Build(PadKeyboard keyboard)
		{
			UniformGrid keys = new() { Columns = PadKeyboard.Columns };
			foreach(PadKeyboardKey _ in keyboard.Keys) {
				keys.Children.Add(new Border() {
					MinWidth = 36, MinHeight = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
					Child = new TextBlock() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Brushes.White }
				});
			}
			StackPanel content = new() { Spacing = 6 };
			content.Children.Add(new TextBlock() { Name = "PadKeyboardDraft", Foreground = Brushes.White, FontSize = 18 });
			content.Children.Add(keys);
			return new Border() {
				Name = "PadKeyboardPanel", Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x20, 0x24)),
				CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Focusable = false, Child = content
			};
		}

		//The draft is drawn through Display, so a secret is only ever bullets.
		public static void Paint(Border panel, PadKeyboard keyboard)
		{
			if(panel.Child is not StackPanel content || content.Children[0] is not TextBlock draft || content.Children[1] is not UniformGrid keys) {
				return;
			}
			draft.Text = keyboard.Display;
			for(int i = 0; i < keys.Children.Count; i++) {
				if(keys.Children[i] is Border key && key.Child is TextBlock label) {
					label.Text = keyboard.Label(keyboard.Keys[i]);
					key.Background = i == keyboard.Cursor ? Brushes.DodgerBlue : Brushes.Transparent;
				}
			}
		}

		//Below the field, or above it when the window has no room below.
		public static void Place(TextBox field, OverlayLayer layer, Border panel)
		{
			panel.Measure(Size.Infinity);
			Point below = field.TranslatePoint(new Point(0, field.Bounds.Height + 4), layer) ?? default;
			double top = below.Y + panel.DesiredSize.Height <= layer.Bounds.Height || layer.Bounds.Height <= 0
				? below.Y
				: Math.Max(0, below.Y - field.Bounds.Height - 8 - panel.DesiredSize.Height);
			Canvas.SetLeft(panel, Math.Max(0, Math.Min(below.X, layer.Bounds.Width - panel.DesiredSize.Width)));
			Canvas.SetTop(panel, top);
		}
	}
}
