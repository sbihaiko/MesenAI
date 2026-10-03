using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;

namespace Mesen.ViewModels
{
	//#736: an accepted community pack for the loaded game that is not the one
	//rendering (turned off, not chosen, or not installed - auto-install off
	//included). W-P4's Pack row says "Community pack available" and W-P6 says
	//why in plain words, with Use Community Pack. The decision is host-free
	//(CommunityPackOfferRule); the window injects what it reads from the core,
	//the catalog copy on disk and the install registry, and runs the action.
	public partial class MainWindowViewModel
	{
		[ObservableProperty] public partial bool PackDetailHasOffer { get; private set; }
		[ObservableProperty] public partial string PackDetailOfferBody { get; private set; } = "";

		//The offer W-P6 shows (and Use Community Pack acts on).
		public CommunityPackOffer CommunityOffer { get; private set; } = CommunityPackOffer.None;

		//What W-P4's Pack row needs when the overlay opens: the core's pack
		//list, the ROM's No-Intro SHA-1 and the offer context. Set by the
		//window (MainWindow.PlaySheets.cs); headless tests replace it.
		public Func<PackRowState?>? ReadPackRowState { get; set; }

		private CommunityPackOffer DecideCommunityOffer(string packListText, string romSha1, CommunityPackOfferContext? community)
		{
			if(community == null) {
				return CommunityPackOffer.None;
			}
			MepPackListResult parsed = MepPackListParser.Parse(packListText);
			PackPreferenceResolver.Resolution resolution = PackPreferenceResolver.Resolve(OfferedCandidates(parsed), Config.EnhancementPacks.GetRomPackPreference(romSha1));
			string? rendered = PlayerPackPicker.CurrentContainer(resolution.Candidates, resolution.PreferredContainer);
			return CommunityPackOfferRule.Decide(community, parsed.Packs, rendered, Config.EnhancementPacks.EnableMepPacks, Config.EnhancementPacks.AutoInstallCommunityPacks);
		}

		//W-P4's Pack row value: the offer, else the pack that renders.
		private string BuildPackSummary()
		{
			PackRowState? state = ReadPackRowState?.Invoke();
			if(state != null && DecideCommunityOffer(state.PackList, state.RomSha1, state.Community).IsShown) {
				return ResourceHelper.GetMessage("OverlayPackCommunityAvailable");
			}
			return string.IsNullOrWhiteSpace(CurrentPackName) ? ResourceHelper.GetMessage("OverlayRowNone") : CurrentPackName;
		}

		private void ShowCommunityOffer(CommunityPackOffer offer, string packListText)
		{
			CommunityOffer = offer;
			PackDetailHasOffer = offer.IsShown;
			PackDetailOfferBody = offer.Reason switch {
				CommunityPackOfferReason.TurnedOff => ResourceHelper.GetMessage("PackOfferTurnedOff", offer.PackName),
				CommunityPackOfferReason.NotChosen => RenderedName(offer, packListText) is string other
					? ResourceHelper.GetMessage("PackOfferNotChosen", offer.PackName, other)
					: ResourceHelper.GetMessage("PackOfferNotInUse", offer.PackName),
				CommunityPackOfferReason.AutoInstallOff => ResourceHelper.GetMessage("PackOfferAutoInstallOff", offer.PackName),
				CommunityPackOfferReason.NotInstalled => ResourceHelper.GetMessage("PackOfferNotInstalled", offer.PackName),
				_ => ""
			};
		}

		private static string? RenderedName(CommunityPackOffer offer, string packListText)
		{
			if(offer.RenderedContainer == null) {
				return null;
			}
			MepPackListEntry? entry = MepPackListParser.Parse(packListText).Packs
				.FirstOrDefault(e => e.Container.Equals(offer.RenderedContainer, StringComparison.OrdinalIgnoreCase));
			return string.IsNullOrWhiteSpace(entry?.Name) ? null : entry.Name;
		}

		//W-P6's Use Community Pack for an installed pack (turned back on by the
		//window first, when it was off): the P.5 pick - the ROM's stored choice,
		//applied through LoadRomHelper.ApplyPackChange - closing back to W-P4
		//(in place) or to the game (restart), like W-P5 opened from W-P4 (#691).
		public void UseOfferedPack(string packListText, string container)
		{
			IsPackDetailVisible = false;
			ShowCommunityOffer(CommunityPackOffer.None, "");
			BuildPackPickerData(packListText, _pickerRomSha1, out _, out _);
			IsPlayerPackPickerVisible = false;
			if(!PlayerPackChoices.Any(c => c.Container.Equals(container, StringComparison.OrdinalIgnoreCase))) {
				OpenPauseOverlay();
				return;
			}
			_packPickerFromOverlay = true;
			PickPlayerPack(container);
		}

		//W-P6's Use Community Pack for a pack to install: the install runs with
		//the W-P9 pill and restarts the game when done - back to the game.
		public void LeaveDetailForCommunityInstall()
		{
			IsPackDetailVisible = false;
			ShowCommunityOffer(CommunityPackOffer.None, "");
			IsPlayerOverlayVisible = false;
			Interop.EmuApi.Resume();
		}
	}

	public sealed record PackRowState(string PackList, string RomSha1, CommunityPackOfferContext? Community);
}
