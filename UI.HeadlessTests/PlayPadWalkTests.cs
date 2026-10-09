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
//pad player could be stuck, in exactly four ways:
//  - a control the surface shows is not reachable from the pad;
//  - B does not leave a surface that declares (or owes) a Back;
//  - the shared action bar (#1104) names an action the surface does not have;
//  - a pad press moves the focus outside the surface (#1134).
//Controls are tracked by identity, not by Name, and are candidates by type,
//visibility and enabled state, not by Focusable / IsTabStop (#1134).
//
//The judgement is PadWalk.Judge (UI/Logic, pure; its negative cases live in
//UI.Tests/Play/PadWalkJudgeTests). The live cases below only produce the
//observation from a real MainWindow, so they self-skip without the core and
//the evidence of a pass is the filtered class run with MESEN_CORE_LIB set.
//
//Two limits of what the walk proves, stated so nobody over-reads a green run:
//  - Every_surface_is_walked_or_named_as_a_gap only compares this file's own
//    constants; it is not wiring evidence. The wiring pin is
//    The_wiring_registers_the_claims_the_walk_accounts_for, which skips in CI
//    (no core), so the filtered class run with MESEN_CORE_LIB is the evidence.
//  - What the surfaces in NotWalkedYet declare on the shared bar is unknown:
//    whether each is on the bar or off it (#1108's list) is not asserted until
//    the walk opens it, so that list is a gap for #1108 as well as for the walk.
//  - SettingsDisplay is in NotWalkedYet on purpose: its tab strip is not walkable
//    from this harness (Right on a tab does not move the ring and the page is
//    rebuilt under the walk), and the walk used to start on the Home under it.
//
//Scale: every live case is parameterized by the interface size (1.0 = Standard,
//1.5 = Extra large, #1111), so a size can never strand the pad.
[Collection(NativeCoreCollection.Name)]
public class PlayPadWalkTests : IDisposable
{
	//The interface scales a case runs at: 1.0 is Standard, 1.5 is Extra large
	//(#1111). Every walk is parameterized by it, so a size cannot strand the pad.
	private static readonly double[] Scales = { 1.0, 1.5 };

	public static IEnumerable<object[]> SurfacesAtEveryScale =>
		from scale in Scales from surface in WalkedSurfaces select new object[] { surface, scale };

	//Registered claims in PlayPadNavigationWiring.RegisterSurfaces plus the
	//content area, by the surface's name. Walked surfaces have an opener below;
	//a surface here that is not walked is a gap the next ticket closes.
	public static readonly string[] WalkedSurfaces = { "Home", "HomeFirstRun", "PauseOverlay", "SaveStates", "Enhancements", "Library", "ToolSheetAbout" };

	//Registered claims whose opener needs state this harness does not build yet
	//(a loaded pack, a cheat database, a failed load...). Named so the list
	//cannot grow silently: Every_surface_is_walked_or_named_as_a_gap and
	//The_wiring_registers_the_claims_the_walk_accounts_for fail when a claim is
	//added without a row in WalkedSurfaces or here. The Settings tab bug that
	//keeps SettingsDisplay out of the walk is #1133.
	public static readonly string[] NotWalkedYet = {
		"QuitGameConfirm", "SelectRomSheet", "ShaderSheet", "BiosSheet", "ControllerSetup", "SettingsSystemTab",
		"ControllerSheet", "PackDepSheet", "PackPicker", "PackDetail", "Cheats", "Replays",
		"SettingsDisplay",
	};

	//The one ToolSheet claim serves every PlayerToolSheet kind; only About is
	//walked (ToolSheetAbout). The other kinds are named here, and the kind guard
	//below counts them apart from the claims, so a new kind fails it instead of
	//hiding behind ToolSheetAbout.
	public static readonly PlayerToolSheet[] ToolSheetKindsWalked = { PlayerToolSheet.About };
	public static readonly PlayerToolSheet[] ToolSheetKindsNotWalkedYet = {
		PlayerToolSheet.CommandLine, PlayerToolSheet.CheckForUpdates, PlayerToolSheet.VideoRecord, PlayerToolSheet.Barcode,
	};

	//Surfaces that have no declaration on the shared bar yet (Declared() is
	//null). Home and the shared bar's own surfaces are on it; these are the
	//remainder of #1108's list, and a surface that joins the bar must leave
	//this list (the test fails on a stale entry as well as on a new one).
	public static readonly string[] KnownBarGaps = { };

	//Surfaces whose console chips (RomPickerConsoleFilter) the pad cannot land on:
	//LB/RB cycle the selection but the focus never enters the chip ListBox (#1107
	//review finding 2). Named so the gap shows, and asserted both ways: when the
	//action starts entering the chips the walk reaches them and this entry must go.
//The chips stay a named gap tracked in #1134; only the RomPickerConsoleFilter
//items are set apart, so any other unreachable ListBoxItem still fails the walk.
	public static readonly string[] KnownChipGaps = { "Library" };

