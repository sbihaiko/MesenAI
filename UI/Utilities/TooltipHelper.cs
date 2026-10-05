using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Mesen.Utilities
{
	public static class TooltipHelper
	{
		public static void ShowTooltip(Control target, object? tooltipContent, int horizontalOffset)
		{
			try {
				//Issue #872: replacing a tip discards the previous content, so release
				//the subscriptions it owns. A tooltip re-shown as the same instance
				//(updated in place) must not be torn down - hence the reference check.
				object? previous = ToolTip.GetTip(target);

				ToolTip.SetShowDelay(target, 0);
				ToolTip.SetTip(target, tooltipContent);

				//Force tooltip to update its position
				ToolTip.SetHorizontalOffset(target, horizontalOffset - 1);
				ToolTip.SetHorizontalOffset(target, horizontalOffset);

				ToolTip.SetIsOpen(target, true);

				//One instance belongs to one target. Handing the same tooltip to a
				//second target moves it out of the first target's popup, which fires
				//DynamicTooltip.OnDetachedFromVisualTree and releases subscriptions
				//the instance may still need on the target it was just taken from -
				//and this reference check cannot see that, because it reads the NEW
				//target's previous tip, not the other target's. Every call site today
				//uses one target per instance; a future one that shares a
				//picture-bearing tooltip between two targets would need re-showing
				//rather than re-hosting.
				if(previous is IDisposable disposable && !ReferenceEquals(previous, tooltipContent)) {
					disposable.Dispose();
				}
			} catch(Exception) {
				HideTooltip(target);
			}
		}

		public static void HideTooltip(Control target)
		{
			try {
				//Issue #872: clearing the tip is how every popup tooltip is dropped
				//(the pointer-exited handlers and their kin). Release the content's
				//subscriptions here so no call site has to remember to.
				object? content = ToolTip.GetTip(target);

				ToolTip.SetTip(target, null);
				ToolTip.SetIsOpen(target, false);

				(content as IDisposable)?.Dispose();
			} catch(Exception) { }
		}
	}
}
