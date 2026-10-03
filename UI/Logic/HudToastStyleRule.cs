namespace Mesen.Logic;

//"Estilizar o HUD do Core" (user's decision, 2026-10-03): which look the
//Core's system toasts (EmuApi.DisplayMessage -> SystemHud) are drawn in.
//Mirrors Core/Shared/SettingTypes.h's `enum class HudToastStyle` value for
//value; InteropPreferencesConfig carries it to the Core.
public enum HudToastStyle
{
	//Mesen's outlined text in the bottom-left corner, "[title] message".
	Classic,
	//W-P3/W-P9's rounded dark card in the bottom-right corner, with a status
	//glyph and the message alone (Core/Shared/Video/HudToastLayout.h).
	Player
}

public static class HudToastStyleRule
{
	//Player mode draws the Player card; Advanced keeps the classic toast.
	public static HudToastStyle For(UiMode mode)
	{
		return mode == UiMode.Player ? HudToastStyle.Player : HudToastStyle.Classic;
	}
}
