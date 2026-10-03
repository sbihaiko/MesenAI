using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Utilities;

namespace Mesen.ViewModels
{
	//W-P6 (PRD Part B §13.5.2): the current pack's Textures, Audio and ROM
	//patch, each with a switch that turns it off for this game only
	//(PackLayerSwitches). A flip is stored per ROM beside the pack choice and
	//applied like any pack change in Play - LoadRomHelper.ApplyPackChange, in
	//place where ADR-0244 allows (the sheet stays, its switches wait under a
	//moving bar), else the restart, back to the game like W-P7.
	public partial class MainWindowViewModel
	{
		[ObservableProperty] public partial bool PackDetailTexturesOn { get; set; }
		[ObservableProperty] public partial bool PackDetailAudioOn { get; set; }
		[ObservableProperty] public partial bool PackDetailPatchOn { get; set; }
		[ObservableProperty] public partial bool PackDetailTexturesSwitchable { get; private set; }
		[ObservableProperty] public partial bool PackDetailAudioSwitchable { get; private set; }
		[ObservableProperty] public partial bool PackDetailPatchSwitchable { get; private set; }
		[ObservableProperty] public partial string PackDetailTexturesNote { get; private set; } = "";
		[ObservableProperty] public partial string PackDetailAudioNote { get; private set; } = "";
		[ObservableProperty] public partial string PackDetailPatchNote { get; private set; } = "";
		[ObservableProperty] public partial bool PackDetailApplyingLayer { get; private set; }

		//The last switch's in-place swap: the headless tests wait for it.
		public Task PackLayerApplied { get; private set; } = Task.CompletedTask;

		private PackLayerChips _packDetailLayers = new(false, false, false);
		private bool _fillingPackLayers;

		//OpenPackDetail: the switches start from this game's stored choice.
		private void LoadPackLayerSwitches(PackLayerChips layers)
		{
			_packDetailLayers = layers;
			RefreshPackLayerSwitches();
		}

		private void RefreshPackLayerSwitches()
		{
			_fillingPackLayers = true;
			try {
				PackLayerSwitch textures = SwitchFor(PackLayer.Textures, _packDetailLayers.Textures, Config.EnhancementPacks.EnableTextures);
				PackDetailTexturesOn = textures.On;
				PackDetailTexturesSwitchable = textures.Enabled;
				PackDetailTexturesNote = NoteText(textures.Note);

				PackLayerSwitch audio = SwitchFor(PackLayer.Audio, _packDetailLayers.Audio, Config.EnhancementPacks.EnableAudio);
				PackDetailAudioOn = audio.On;
				PackDetailAudioSwitchable = audio.Enabled;
				PackDetailAudioNote = NoteText(audio.Note);

				PackLayerSwitch patch = SwitchFor(PackLayer.Patch, _packDetailLayers.Patch, Config.EnhancementPacks.EnablePatches);
				PackDetailPatchOn = patch.On;
				PackDetailPatchSwitchable = patch.Enabled;
				PackDetailPatchNote = NoteText(patch.Note);
			} finally {
				_fillingPackLayers = false;
			}
		}

		private PackLayerSwitch SwitchFor(PackLayer layer, bool present, bool globalOn)
		{
			bool romOn = string.IsNullOrEmpty(_pickerRomSha1) || Config.EnhancementPacks.IsRomLayerOn(_pickerRomSha1, layer);
			return PackLayerSwitches.Row(present, Config.EnhancementPacks.EnableMepPacks && globalOn, romOn, PackDetailApplyingLayer);
		}

		private static string NoteText(PackLayerNote note) => note switch {
			PackLayerNote.NotInPack => ResourceHelper.GetMessage("PackLayerNotInPack"),
			PackLayerNote.OffEverywhere => ResourceHelper.GetMessage("PackLayerOffEverywhere"),
			_ => ""
		};

		partial void OnPackDetailTexturesOnChanged(bool value) => SwitchPackLayer(PackLayer.Textures, value);
		partial void OnPackDetailAudioOnChanged(bool value) => SwitchPackLayer(PackLayer.Audio, value);
		partial void OnPackDetailPatchOnChanged(bool value) => SwitchPackLayer(PackLayer.Patch, value);

		private void SwitchPackLayer(PackLayer layer, bool on)
		{
			if(_fillingPackLayers || string.IsNullOrEmpty(_pickerRomSha1) || on == Config.EnhancementPacks.IsRomLayerOn(_pickerRomSha1, layer)) {
				return;
			}
			Config.EnhancementPacks.SetRomLayerOn(_pickerRomSha1, layer, on);
			Config.EnhancementPacks.ApplyConfig();
			Config.Save();

			//Every visible wait moves (#734): the switches wait under a bar
			//until the in-place swap is done.
			bool keepsPlace = LayerChangeKeepsPlace;
			SetPackLayerApplying(true);
			Task applied = LoadRomHelper.ApplyPackChange(RomInfo.ConsoleType, LoadRomHelper.ReloadRom);
			PackLayerApplied = applied;
			if(!keepsPlace) {
				//The game restarts (the load card shows): back to it, like W-P7.
				SetPackLayerApplying(false);
				IsPackDetailVisible = false;
				IsPlayerOverlayVisible = false;
				EmuApi.Resume();
				return;
			}
			applied.ContinueWith(_ => Dispatcher.UIThread.Post(() => SetPackLayerApplying(false)));
		}

		private void SetPackLayerApplying(bool applying)
		{
			PackDetailApplyingLayer = applying;
			RefreshPackLayerSwitches();
		}
	}
}
