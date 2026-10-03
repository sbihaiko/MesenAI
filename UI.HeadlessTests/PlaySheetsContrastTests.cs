using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#716: the pause overlay and the Play sheets it opens draw a dark scrim over
//the game. In the light theme (the app default) their text used to inherit the
//theme's black foreground - black on near-black, unreadable - while the dark
//theme was fine. This opens each sheet under both theme variants and checks
//every visible, enabled TextBlock against what is actually painted behind it:
//the brushes of its ancestors composited from the sheet down, over an opaque
//black frame (the emulator picture or Play's dark home behind the sheet; black
//is the worst case for dark text). WCAG 2 AA asks 4.5:1 for body text.
//Disabled controls are exempt, as in WCAG, and so is a TextBox's themed
//placeholder hint.
[Collection(NativeCoreCollection.Name)]
public class PlaySheetsContrastTests : IDisposable
{
	private const double MinContrast = 4.5;
	//Accent buttons carry colors chosen on purpose (system blue, alert red):
	//the two named here, and every ADR-0249 `primary`/`destructive` button,
	//whose white-on-tint and RED-on-pale-red pairs are the rendered
	//wireframes' tokens. They are held to the 3:1 WCAG minimum for large text
	//and UI components instead of AA body text.
	private const double AccentMinContrast = 3.0;
	private static readonly HashSet<string> AccentButtons = new() { "OverlayResumeButton", "OverlayQuitGameButton" };
	private static bool IsAccent(Button b) => AccentButtons.Contains(b.Name ?? "") || b.Classes.Contains("primary") || b.Classes.Contains("destructive");
	private const string OnePack = "aaa\tAaa Pack\t1.2\tTastic\tCC BY-NC 4.0\ttextures,audio\t1\t0\tissue-1\tc1\n";
	private const string TwoPacks =
		"aaa\tAaa Pack\t1.0\t\t\ttextures\t1\t0\tissue-1\tc1\n" +
		"bbb\tBbb Pack\t1.0\t\t\ttextures\t1\t0\tissue-2\tc2\n";
	private const string Sha1 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		if(Application.Current != null) {
			Application.Current.RequestedThemeVariant = ThemeVariant.Light;
		}
	}

	public static IEnumerable<object[]> Sheets()
	{
		string[] sheets = { "PlayerOverlay", "PlayerSaveStatesSheet", "PlayerPackDetailSheet", "PlayerPackPicker", "PlayerEnhancementsPanel", "PlayerCheatsSheet", "PlayerReplaysSheet", "PlayHomeSlotSheet", "PackDepSheet", "BiosSheet", "PlayHomeLoadAlert" };
		foreach(string theme in new[] { "Light", "Dark" }) {
			foreach(string sheet in sheets) {
				yield return new object[] { theme, sheet };
			}
		}
	}

	[AvaloniaTheory]
	[MemberData(nameof(Sheets))]
	public void Every_text_on_a_play_sheet_is_readable_against_its_scrim(string theme, string sheet)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		MainWindow window = OpenSheet(sheet);

		Border root = window.FindNamed<Border>(sheet);
		Assert.True(root.IsOnScreen(), sheet + " is not on screen");
		TextBlock[] texts = root.GetVisualDescendants().OfType<TextBlock>()
			.Where(t => t.IsOnScreen() && t.IsEffectivelyEnabled && !string.IsNullOrWhiteSpace(t.Text))
			//A TextBox's placeholder is a hint drawn by the theme, not sheet text.
			.Where(t => t.TemplatedParent is not TextBox)
			.ToArray();
		Assert.NotEmpty(texts);

		List<string> unreadable = new();
		foreach(TextBlock text in texts) {
			Color back = BackgroundBehind(text, root);
			Color fore = Over(EffectiveForeground(text, root), back);
			double ratio = Contrast(fore, back);
			bool accent = text.GetVisualAncestors().OfType<Button>().FirstOrDefault() is Button b && IsAccent(b);
			if(ratio < (accent ? AccentMinContrast : MinContrast)) {
				unreadable.Add($"'{text.Text}' {fore} on {back} = {ratio:0.00}:1");
			}
		}
		Assert.True(unreadable.Count == 0, $"{theme} theme, {sheet}: {unreadable.Count} of {texts.Length} texts below {MinContrast}:1 ({AccentMinContrast}:1 on an accent button)\n" + string.Join("\n", unreadable));
	}

	private static MainWindow OpenSheet(string sheet)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new();
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		//ADR-0249 wave 2: the BIOS sheet and the load alert sit on Play's home,
		//with no game running; every other surface is over a paused game.
		if(sheet is not ("BiosSheet" or "PlayHomeLoadAlert")) {
			model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
			model.OpenPauseOverlay();
		}
		Dispatcher.UIThread.RunJobs();

		switch(sheet) {
			case "PlayerOverlay":
				break;
			case "PlayerSaveStatesSheet":
				model.OpenSaveStatesSheet();
				break;
			case "PlayerPackDetailSheet":
				model.OpenPackFromOverlay(OnePack, Sha1, "/packs", "", installedSourceSha256: "abc123");
				break;
			case "PlayerPackPicker":
				model.OpenPackFromOverlay(TwoPacks, Sha1, "/packs", "", installedSourceSha256: null);
				break;
			case "PlayerEnhancementsPanel":
				model.OpenEnhancementsPanel();
				break;
			case "PlayerCheatsSheet":
				CheatDbGame contra = new("Contra (USA)", Sha1, new[] {
					new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG"),
					new CheatDbCode("Invincibility (star effect)", "00B0:FF"),
				});
				model.CheatsSheet.Open(ConsoleType.Nes, Sha1, new[] { contra }, Array.Empty<StoredCheat>(), recordingArt: true, disableAll: false, _ => { });
				break;
			case "PlayHomeSlotSheet":
				model.OpenSaveStatesSheet();
				Dispatcher.UIThread.RunJobs();
				window.FindNamed<Button>("SaveStatesLoadButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
				break;
			case "PackDepSheet":
				model.PackDepSheet.SetPending("Contra Arcade Music", new[] { new CommunityPackDepPrompt("arcade-soundtrack", "Arcade soundtrack (MP3 set, 23 files)", "", "/packs/drop", "") });
				model.PackDepSheet.Open();
				break;
			case "BiosSheet":
				_ = model.RequestBios(FirmwareType.FDS, "disksys.rom", 8192, 0, "Zelda no Densetsu");
				break;
			case "PlayHomeLoadAlert":
				model.RecentGames.Init(GameScreenMode.RecentGames);
				model.RecentGames.ShowLoadFailure(LoadFailureCause.NotAGame, "Contra.txt");
				break;
			case "PlayerReplaysSheet":
				CommunityReplay replay = new(301, "https://github.com/user-attachments/files/301/run.mmo", new string('c', 64), 4096, "nes", "Contra (USA)", "alice", "stage skip", 3600,
					new[] { new CommunityReplayCheat("NesCustom", "0032:00") }, 2);
				CommunityReplayGame[] catalog = { new(Sha1, "Contra (USA)", new[] { replay }) };
				model.CommunityReplaysLastKnown = () => catalog;
				model.CommunityReplaysSource = () => Task.FromResult<IReadOnlyList<CommunityReplayGame>?>(null);
				model.ReplayRomSha1 = () => Sha1;
				model.ReplayWatchReasonSource = () => ReplayWatchReason.None;
				model.OpenSaveStatesSheet();
				Dispatcher.UIThread.RunJobs();
				window.FindNamed<Button>("SaveStatesReplaysButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
				break;
		}
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();
		return window;
	}

	//The text's own brush, faded by its own and its ancestors' Opacity up to the sheet.
	private static Color EffectiveForeground(TextBlock text, Visual root)
	{
		ISolidColorBrush brush = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
		double alpha = brush.Color.A / 255.0 * brush.Opacity;
		for(Visual? v = text; v != null; v = v.GetVisualParent()) {
			alpha *= v.Opacity;
			if(v == root) {
				break;
			}
		}
		return Color.FromArgb((byte)Math.Round(alpha * 255), brush.Color.R, brush.Color.G, brush.Color.B);
	}

	//What is painted behind the text: every background between the sheet and the
	//text, outermost first, over an opaque black frame. A templated control does
	//not paint its own Background; its template's Border/ContentPresenter does.
	//A sibling drawn earlier under the same parent (a TextBox paints its field
	//on a Border beside, not around, its text) counts when it covers the text.
	private static Color BackgroundBehind(TextBlock text, Visual root)
	{
		Rect area = OnScreen(text);
		List<IBrush> layers = new();
		for(Visual? v = text; v != null; v = v.GetVisualParent()) {
			if(Painted(v) is IBrush own) {
				layers.Add(own);
			}
			if(v == root) {
				break;
			}
			Visual[] siblings = v.GetVisualParent()?.GetVisualChildren().ToArray() ?? Array.Empty<Visual>();
			for(int i = Array.IndexOf(siblings, v) - 1; i >= 0; i--) {
				Visual s = siblings[i];
				if(s.IsEffectivelyVisible && Painted(s) is IBrush under && OnScreen(s).Contains(area.Center)) {
					layers.Add(under);
				}
			}
		}
		Color back = Colors.Black;
		for(int i = layers.Count - 1; i >= 0; i--) {
			ISolidColorBrush solid = Assert.IsAssignableFrom<ISolidColorBrush>(layers[i]);
			Color c = solid.Color;
			back = Over(Color.FromArgb((byte)Math.Round(c.A * solid.Opacity), c.R, c.G, c.B), back);
		}
		return back;
	}

	//A visual's own rectangle in window coordinates, cut by its clip.
	//(TransformedBounds.Clip alone is the ancestors' clip region, not the
	//visual: with it, an icon badge beside a row's text counted as behind it.)
	private static Rect OnScreen(Visual v)
	{
		if(v.GetTransformedBounds() is not TransformedBounds tb) {
			return default;
		}
		return tb.Bounds.TransformToAABB(tb.Transform).Intersect(tb.Clip);
	}

	private static IBrush? Painted(Visual v) => v switch {
		TextBlock t => t.Background,
		Border b => b.Background,
		Panel p => p.Background,
		ContentPresenter c => c.Background,
		_ => null,
	};

	private static Color Over(Color top, Color under)
	{
		double a = top.A / 255.0;
		byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
		return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
	}

	private static double Luminance(Color c)
	{
		static double Channel(byte v)
		{
			double s = v / 255.0;
			return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
		}
		return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
	}

	private static double Contrast(Color a, Color b)
	{
		double la = Luminance(a), lb = Luminance(b);
		return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
	}
}