	//Surfaces where a D-pad press moves focus to header controls outside the
	//PlayHomeHost root. Product focus behavior is unchanged and the root is not
	//widened; the real focus-scope fix is tracked in #1137. Asserted both ways:
	//a listed leak that disappears and any unlisted leak both fail the walk.
	public static readonly Dictionary<string, string[]> KnownFocusLeaks = new() {
		["Home"] = new[] { "ProfileButton", "ToolsMenuButton" },
		["HomeFirstRun"] = new[] { "ProfileButton", "ToolsMenuButton" },
	};


	private const int ClaimsInWiring = 18;

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _confirm = ConfigManager.Config.Preferences.ConfirmExitResetPower;
	private readonly InterfaceSize _size = ConfigManager.Config.Preferences.InterfaceSize;
	private readonly string? _recentFolderOverride = ConfigManager.RecentGamesFolderOverride;
	//Recents and the ROMs they name live in a temp tree of this case's own, so
	//seeding and clearing never touch the maintainer's real RecentGames folder.
	private readonly string _temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "padwalk-1107-" + Guid.NewGuid().ToString("N"));
	private readonly List<MainWindow> _windows = new();

	public PlayPadWalkTests()
	{
		System.IO.Directory.CreateDirectory(RecentFolder);
		ConfigManager.RecentGamesFolderOverride = RecentFolder;
	}

	private string RecentFolder => System.IO.Path.Combine(_temp, "RecentGames");

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
		ConfigManager.RecentGamesFolderOverride = _recentFolderOverride;
		try {
			System.IO.Directory.Delete(_temp, true);
		} catch {
			//A case that failed before it seeded anything leaves nothing to remove.
		}
	}

	[Fact]
	public void Every_surface_is_walked_or_named_as_a_gap()
	{
		Assert.Empty(WalkedSurfaces.Intersect(NotWalkedYet));
		Assert.Empty(KnownBarGaps.Except(WalkedSurfaces));
		Assert.Equal(ClaimsInWiring, WalkedSurfaces.Length - 2 /*Home and HomeFirstRun are the content area, not a claim*/ + NotWalkedYet.Length);
	}

	[Fact]
	public void Every_tool_sheet_kind_is_walked_or_named_as_a_gap()
	{
		PlayerToolSheet[] kinds = Enum.GetValues<PlayerToolSheet>().Where(k => k != PlayerToolSheet.None).ToArray();
		Assert.Empty(ToolSheetKindsWalked.Intersect(ToolSheetKindsNotWalkedYet));
		Assert.Equal(kinds.Length, ToolSheetKindsWalked.Length + ToolSheetKindsNotWalkedYet.Length);
		Assert.Empty(kinds.Except(ToolSheetKindsWalked).Except(ToolSheetKindsNotWalkedYet));
	}

	//---- the live walk

	[AvaloniaTheory]
	[MemberData(nameof(SurfacesAtEveryScale))]
	public void The_pad_walks_every_control_and_back_leaves(string surface, double scale)
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.Preferences.InterfaceSize = scale >= 1.5 ? InterfaceSize.ExtraLarge : InterfaceSize.Standard;
		(MainWindow window, MainWindowViewModel model, Func<bool> isUp, bool isRoot) = Open(surface);

		(PadWalkObservation observation, int chipCount, bool chipsReached) = Walk(surface, window, model, isUp, isRoot);
		//A walk that found nothing to reach would pass vacuously.
		Assert.NotEmpty(observation.Interactive);
		//The chips were taken out of the observation, so Judge sees only the
		//controls the D-pad must reach and no unrelated unreachable ListBoxItem
		//(a ROM row, another list) can be mistaken for the chip gap.
		bool chipGap = chipCount > 0 && !chipsReached;
		Assert.True(chipGap == KnownChipGaps.Contains(surface),
			chipGap ? $"{surface}: the console chips are not reachable and the surface is not listed in KnownChipGaps"
				: $"{surface}: the pad reaches the console chips now: remove it from KnownChipGaps");
		//Judge still reports a leak (PadWalkJudgeTests); the walk only strips the
		//ones named in KnownFocusLeaks after checking the set matches exactly.
		//A leak label reads "<control the press started on> -> <control that took focus>".
		string[] leaks = (observation.FocusOutside ?? Array.Empty<string>()).Select(l => l[(l.LastIndexOf("-> ", StringComparison.Ordinal) + 3)..]).Distinct().ToArray();
		string[] known = KnownFocusLeaks.TryGetValue(surface, out string[]? listed) ? listed : Array.Empty<string>();
		Assert.True(leaks.Except(known).Count() == 0,
			$"{surface}: focus left the surface onto [{string.Join(", ", leaks.Except(known))}], not listed in KnownFocusLeaks (#1137)");
		//Which header button the pad lands on depends on the scale's layout, so a
		//stale entry is caught per surface: a listed surface that no longer leaks.
		Assert.True(known.Length == 0 || leaks.Length > 0,
			$"{surface}: focus no longer leaves the surface: remove it from KnownFocusLeaks (#1137)");
		List<string> problems = PadWalk.Judge(observation with { FocusOutside = null });
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
			default:
				throw new ArgumentException("no opener for " + surface);
		}
	}

	private (PadWalkObservation Observation, int ChipCount, bool ChipsReached) Walk(string surface, MainWindow window, MainWindowViewModel model, Func<bool> isUp, bool isRoot)
	{
		Arbiter focus = new(window);
		//A surface's own claim has to have landed (it names a root) before the walk
		//starts; otherwise the focus is still the content area's and the walk would
		//be of the Home under the surface.
		WaitFor(() => isUp() && window.FocusManager?.GetFocusedElement() is Control && (isRoot || focus.SearchRoot() is not null), () => $"{surface} never took the focus");
		Control start = Canonical((Control)window.FocusManager!.GetFocusedElement()!);
		Control root = focus.SearchRoot() ?? RootOf(window, surface, start);

		List<Control> interactive = InteractiveControls(root);
		//The library's console chips are entered with the ConsoleFilter action, not
		//the D-pad, so they are interactive but are reached only by that press.
		List<Control> chips = ConsoleChips(root);
		interactive.AddRange(chips);
		Dictionary<Control, string> names = interactive.Concat(new[] { start }).Distinct().ToDictionary(c => c, Label);
		HashSet<Control> reached = new() { start };
		Queue<Control> frontier = new(new[] { start });
		List<(string, IReadOnlyList<PlayBarEntry>?, bool)> bar = new();
		List<string> outside = new();
		Dictionary<Control, bool> seenForBar = new();

		//Edges: with the focus on a node, one pad press per direction. The press
		//is the only thing that moves the ring between nodes; Focus() only sets
		//the node a press is made from, which is where the player would be.
		while(frontier.Count > 0 && reached.Count < 200) {
			Control node = frontier.Dequeue();
			//Moving across a tab strip swaps the tab's content for a new tree: a
			//node of the old one is gone and is reached again as its twin.
			if(!node.IsAttachedToVisualTree()) {
				continue;
			}
			foreach(PadNavAction direction in new[] { PadNavAction.Down, PadNavAction.Right, PadNavAction.Up, PadNavAction.Left }) {
				Land(window, node);
				if(!seenForBar.ContainsKey(node)) {
					seenForBar[node] = true;
					bar.Add((Label(node), focus.Declared(), Arbiter.CoverHasFocus(model, node)));
				}
				Press(window, direction);
				if(window.FocusManager?.GetFocusedElement() is Control focusedNext && Canonical(focusedNext) is Control next && next != node) {
					//A press that lands outside the surface is a leak to report, not an edge to follow.
					if(!root.IsVisualAncestorOf(next)) {
						outside.Add($"{Label(node)} -> {Label(next)}");
					} else if(reached.Add(next)) {
						frontier.Enqueue(next);
						names.TryAdd(next, Label(next));
					}
				}
			}
		}

		//The chips are entered with the ConsoleFilter action, not the D-pad: press it
		//once and count them reached only when the focus lands in their ListBox.
		bool chipsReached = false;
		if(chips.Count > 0) {
			Land(window, start);
			PressShoulder(window, "Pad1 R1");
			if(window.FocusManager?.GetFocusedElement() is Control chip && chip.FindAncestorOfType<ListBox>(true)?.Name == "RomPickerConsoleFilter") {
				chipsReached = true;
			}
		}

		HashSet<PlayAction> available = Available(window, root, interactive);
		bool? backLeft = null;
		Land(window, start);
		Press(window, PadNavAction.Back);
		Pump();
		backLeft = WaitUntil(() => !isUp());

		//The chips are judged on their own line by the caller (chipCount, chipsReached).
		return (new PadWalkObservation(
			surface, isRoot,
			interactive.Except(chips).Select(c => new PadWalkControl(c, names[c])).ToList(),
			reached.Select(c => new PadWalkControl(c, names.TryGetValue(c, out string? n) ? n : Label(c))).ToList(),
			isRoot ? false : backLeft, bar, available, outside), chips.Count, chipsReached);
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
		//PlayFavoriteCover is internal too: a cover is what it gives a path for.
		public static bool CoverHasFocus(MainWindowViewModel model, Control focused)
		{
			Type cover = typeof(MainWindow).Assembly.GetType("Mesen.Windows.PlayFavoriteCover", true)!;
			return cover.GetMethod("PathOf", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object?[] { model, focused }) is string;
		}

		public int ClaimCount => ((System.Collections.ICollection)Type.GetField("_claims", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_instance)!).Count;
	}

	private static List<Control> InteractiveControls(Control root)
	{
		return root.GetVisualDescendants().OfType<Control>()
			.Select(Canonical).Distinct().Cast<Control>()
			.Where(c => c is StateGrid or Button or ToggleButton or ComboBox or Slider or TextBox or TabItem or ListBoxItem)
			//Not filtered by Focusable / IsTabStop: a visible, enabled control the ring
				//cannot land on is exactly the pointer-only control the walk must report.
				.Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
			.Where(c => !c.GetVisualAncestors().TakeWhile(a => a != root).Any(a => a is ComboBox or Slider or TextBox))
			.Where(c => c is not ListBoxItem item || !item.GetVisualDescendants().OfType<Button>().Any())
			//The library's console chips are one ListBox the pad enters with the
			//ConsoleFilter action (the bar's own entry), not with the D-pad.
			.Where(c => c is not ListBoxItem || c.FindAncestorOfType<ListBox>()?.Name != "RomPickerConsoleFilter")
			.ToList();
	}

	private static List<Control> ConsoleChips(Control root)
	{
		return root.GetVisualDescendants().OfType<ListBoxItem>()
			.Where(c => c.FindAncestorOfType<ListBox>()?.Name == "RomPickerConsoleFilter" && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
			.Cast<Control>().ToList();
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

	//What the surface can really do, read off its live controls. Confirm needs an
	//interactive control to press; Back needs a surface to leave (the content
	//area is the root); Search and the console row are the library's own named
	//controls, looked up under the surface's root. Favorite is not
	//here: it is per focus (the bar row's cover flag, PlayFavoriteCover.PathOf).
	private static HashSet<PlayAction> Available(MainWindow window, Control root, IReadOnlyCollection<Control> interactive)
	{
		HashSet<PlayAction> available = new();
		if(interactive.Count > 0) {
			available.Add(PlayAction.Confirm);
		}
		if(root != window && root.Name != "PlayHomeHost") {
			available.Add(PlayAction.Back);
		}
		bool Visible(string name) => root.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name && c.IsEffectivelyVisible);
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

	//LB/RB are the ConsoleFilter action; they are not one of the six nav actions.
	private void PressShoulder(MainWindow window, string name)
	{
		PlayPadNavigationWiring.TickForTest(window, Array.Empty<ushort>(), TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
		PlayPadNavigationWiring.TickForTest(window, new ushort[] { BackendCode(name) }, TimeSpan.FromMilliseconds(50), BackendName, BackendCode);
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

	private static void Pump()
	{
		Avalonia.Threading.Dispatcher.UIThread.Post(static () => { }, Avalonia.Threading.DispatcherPriority.Background);
		Avalonia.Threading.Dispatcher.UIThread.RunJobs();
	}

	//Recent-game files as the Core writes them, down to the RomInfo.txt that names
	//a ROM that exists on disk, so a Home cover resolves to a library path (the
	//Favorite action's precondition, PlayFavoriteCover.PathOf).
	private void SeedRecents(params string[] games)
	{
		string folder = RecentFolder;
		System.IO.Directory.CreateDirectory(folder);
		foreach(string stale in System.IO.Directory.GetFiles(folder, "*.rgd")) {
			System.IO.File.Delete(stale);
		}
		string roms = System.IO.Path.Combine(_temp, "roms");
		System.IO.Directory.CreateDirectory(roms);
		DateTime written = DateTime.Now;
		foreach(string game in games) {
			string rom = System.IO.Path.Combine(roms, game + ".nes");
			System.IO.File.WriteAllText(rom, "rom");
			string file = System.IO.Path.Combine(folder, game + ".rgd");
			using(System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Create)) {
				using System.IO.StreamWriter writer = new(zip.CreateEntry("RomInfo.txt").Open());
				writer.Write(game + "\n" + rom + "\x1\n");
			}
			System.IO.File.SetLastWriteTime(file, written);
			written = written.AddMinutes(-1);
		}
	}

	private void ClearRecents()
	{
		string folder = RecentFolder;
		if(!System.IO.Directory.Exists(folder)) {
			return;
		}
		foreach(string file in System.IO.Directory.GetFiles(folder, "*.rgd")) {
			System.IO.File.Delete(file);
		}
	}
}
