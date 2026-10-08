using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Config.Shortcuts
{
	//Split out of EmulatorShortcut.cs, which now holds the enum alone: UI/Logic
	//dual-compiles into UI.Tests and has to name the actions it filters (ADR-0255
	//slice 4's EXTRA BUTTONS section), so the enum has to be reachable from there
	//- and these methods reach ConfigManager and InputApi, which is exactly what
	//cannot. Behavior is unchanged; only the file boundary moved.
	public static class EmulatorShortcutExtensions
	{
		public static KeyCombination? GetShortcutKeys(this EmulatorShortcut shortcut)
		{
			PreferencesConfig cfg = ConfigManager.Config.Preferences;
			int keyIndex = cfg.ShortcutKeys.FindIndex((ShortcutKeyInfo shortcutInfo) => shortcutInfo.Shortcut == shortcut);
			if(keyIndex >= 0) {
				if(!cfg.ShortcutKeys[keyIndex].KeyCombination.IsEmpty) {
					return cfg.ShortcutKeys[keyIndex].KeyCombination;
				} else if(!cfg.ShortcutKeys[keyIndex].KeyCombination2.IsEmpty) {
					return cfg.ShortcutKeys[keyIndex].KeyCombination2;
				} else if(cfg.ShortcutKeys[keyIndex].PadBinding is PadShortcutBinding pad && !pad.IsEmpty) {
					//ADR-0255 slice 4: the chain ends on the pad slot, which is how
					//a shortcut with a button and no key still has something to
					//display (the context menu's shortcut text). A shortcut that
					//has a key returns it exactly as it did.
					return pad.ToKeyCombination();
				}
			}
			return null;
		}
	}
}
