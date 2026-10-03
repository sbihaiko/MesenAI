using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Config;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;
using System;
using System.Collections.Generic;

namespace Mesen.ViewModels
{
	//ADR-0245 §3 / ADR-0184 §1: a Remaster recording carries RAM codes only.
	//Record While I Play refuses to start while a cheat that is not a RAM code
	//is on, naming it (refuse, not warn); while it records, CheatCodes holds
	//back any such code turned on later (the classic window, or W-P11 from Play
	//over the running recording), and the strip says which. The rule is
	//host-free in UI/Logic/CheatRecordingRule.
	public partial class RemasterWorkspaceViewModel
	{
		//The game's stored cheats and the classic window's "disable all" switch.
		//Replaced by tests, which run without a game.
		public Func<IReadOnlyList<StoredCheat>> StoredCheats { get; set; } = PlayerCheatsStore.LoadStored;
		public Func<bool> AllCheatsDisabled { get; set; } = () => ConfigManager.Config.Cheats.DisableAllCheats;

		//W-R2: "" or the codes held off while recording.
		[ObservableProperty] public partial string HeldCheatsText { get; private set; } = "";

		//"" when the recording may start, else the inline refusal naming the codes.
		private string CheatRefusal()
		{
			IReadOnlyList<StoredCheat> refused = CheatRecordingRule.Refused(StoredCheats(), AllCheatsDisabled());
			return refused.Count == 0 ? "" : ResourceHelper.GetMessage("RemasterRecordRefusedCheats", CheatRecordingRule.Names(refused));
		}

		private void BeginRecordingArtCheats()
		{
			CheatCodes.HeldForRecordingChanged -= OnHeldCheatsChanged;
			CheatCodes.HeldForRecordingChanged += OnHeldCheatsChanged;
			CheatCodes.SetRecordingArt(true);
			RefreshHeldCheats();
		}

		private void EndRecordingArtCheats()
		{
			CheatCodes.HeldForRecordingChanged -= OnHeldCheatsChanged;
			if(CheatCodes.RecordingArt) {
				//Codes held back during the recording reach the core again.
				CheatCodes.SetRecordingArt(false);
			}
			HeldCheatsText = "";
		}

		private void OnHeldCheatsChanged() => Dispatcher.UIThread.Post(RefreshHeldCheats);

		private void RefreshHeldCheats()
		{
			IReadOnlyList<StoredCheat> held = CheatCodes.HeldForRecording;
			HeldCheatsText = held.Count == 0 ? "" : ResourceHelper.GetMessage("RemasterCheatsHeld", CheatRecordingRule.Names(held));
		}
	}
}
