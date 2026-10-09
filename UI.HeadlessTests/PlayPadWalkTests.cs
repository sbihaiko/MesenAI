using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Controls;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1107 (spec #1102 slice 2, ADR-0256 stop rule, PRD Part B §13.3 rule 9): one
//walk over every Play surface with only the D-pad, A and B. It fails when a
//pad player could be stuck, in exactly three ways:
//  - a control the surface shows is not reachable from the pad;
//  - B does not leave a surface that declares (or owes) a Back;
//  - the shared action bar (#1104) names an action the surface does not have.
//
//The judgement is PadWalk.Judge (UI/Logic, pure; its negative cases live in
//UI.Tests/Play/PadWalkJudgeTests). The live cases below only produce the
//observation from a real MainWindow, so they self-skip without the core and
//the evidence of a pass is the filtered class run with MESEN_CORE_LIB set.
//
//Scale: every live case is parameterized by the interface size (1.0 = Standard,
//1.5 = Extra large, #1111), so a size can never strand the pad.
[Collection(NativeCoreCollection.Name)]
public class PlayPadWalkTests : IDisposable
{
	//The scales a case runs at: 1.0 is what ships today, 1.5 is Extra large.
	public static IEnumerable<object[]> Scales => new[] {
		new object[] { 1.0 },
		new object[] { 1.5 },
	};

	public static IEnumerable<object[]> SurfacesAtEveryScale =>
		from scale in new[] { 1.0, 1.5 } from surface in WalkedSurfaces select new object[] { surface, scale };

	//Registered claims in PlayPadNavigationWiring.RegisterSurfaces plus the
	//content area, by the surface's name. Walked surfaces have an opener below;
	//a surface here that is not walked is a gap the next ticket closes.
	public static readonly string[] WalkedSurfaces = { "Home", "HomeFirstRun", "PauseOverlay", "SaveStates", "Enhancements", "Library", "ToolSheetAbout", "SettingsDisplay" };

	//Registered claims whose opener needs state this harness does not build yet
	//(a loaded pack, a cheat database, a failed load...). Named so the list
	//cannot grow silently: RegisteredClaimCount below fails when a claim is
	//added without a row in WalkedSurfaces or here.
	public static readonly string[] NotWalkedYet = {
		"QuitGameConfirm", "SelectRomSheet", "ShaderSheet", "BiosSheet", "ControllerSetup", "SettingsSystemTab",
		"ControllerSheet", "PackDepSheet", "PackPicker", "PackDetail", "Cheats", "Replays",
	};

	//Surfaces that have no declaration on the shared bar yet (Declared() is
	//null). Home and the shared bar's own surfaces are on it; these are the
	//remainder of #1108's list, and a surface that joins the bar must leave
	//this list (the test fails on a stale entry as well as on a new one).
	public static readonly string[] KnownBarGaps = { "SaveStates", "Enhancements", "ToolSheetAbout", "SettingsDisplay" };

	private const int ClaimsInWiring = 18;

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly InterfaceSize _size = ConfigManager.Config.Preferences.InterfaceSize;
	private readonly List<MainWindow> _windows = new();

	public void Dispose()
	{
		foreach(MainWindow window in _windows) {
			window.ReleaseCore = () => { };
			window.Close();
		}
		Pump();
		_windows.Clear();
		ClearRecents();
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = _uiMode;
		prefs.Workspace = _workspace;
		prefs.ConfirmExitResetPower = _confirm;
		prefs.InterfaceSize = _size;
		ConfigManager.Config.Save();
	}

	[Fact]
	public void Every_surface_is_walked_or_named_as_a_gap()
	{
		Assert.Empty(WalkedSurfaces.Intersect(NotWalkedYet));
		Assert.Empty(KnownBarGaps.Except(WalkedSurfaces));
		Assert.Equal(ClaimsInWiring, WalkedSurfaces.Length - 2 /*Home and HomeFirstRun are the content area, not a claim*/ + NotWalkedYet.Length);
	}

	//---- the live walk

	[AvaloniaTheory]
	[MemberData(nameof(SurfacesAtEveryScale))]
	public void The_pad_walks_every_control_and_back_leaves(string surface, double scale)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.InterfaceSize = scale >= 1.5 ? InterfaceSize.ExtraLarge : InterfaceSize.Standard;
		(MainWindow window, MainWindowViewModel model, Func<bool> isUp, bool isRoot) = Open(surface);

		PadWalkObservation observation = Walk(surface, window, model, isUp, isRoot);
		//A walk that found nothing to reach would pass vacuously.
		Assert.NotEmpty(observation.Interactive);
		List<string> problems = PadWalk.Judge(observation);
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

		//The known gaps are asserted both ways, so a surface that joins the
		//shared bar has to leave the list and one that falls off it cannot hide.
		bool offBar = observation.BarByFocus.All(b => b.Declared is null);
		Assert.True(offBar == KnownBarGaps.Contains(surface),
			offBar ? $"{surface} is not on the shared action bar and is not listed in KnownBarGaps"
				: $"{surface} is on the shared action bar: remove it from KnownBarGaps");
	}

	[AvaloniaFact]
	public void The_wiring_registers_the_claims_the_walk_accounts_for()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, _) = ShowPlay();
		Assert.Equal(ClaimsInWiring, new Arbiter(window).ClaimCount);
	}

	private (MainWindow Window, MainWindowViewModel Model, Func<bool> IsUp, bool IsRoot) Open(string surface)
	{
		if(surface == "Home" || surface == "HomeFirstRun") {
			if(surface == "Home") {
				SeedRecents("Contra", "Zelda", "Metroid");
			} else {
				ClearRecents();
			}
			(MainWindow home, MainWindowViewModel homeModel) = ShowPlay();
			homeModel.RecentGames.Init(GameScreenMode.RecentGames);
			Pump();
			Assert.Equal(surface == "Home", homeModel.RecentGames.ShowRecentsHome);
			return (home, homeModel, () => homeModel.RecentGames.Visible, true);
		}

		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		switch(surface) {
			case "PauseOverlay":
				model.OpenPauseOverlay();
				return (window, model, () => model.IsPlayerOverlayVisible, false);
			case "SaveStates":
				model.OpenPauseOverlay();
				model.OpenSaveStatesSheet();
				return (window, model, () => model.IsSaveStatesSheetVisible, false);
			case "Enhancements":
				model.OpenPauseOverlay();
				model.OpenEnhancementsPanel();
				return (window, model, () => model.IsEnhancementsPanelVisible, false);
			case "Library":
				model.OpenRomPicker();
				return (window, model, () => model.RomPicker.IsVisible, false);
			case "ToolSheetAbout":
				model.ToolSheet.OpenAbout();
				return (window, model, () => model.ToolSheet.IsVisible, false);
			case "SettingsDisplay":
				ConfigViewModel settings = new(ConfigWindowTab.Display, playerMode: true, audioDevices: () => new[] { "Speakers" }, connectedPads: () => 0);
				model.OpenPlayerSettings(settings);
				return (window, model, () => model.IsPlayerSettingsVisible, false);
			default:
				throw new ArgumentException("no opener for " + surface);
		}
	}

	private PadWalkObservation Walk(string surface, MainWindow window, MainWindowViewModel model, Func<bool> isUp, bool isRoot)
	{
		Arbiter focus = new(window);
		WaitFor(() => isUp() && window.FocusManager?.GetFocusedElement() is Control, () => $"{surface} never took the focus");
		Control start = Canonical((Control)window.FocusManager!.GetFocusedElement()!);
		Control root = focus.SearchRoot() ?? RootOf(window, surface, start);

		List<Control> interactive = InteractiveControls(root);
		Dictionary<Control, string> names = interactive.Concat(new[] { start }).Distinct().ToDictionary(c => c, Label);
		HashSet<Control> reached = new() { start };
		Queue<Control> frontier = new(new[] { start });
		List<(string, IReadOnlyList<PlayBarEntry>?)> bar = new();
		Dictionary<Control, bool> seenForBar = new();

		//Edges: with the focus on a node, one pad press per direction. The press
		//is the only thing that moves the ring between nodes; Focus() only sets
		//the node a press is made from, which is where the player would be.
		while(frontier.Count > 0 && reached.Count < 200) {
			Control node = frontier.Dequeue();
			foreach(PadNavAction direction in new[] { PadNavAction.Down, PadNavAction.Right, PadNavAction.Up, PadNavAction.Left }) {
				Land(window, node);
				if(!seenForBar.ContainsKey(node)) {
					seenForBar[node] = true;
					bar.Add((Label(node), focus.Declared()));
				}
				Press(window, direction);
				if(window.FocusManager?.GetFocusedElement() is Control focusedNext && Canonical(focusedNext) is Control next && next != node && root.IsVisualAncestorOf(next) && reached.Add(next)) {
					frontier.Enqueue(next);
					names.TryAdd(next, Label(next));
				}
			}
		}

		HashSet<PlayAction> available = Available(window, root);
		bool? backLeft = null;
		Land(window, start);
		Press(window, PadNavAction.Back);
		Pump();
		backLeft = WaitUntil(() => !isUp());

		return new PadWalkObservation(
			surface, isRoot,
			interactive.Select(c => names[c]).Distinct().ToList(),
			reached.Select(c => names.TryGetValue(c, out string? n) ? n : Label(c)).Distinct().ToList(),
			isRoot ? false : backLeft, bar, available);
	}

	//Types a pad player is expected to be able to land on. Template parts
	//inside a composite (a ComboBox's toggle, a Slider's thumb) and a list row
	//that holds its own buttons are not separate controls.
	//PlayFocusOnOpen is internal to the UI assembly and this project is not a
	//friend of it, so the three reads the walk needs go through reflection: who
	//the arbiter says is up (Declared, SearchRoot) and how many claims it holds.
	private sealed class Arbiter
	{
		private static readonly Type Type = typeof(MainWindow).Assembly.GetType("Mesen.Utilities.PlayFocusOnOpen", true)!;
		private readonly object _instance;

		public Arbiter(MainWindow window)
		{
			_instance = Type.GetMethod("Of", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { window })
				?? throw new InvalidOperationException("the window has no focus arbiter");
		}

		public IReadOnlyList<PlayBarEntry>? Declared() => (IReadOnlyList<PlayBarEntry>?)Type.GetMethod("Declared")!.Invoke(_instance, null);
		public Control? SearchRoot() => (Control?)Type.GetMethod("SearchRoot")!.Invoke(_instance, null);
		public int ClaimCount => ((System.Collections.ICollection)Type.GetField("_claims", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_instance)!).Count;
	}

	private static List<Control> InteractiveControls(Control root)
	{
		return root.GetVisualDescendants().OfType<Control>()
			.Select(Canonical).Distinct().Cast<Control>()
			.Where(c => c is StateGrid or Button or ToggleButton or ComboBox or Slider or TextBox or TabItem or ListBoxItem)
			.Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.IsTabStop)
			.Where(c => !c.GetVisualAncestors().TakeWhile(a => a != root).Any(a => a is ComboBox or Slider or TextBox))
			.Where(c => c is not ListBoxItem item || !item.GetVisualDescendants().OfType<Button>().Any())
			.ToList();
	}

	//The slot / tile grid keeps the D-pad for its own selection (ADR-0256
	//Decision 3), so everything inside it is reached as the grid: one pad press
	//moves the selection, not the focus ring between its tiles.
	private static Control Canonical(Control c)
	{
		return c.FindAncestorOfType<StateGrid>(true) ?? c;
	}

	//The surface's own root when the arbiter names none (the content area).
	private static Control RootOf(MainWindow window, string surface, Control start)
	{
		return window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PlayHomeHost") ?? window;
	}

	//What the surface can really do, read off its live controls. Confirm is any
	//focus on an interactive control; Search and the console row are the
	//library's own named controls; Favorite has no surface yet (#1110 adds one
	//and extends this).
	private static HashSet<PlayAction> Available(MainWindow window, Control root)
	{
		HashSet<PlayAction> available = new() { PlayAction.Confirm, PlayAction.Back };
		bool Visible(string name) => window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name && c.IsEffectivelyVisible);
		if(Visible("RomPickerSearch")) {
			available.Add(PlayAction.Search);
		}
		if(Visible("RomPickerConsoleFilter")) {
			available.Add(PlayAction.ConsoleFilter);
		}
		return available;
	}

	private static string Label(Control c)
	{
		if(!string.IsNullOrEmpty(c.Name)) {
			return c.Name;
		}
		//Unnamed: the type and its place under the nearest named ancestor, which
		//is stable between runs (a hash code would not be) and tells a reader
		//where the control is.
		List<string> path = new();
		for(Visual? v = c; v is not null; v = v.GetVisualParent()) {
			Visual? parent = v.GetVisualParent();
			int index = parent?.GetVisualChildren().ToList().IndexOf(v) ?? 0;
			path.Add(v.GetType().Name + "[" + index + "]");
			if(v != c && v is Control { Name.Length: > 0 } named) {
				path[^1] = named.Name;
				break;
			}
		}
		path.Reverse();
		return string.Join("/", path);
	}

	private static void Land(MainWindow window, Control node)
	{
		WaitFor(() => node.IsEffectivelyVisible && node.Focus(NavigationMethod.Directional), () => $"{Label(node)} never took the focus");
		Pump();
	}

	//---- plumbing shared in shape with PlayPadNavigationTests (kept local so the
	//two classes do not collide; the stand-in backend is the same table)

	private const ushort PadBase = 0x1000;
	private static readonly string[] ButtonNames = { "A", "B", "X", "Y", "L1", "R1", "Start", "Select", "Up", "Down", "Left", "Right" };

	private static string BackendName(ushort keyCode)
	{
		int offset = keyCode - PadBase;
		return offset >= 0 && offset < ButtonNames.Length ? "Pad1 " + ButtonNames[offset] : "";
	}

	private static ushort BackendCode(string name)
	{
		for(int i = 0; i < ButtonNames.Length; i++) {
			if(name == "Pad1 " + ButtonNames[i]) {
				return (ushort)(PadBase + i);
			}
		}
		return 0;
	}

	private PadNavMapping? _mapping;
	private PadNavMapping Mapping => _mapping ??= PadNavControls.Resolve(PadFamily.Xbox, 0, BackendCode)
		?? throw new InvalidOperationException("the stand-in table does not answer the Xbox preset's names");

	private void Press(MainWindow window, PadNavAction action)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { PlayPadNavigation.CodeOf(Mapping, action) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		Pump();
	}

	private (MainWindow Window, MainWindowViewModel Model) ShowPlay()
	{
		PreferencesConfig prefs = ConfigManager.Config.Preferences;
		prefs.UiMode = UiMode.Player;
		prefs.Workspace = Workspace.Play;
		prefs.ConfirmExitResetPower = false;
		//A television-sized window: the headless default (about 512 wide) squeezes
		//the chrome at 1.5 until Continue has no width, which no player sees.
		MainWindow window = new() { Width = 1280, Height = 720 };
		window.ShowStarted();
		_windows.Add(window);
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		WaitFor(() => model.MainMenu.HelpMenuItems.Count > 0, () => "MainWindow never finished building its menus.");
		return (window, model);
	}

	private static bool WaitUntil(Func<bool> condition)
	{
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		while(!condition()) {
			if(clock.ElapsedMilliseconds > 3000) {
				return false;
			}
			Pump();
			System.Threading.Thread.Sleep(20);
		}
		return true;
	}

	private static void WaitFor(Func<bool> condition, Func<string> failure)
	{
		if(!WaitUntil(condition) && !condition()) {
			throw new Xunit.Sdk.XunitException(failure());
		}
	}

	private static void WaitFor(Func<bool> condition, string failure) => WaitFor(condition, () => failure);

	private static void Pump()
	{
		Avalonia.Threading.Dispatcher.UIThread.Post(static () => { }, Avalonia.Threading.DispatcherPriority.Background);
		Avalonia.Threading.Dispatcher.UIThread.RunJobs();
	}

	private static void SeedRecents(params string[] games)
	{
		string folder = ConfigManager.RecentGamesFolder;
		System.IO.Directory.CreateDirectory(folder);
		foreach(string stale in System.IO.Directory.GetFiles(folder, "*.rgd")) {
			System.IO.File.Delete(stale);
		}
		DateTime written = DateTime.Now;
		foreach(string game in games) {
			string file = System.IO.Path.Combine(folder, game + ".rgd");
			System.IO.File.WriteAllText(file, "");
			System.IO.File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
	}

	private static void ClearRecents()
	{
		string folder = ConfigManager.RecentGamesFolder;
		if(!System.IO.Directory.Exists(folder)) {
			return;
		}
		foreach(string file in System.IO.Directory.GetFiles(folder, "*.rgd")) {
			System.IO.File.Delete(file);
		}
	}
}
