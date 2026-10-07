using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//ADR-0262 (#966, pick (a)): the one on-screen pad keyboard, owned by the pad
//bridge. With a synthetic pad only (TickForTest, the same stand-in backend
//PlayPadNavigationTests uses), the Cheats sheet's search field is filled,
//committed and cancelled, and the Add a Code form is filled and committed into
//a stored cheat. The rule itself is pinned host-free in UI.Tests/Play/
//PadKeyboardTests; this checks the realized window feeds it and applies what it
//answers. The real-pad couch check is not here: it joins issue #926.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class PlayPadKeyboardTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly bool _pauseInBackground = ConfigManager.Config.Preferences.PauseWhenInBackground;
	private readonly bool _pauseInMenus = ConfigManager.Config.Preferences.PauseWhenInMenusAndConfig;
	private readonly List<MainWindow> _windows = new();

	//The backend a headless build does not have, built the way the real ones
	//are ("Pad{N} {Button}" at the family's base) - see PlayPadNavigationTests.
	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };
	private static readonly Dictionary<ushort, string> Backend = Enumerable.Range(0, ButtonNames.Length)
		.ToDictionary(button => (ushort)(PadBase + button), button => "Pad1 " + ButtonNames[button]);
	private static readonly Dictionary<string, ushort> BackendCodes = Backend.ToDictionary(pair => pair.Value, pair => pair.Key);

	private static string BackendName(ushort keyCode) => Backend.TryGetValue(keyCode, out string? name) ? name : "";
	private static ushort BackendCode(string name) => BackendCodes.TryGetValue(name, out ushort code) ? code : (ushort)0;

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	public void Dispose()
	{
		//#838: a window that outlives its case is a second top level for the next.
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		PlayPadNavigationWiring.SetOverlayLookupForTest(null);
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.PauseWhenInBackground = _pauseInBackground;
		prefs.PauseWhenInMenusAndConfig = _pauseInMenus;
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowCheats(List<IReadOnlyList<StoredCheat>> saved, bool withIntent = false)
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.PauseWhenInBackground = false;
		prefs.PauseWhenInMenusAndConfig = false;
		MainWindow window = new();
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, "MainWindow never finished building its menus.");
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.CommunityCheatsLastKnown = () => Array.Empty<CommunityCheatGame>();
		model.CommunityCheatsSource = () => Task.FromResult<IReadOnlyList<CommunityCheatGame>?>(Array.Empty<CommunityCheatGame>());
		CheatDbGame contra = new("Contra (USA)", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", new[] { new CheatDbCode("Infinite lives - 1P game", "SZKGPAVG") });
		model.CheatsSheet.Open(ConsoleType.Nes, contra.Sha1, new[] { contra }, Array.Empty<StoredCheat>(), recordingArt: false, disableAll: false, saved.Add);
		if(withIntent) {
			//The key box lives in the intent panel, shown only with a key store
			//and a runner factory (the runner itself is never asked for here).
			model.CheatsSheet.ConfigureIntentSearch(new InMemoryByokKeyStore(), () => Task.FromResult<ICheatIntentRunner?>(null));
		}
		Pump();
		Assert.True(window.FindNamed<TextBox>("CheatsSearchBox").IsOnScreen(), "the Cheats sheet did not open");
		return (window, model);
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 30000) {
				throw new XunitException(failure);
			}
			Pump();
			Thread.Sleep(20);
		}
		Pump();
	}

	private static void Pump()
	{
		Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Background);
		Dispatcher.UIThread.RunJobs();
	}

	//One press and its release: the release is what makes the next press a new
	//edge (the bridge records the pressed set every tick).
	private void Press(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	private static string? FocusedName(MainWindow window) => (window.FocusManager?.GetFocusedElement() as Control)?.Name;

	private static PadKeyboard OpenKeyboard(MainWindow window) =>
		PlayPadNavigationWiring.KeyboardForTest(window) ?? throw new XunitException("the pad's A on a text field did not open the keyboard");

	//The D-pad walks the cursor onto each key, A presses it - a player's path.
	private void Type(MainWindow window, string text)
	{
		foreach(char c in text) {
			PressKeyAt(window, OpenKeyboard(window).IndexOf(c));
		}
	}

	private void PressKey(MainWindow window, PadKeyKind kind)
	{
		PressKeyAt(window, OpenKeyboard(window).Keys.ToList().FindIndex(key => key.Kind == kind));
	}

	private void PressKeyAt(MainWindow window, int index)
	{
		Assert.True(index >= 0, "the key is not on this keyboard");
		for(int steps = 0; OpenKeyboard(window).Cursor != index; steps++) {
			Assert.True(steps < 100, "the D-pad never reached the key");
			Press(window, PadNavAction.Right);
		}
		Press(window, PadNavAction.Confirm);
	}

	private static bool PanelShown(MainWindow window)
	{
		return window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "PadKeyboardPanel");
	}

	//Walks the focus with the D-pad until the named field holds it.
	private void WalkTo(MainWindow window, PadNavAction direction, Func<Control, bool> target, string what)
	{
		for(int steps = 0; !(window.FocusManager?.GetFocusedElement() is Control c && target(c)); steps++) {
			Assert.True(steps < 30, $"the D-pad never reached {what}; focus is on {FocusedName(window) ?? "<unnamed>"}");
			Press(window, direction);
		}
	}

	[AvaloniaFact]
	public void The_pad_types_into_the_cheats_search_and_commits_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new());
		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		search.Focus(NavigationMethod.Directional);
		Pump();

		Press(window, PadNavAction.Confirm);
		Assert.Equal(PadKeyboardShape.Text, OpenKeyboard(window).Shape);
		Assert.True(PanelShown(window), "the keyboard was not drawn");

		Type(window, "lives");
		Assert.Equal("lives", search.Text);
		Assert.Equal("lives", model.CheatsSheet.SearchText);
		PressKey(window, PadKeyKind.Delete);
		Type(window, "s");

		PressKey(window, PadKeyKind.Commit);
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.False(PanelShown(window), "the keyboard stayed on screen after OK");
		Assert.Equal("lives", search.Text);
		Assert.Equal("CheatsSearchBox", FocusedName(window));
	}

	[AvaloniaFact]
	public void Back_on_the_keyboard_restores_the_search_and_its_focus_and_keeps_the_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new());
		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		search.Text = "contra";
		search.Focus(NavigationMethod.Directional);
		Pump();

		Press(window, PadNavAction.Confirm);
		PressKey(window, PadKeyKind.Delete);
		Type(window, "x");
		Assert.Equal("contrx", search.Text);

		Press(window, PadNavAction.Back);
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.Equal("contra", search.Text);
		Assert.Equal("CheatsSearchBox", FocusedName(window));
		Assert.True(search.IsOnScreen(), "Back closed the sheet instead of only the keyboard");
	}

	[AvaloniaFact]
	public void The_pad_fills_the_add_a_code_form_and_the_code_is_stored()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		List<IReadOnlyList<StoredCheat>> saved = new();
		(MainWindow window, MainWindowViewModel model) = ShowCheats(saved);
		window.FindNamed<Button>("CheatsAddCodeButton").Focus(NavigationMethod.Directional);
		Pump();
		Press(window, PadNavAction.Confirm);
		Assert.True(model.CheatsSheet.IsAddCodeOpen, "the pad's A did not open Add a Code");

		WalkTo(window, PadNavAction.Up, c => c.Name == "CheatsNewCodeBox", "the code box");
		Press(window, PadNavAction.Confirm);
		Assert.Equal(PadKeyboardShape.Code, OpenKeyboard(window).Shape);
		Type(window, "SZKGPAVG");
		PressKey(window, PadKeyKind.Commit);
		Assert.Equal("SZKGPAVG", model.CheatsSheet.NewCode);

		WalkTo(window, PadNavAction.Down, c => c is TextBox box && box.Name != "CheatsNewCodeBox", "the description box");
		Press(window, PadNavAction.Confirm);
		Type(window, "lives");
		PressKey(window, PadKeyKind.Commit);

		WalkTo(window, PadNavAction.Down, c => c.Name == "CheatsAddCodeConfirm", "the Add button");
		Press(window, PadNavAction.Confirm);

		StoredCheat added = Assert.Single(Assert.Single(saved));
		Assert.Equal("SZKGPAVG", added.Codes);
		Assert.Equal("lives", added.Description);
	}

	//#994 review 2: the masked secret is checked on the real CheatsKeyBox, so a
	//wiring that stops reading PasswordChar, or a panel that stops drawing
	//Display, fails here instead of drawing the API key in clear.
	[AvaloniaFact]
	public void The_key_box_opens_a_secret_keyboard_whose_panel_never_shows_the_key()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new(), withIntent: true);
		TextBox key = window.FindNamed<TextBox>("CheatsKeyBox");
		Assert.True(key.IsOnScreen(), "the key box is not shown");
		key.Focus(NavigationMethod.Directional);
		Pump();

		Press(window, PadNavAction.Confirm);
		Assert.Equal(PadKeyboardShape.Secret, OpenKeyboard(window).Shape);
		Type(window, "sk-or");

		Assert.Equal("sk-or", model.CheatsSheet.KeyText);
		Assert.Equal("•••••", DraftShown(window));
	}

	//#994 review 2: cancel restores the original value on the secret field too.
	[AvaloniaFact]
	public void Back_on_the_key_box_keyboard_restores_the_key_it_held()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new(), withIntent: true);
		TextBox key = window.FindNamed<TextBox>("CheatsKeyBox");
		key.Text = "sk-old";
		key.Focus(NavigationMethod.Directional);
		Pump();

		Press(window, PadNavAction.Confirm);
		PressKey(window, PadKeyKind.Delete);
		Type(window, "x");
		Assert.Equal("sk-olx", model.CheatsSheet.KeyText);

		Press(window, PadNavAction.Back);
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.Equal("sk-old", model.CheatsSheet.KeyText);
		Assert.Equal("CheatsKeyBox", FocusedName(window));
	}

	//#994 review 3: with no overlay layer to draw in, an open keyboard would be
	//invisible and swallow every press, Back included. The press is not taken.
	[AvaloniaFact]
	public void Without_an_overlay_layer_the_keyboard_does_not_open_and_back_still_works()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new());
		PlayPadNavigationWiring.SetOverlayLookupForTest(_ => null);
		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		search.Focus(NavigationMethod.Directional);
		Pump();

		Press(window, PadNavAction.Confirm);
		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));

		Press(window, PadNavAction.Back);
		Assert.False(search.IsOnScreen(), "Back was swallowed: the sheet is still open");
	}

	//#994 review 4: a click that moves the focus elsewhere cancels the keyboard;
	//the focus stays where the click put it.
	[AvaloniaFact]
	public void Focus_leaving_the_field_cancels_the_keyboard_and_leaves_the_focus_where_it_went()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new());
		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		search.Text = "contra";
		search.Focus(NavigationMethod.Directional);
		Pump();
		Press(window, PadNavAction.Confirm);
		Type(window, "x");
		Assert.Equal("contrax", search.Text);

		window.FindNamed<Button>("CheatsAddCodeButton").Focus(NavigationMethod.Pointer);
		Pump();
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();

		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.False(PanelShown(window), "the keyboard stayed on screen after the focus left its field");
		Assert.Equal("contra", search.Text);
		Assert.Equal("CheatsAddCodeButton", FocusedName(window));
	}

	//#994 review 4: the pad losing authority (here, to the load card)
	//cancels the keyboard, so the pad never comes back editing a stale field.
	[AvaloniaFact]
	public void Losing_pad_authority_cancels_the_keyboard()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowCheats(new());
		TextBox search = window.FindNamed<TextBox>("CheatsSearchBox");
		search.Text = "contra";
		search.Focus(NavigationMethod.Directional);
		Pump();
		Press(window, PadNavAction.Confirm);
		Type(window, "x");

		//The load card refuses the pad authority (ADR-0256) while the sheet,
		//and so the field, stays drawn - so only the authority loss can close it.
		model.BeginLoadWait("Contra (USA)", keepsHome: true);
		Pump();
		Assert.True(model.IsLoadCardVisible, "the load card did not come up");
		Assert.True(search.IsEffectivelyVisible, "the load card hid the field, so this would not test authority");
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();

		Assert.Null(PlayPadNavigationWiring.KeyboardForTest(window));
		Assert.False(PanelShown(window), "the keyboard stayed on screen after the pad lost authority");
		Assert.Equal("contra", search.Text);
	}

	private static string? DraftShown(MainWindow window)
	{
		return window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "PadKeyboardDraft")?.Text;
	}
}
