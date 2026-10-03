namespace Mesen.Logic;

//The four tones a classic message box carries (MessageBoxIcon, mirrored here
//so the rule stays host-free).
public enum DialogTone
{
	Error,
	Warning,
	Question,
	Info
}

//ADR-0249 (W-X1, W-X2): a dialog the Player GUI still raises - a message box,
//an archive's game list, a shader's parameters - takes the Player look when
//its owner shows the Player theme (MainWindow, or Player-mode Settings), and
//keeps the classic look under a classic window, the debugger or Advanced mode.
//The look is the shared banner (an error the stop, a warning or a question the
//warning, information the pale blue one) and the renders' button order: the
//safe button first, the one that goes on last, on the right.
public static class PlayerDialog
{
	public static bool UsesPlayerLook(bool playerMode, bool ownerShowsPlayerTheme)
	{
		return playerMode && ownerShowsPlayerTheme;
	}

	public static BannerKind BannerOf(DialogTone tone)
	{
		return tone switch {
			DialogTone.Error => BannerKind.Stop,
			DialogTone.Info => BannerKind.Info,
			_ => BannerKind.Warning,
		};
	}

	//The classic buttons (OK/Yes first, Cancel/No after) in Player order, as
	//indexes into the classic list.
	public static int[] ButtonOrder(int count)
	{
		int[] order = new int[count];
		for(int i = 0; i < count; i++) {
			order[i] = count - 1 - i;
		}
		return order;
	}

	//OK or Yes - the first classic button - is the primary one.
	public static bool IsPrimary(int classicIndex)
	{
		return classicIndex == 0;
	}
}
