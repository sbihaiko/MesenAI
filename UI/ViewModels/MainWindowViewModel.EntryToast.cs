using System;
using System.Collections.Generic;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//ADR-0251 (W-P3): the toast a game start shows in Player mode. It names the
	//pack it applied and, during the first three starts, ends with the way into
	//W-P4 ("· Esc for the menu", or the controller binding). The rule is
	//host-free in UI/Logic/PlayMenuHint.
	public partial class MainWindowViewModel
	{
		//The last entry toast shown (null when the start showed none).
		public PlayEntryToast? LastEntryToast { get; private set; }

		//Where the toast reads the ToggleOverlay slots' key names and the
		//connected pad count. The headless tests replace them: they run the
		//Core without a key manager, where every key name is empty.
		public Func<(List<string> First, List<string> Second)> OverlayBindingKeyNames { get; set; } = () => ConfigManager.Config.Preferences.OverlayBindingKeyNames();
		public Func<uint> ConnectedGamepadCount { get; set; } = InputApi.GetConnectedGamepadCount;

		//gameStart is false for a power-cycle reload, which is not a new start.
		public void ShowPlayEntryToast(bool gameStart)
		{
			PreferencesConfig prefs = Config.Preferences;
			if(prefs.UiMode != UiMode.Player) {
				LastEntryToast = null;
				return;
			}

			string packText = "";
			if(!string.IsNullOrEmpty(CurrentPackName)) {
				packText = CurrentPackName + (string.IsNullOrEmpty(CurrentPackLayers) ? "" : " — " + CurrentPackLayers);
			}

			(List<string> first, List<string> second) = OverlayBindingKeyNames();
			PlayInputDevice device = PlayMenuHint.ActiveDevice(ConnectedGamepadCount());
			string? binding = PlayMenuHint.BindingName(device, first, second);

			PlayEntryToast? toast = PlayMenuHint.EntryToast(packText, gameStart, IsPlayWorkspace, prefs.PlayMenuHintsShown, binding);
			if(toast != null) {
				EmuApi.DisplayMessage(toast.Title, toast.Message, toast.Param);
				prefs.PlayMenuHintsShown = PlayMenuHint.Next(prefs.PlayMenuHintsShown, toast);
			}
			LastEntryToast = toast;
		}
	}
}
