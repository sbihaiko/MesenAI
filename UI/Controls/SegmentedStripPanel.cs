using Avalonia;
using Avalonia.Controls;
using Mesen.Logic;
using System;

namespace Mesen.Controls
{
	//The segmented tab strip's panel (PlayerTheme.axaml, "segmented tab
	//strip"): every child gets the same width and they are laid out in a row.
	//The rule behind the width - 96 px a segment while 96 px still fits the
	//strip, an even share of it below that - is
	//PlayerSettingsEssentials.SegmentWidth, which is host-free and pinned by
	//UI.Tests; this panel only arranges by it. The why is in that file (#852).
	public class SegmentedStripPanel : Panel
	{
		protected override Size MeasureOverride(Size availableSize)
		{
			double width = PlayerSettingsEssentials.SegmentWidth(availableSize.Width, Children.Count);
			double height = 0;
			foreach(Control child in Children) {
				child.Measure(new Size(width, availableSize.Height));
				height = Math.Max(height, child.DesiredSize.Height);
			}
			return new Size(width * Children.Count, height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			double width = PlayerSettingsEssentials.SegmentWidth(finalSize.Width, Children.Count);
			for(int i = 0; i < Children.Count; i++) {
				Children[i].Arrange(new Rect(i * width, 0, width, finalSize.Height));
			}
			return new Size(width * Children.Count, finalSize.Height);
		}
	}
}
