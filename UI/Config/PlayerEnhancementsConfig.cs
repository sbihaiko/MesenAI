using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Mesen.Config;

//P.7 (PRD Part B §6.1/§6.2): state the Player-mode "Enhancements" overlay
//panel and home cards need to persist, on top of settings that already
//exist elsewhere (EnhancementPackConfig.EnableTextures/EnableAudio,
//VideoConfig.AspectRatio/VideoFilter, the per-console overclock fields).
//This class has no Core counterpart - it is never marshaled to the
//native side, unlike EnhancementPackConfig/VideoConfig/etc.
public partial class PlayerEnhancementsConfig : BaseConfig<PlayerEnhancementsConfig>
{
	//WideScrn/HiRes toggle off restores exactly what was configured before
	//the toggle was switched on (not a hardcoded default) - these two
	//fields are that "before" value, only meaningful while the
	//corresponding toggle is on (VideoConfig.AspectRatio == Widescreen /
	//VideoConfig.VideoFilter == HQ4x). Read/written by
	//UI/Logic/PlayerEnhancementsToggle.cs's pure toggle functions.
	[ObservableProperty] public partial VideoAspectRatio WideScrnPriorAspectRatio { get; set; } = VideoAspectRatio.NoStretching;
	//No longer written: Hi-res filter left the quick panel (ADR-0246). Kept so
	//existing settings files still load.
	[ObservableProperty] public partial VideoFilterType HiResPriorFilter { get; set; } = VideoFilterType.None;

	//The Welcome card (§6.2) showed once, on the very first Player-mode
	//boot. G.2 replaced it with the W-P1 first-run home, which shows whenever
	//there is no recent game, so nothing reads this key any more; it is kept
	//so an existing settings.json still round-trips it unchanged.
	[ObservableProperty] public partial bool WelcomeCardDismissed { get; set; } = false;

	//ADR-0253 §4 (W.5): per-ROM-sha1 widescreen support the core measured,
	//keyed like EnhancementPackConfig.RomPackPreference. Only a measured
	//"unsupported" is stored (value false): absent means never measured, or
	//measured supported - a later run that finds content, or a pack with
	//widescreen art, drops the key. The Enhancements sheet's Widescreen switch
	//is shown disabled with its reason for a ROM recorded here.
	public Dictionary<string, bool> RomWidescreenSupport { get; set; } = new();

	//True only for a ROM the core measured with nothing beside the picture.
	public bool IsRomWidescreenUnsupported(string romSha1)
	{
		return !string.IsNullOrEmpty(romSha1)
			&& RomWidescreenSupport.TryGetValue(romSha1, out bool supported)
			&& !supported;
	}

	//Records what the core measured. Supported (or a later run that found
	//content) drops the key, so the switch comes back.
	public void SetRomWidescreenSupport(string romSha1, bool supported)
	{
		if(string.IsNullOrEmpty(romSha1)) {
			return;
		}
		if(supported) {
			RomWidescreenSupport.Remove(romSha1);
		} else {
			RomWidescreenSupport[romSha1] = false;
		}
	}
}
