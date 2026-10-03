using System;
using System.Collections.Generic;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//ADR-0250: every menu entry has one place per door. The host-free rule
	//(UI/Logic/WorkspaceMenu.cs) maps the door, the platform and the loaded
	//game's capabilities to the entries each surface shows: one case per cell
	//of the ADR's table, the console-capability cases, the Classic dedup, and
	//the two guards (an action with no door, an action twice in one door).
	public class WorkspaceMenuTests
	{
		private static readonly Workspace[] TaskDoors = { Workspace.Play, Workspace.Remaster, Workspace.Share };
		private static readonly Workspace[] Doors = { Workspace.Play, Workspace.Remaster, Workspace.Share, Workspace.Classic };

		private static MenuEntry[] Flat(Workspace door, bool isMacOS, GameCapabilities game)
		{
			return WorkspaceMenu.ToolsMenu(door, isMacOS, game).SelectMany(g => g).ToArray();
		}

		//Decision 3, Play's row: Reset · Power Cycle · Screenshot · Fullscreen,
		//then the tail. W-S2 is the macOS render: the tail keeps only Help.
		[Fact]
		public void Play_tools_on_macOS_match_W_S2()
		{
			IReadOnlyList<IReadOnlyList<MenuEntry>> groups = WorkspaceMenu.ToolsMenu(Workspace.Play, true, GameCapabilities.None);
			Assert.Equal(3, groups.Count);
			Assert.Equal(new[] { MenuEntry.Reset, MenuEntry.PowerCycle }, groups[0]);
			Assert.Equal(new[] { MenuEntry.Screenshot, MenuEntry.Fullscreen }, groups[1]);
			Assert.Equal(new[] { MenuEntry.Help }, groups[2]);
		}

		[Fact]
		public void Play_tools_elsewhere_end_with_the_whole_tail()
		{
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.Screenshot, MenuEntry.Fullscreen, MenuEntry.Settings, MenuEntry.Help, MenuEntry.About, MenuEntry.Exit },
				Flat(Workspace.Play, false, GameCapabilities.None)
			);
		}

		[Fact]
		public void Remaster_tools()
		{
			Assert.Equal(
				new[] { MenuEntry.ReloadPackImages, MenuEntry.MusicRecorder, MenuEntry.EnhancementPacks, MenuEntry.LogWindow, MenuEntry.Help },
				Flat(Workspace.Remaster, true, GameCapabilities.All)
			);
			Assert.Equal(
				new[] { MenuEntry.ReloadPackImages, MenuEntry.MusicRecorder, MenuEntry.EnhancementPacks, MenuEntry.LogWindow, MenuEntry.Settings, MenuEntry.Help, MenuEntry.About, MenuEntry.Exit },
				Flat(Workspace.Remaster, false, GameCapabilities.All)
			);
		}

		[Fact]
		public void Share_tools()
		{
			Assert.Equal(
				new[] { MenuEntry.MoviePlay, MenuEntry.Record, MenuEntry.NetPlay, MenuEntry.Screenshot, MenuEntry.Help },
				Flat(Workspace.Share, true, GameCapabilities.All)
			);
			Assert.Equal(
				new[] { MenuEntry.MoviePlay, MenuEntry.Record, MenuEntry.NetPlay, MenuEntry.Screenshot, MenuEntry.Settings, MenuEntry.Help, MenuEntry.About, MenuEntry.Exit },
				Flat(Workspace.Share, false, GameCapabilities.All)
			);
		}

		//Classic is a door with its own menu bar: it has no Tools ⋯ at all, and
		//no task door lists it as an entry (it appears only in the switcher).
		[Fact]
		public void Classic_has_no_tools_menu_and_no_task_door_names_it()
		{
			Assert.Empty(WorkspaceMenu.ToolsMenu(Workspace.Classic, true, GameCapabilities.All));
			Assert.Empty(WorkspaceMenu.ToolsMenu(Workspace.Classic, false, GameCapabilities.All));
			foreach(Workspace door in TaskDoors) {
				foreach(bool mac in new[] { true, false }) {
					MenuEntry[] tools = Flat(door, mac, GameCapabilities.All);
					Assert.DoesNotContain(MenuEntry.SwitchToPlay, tools);
					Assert.DoesNotContain(MenuEntry.SwitchToRemaster, tools);
					Assert.DoesNotContain(MenuEntry.SwitchToShare, tools);
				}
			}
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public void The_shared_tail_lives_in_the_app_menu_on_macOS_and_ends_the_menu_elsewhere(bool isMacOS)
		{
			foreach(Workspace door in TaskDoors) {
				Assert.Equal(new[] { MenuEntry.About, MenuEntry.Settings, MenuEntry.Exit }, WorkspaceMenu.AppMenu(door, isMacOS));
				IReadOnlyList<MenuEntry> last = WorkspaceMenu.ToolsMenu(door, isMacOS, GameCapabilities.None).Last();
				Assert.Equal(isMacOS ? new[] { MenuEntry.Help } : new[] { MenuEntry.Settings, MenuEntry.Help, MenuEntry.About, MenuEntry.Exit }, last);
			}
		}

		//The console items of Play's ⋯ show only when the loaded game uses them.
		[Fact]
		public void Play_shows_no_console_item_for_a_game_that_uses_none()
		{
			MenuEntry[] tools = Flat(Workspace.Play, true, GameCapabilities.None);
			foreach(MenuEntry console in new[] { MenuEntry.FdsSelectDisk, MenuEntry.FdsEjectDisk, MenuEntry.InsertCoin1, MenuEntry.InsertCoin2, MenuEntry.InsertCoin3, MenuEntry.InsertCoin4, MenuEntry.InputBarcode, MenuEntry.TapeRecorder }) {
				Assert.DoesNotContain(console, tools);
			}
		}

		[Fact]
		public void Play_shows_the_disk_items_for_an_FDS_game()
		{
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.FdsSelectDisk, MenuEntry.FdsEjectDisk },
				WorkspaceMenu.ToolsMenu(Workspace.Play, true, new GameCapabilities(FdsDisk: true))[0]
			);
		}

		[Fact]
		public void Play_shows_two_coins_for_VS_and_four_for_VS_dual_system()
		{
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.InsertCoin1, MenuEntry.InsertCoin2 },
				WorkspaceMenu.ToolsMenu(Workspace.Play, true, new GameCapabilities(VsSystem: true))[0]
			);
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.InsertCoin1, MenuEntry.InsertCoin2, MenuEntry.InsertCoin3, MenuEntry.InsertCoin4 },
				WorkspaceMenu.ToolsMenu(Workspace.Play, true, new GameCapabilities(VsSystem: true, VsDualSystem: true))[0]
			);
		}

		[Fact]
		public void Play_shows_barcode_and_tape_when_the_game_takes_them()
		{
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.InputBarcode },
				WorkspaceMenu.ToolsMenu(Workspace.Play, true, new GameCapabilities(Barcode: true))[0]
			);
			Assert.Equal(
				new[] { MenuEntry.Reset, MenuEntry.PowerCycle, MenuEntry.TapeRecorder },
				WorkspaceMenu.ToolsMenu(Workspace.Play, true, new GameCapabilities(Tape: true))[0]
			);
		}

		[Fact]
		public void Console_capabilities_never_change_Remaster_or_Share()
		{
			foreach(Workspace door in new[] { Workspace.Remaster, Workspace.Share }) {
				Assert.Equal(Flat(door, true, GameCapabilities.None), Flat(door, true, GameCapabilities.All));
			}
		}

		//Decision 3's "lives elsewhere in the door" column: the door's own
		//screens hold these, so its ⋯ does not.
		[Theory]
		[InlineData(Workspace.Play, MenuSurface.Home, new[] { MenuEntry.OpenRom, MenuEntry.RecentFiles })]
		[InlineData(Workspace.Play, MenuSurface.PauseOverlay, new[] { MenuEntry.PauseResume, MenuEntry.SaveStates, MenuEntry.Pack, MenuEntry.Enhancements, MenuEntry.Cheats, MenuEntry.Settings, MenuEntry.QuitGame })]
		[InlineData(Workspace.Remaster, MenuSurface.ProjectChip, new[] { MenuEntry.ShowProjectFolder, MenuEntry.SwitchProject, MenuEntry.ComposeScene })]
		[InlineData(Workspace.Remaster, MenuSurface.Home, new[] { MenuEntry.RemasterRecord, MenuEntry.RemasterBuild })]
		[InlineData(Workspace.Share, MenuSurface.Home, new[] { MenuEntry.RecordAndShare, MenuEntry.SharePack })]
		public void The_door_screens_hold_the_rest(Workspace door, MenuSurface surface, MenuEntry[] expected)
		{
			Assert.Equal(expected, WorkspaceMenu.Placements(door, true, GameCapabilities.All).Where(p => p.Surface == surface).Select(p => p.Entry).ToArray());
			foreach(bool mac in new[] { true, false }) {
				MenuEntry[] tools = Flat(door, mac, GameCapabilities.All);
				foreach(MenuEntry entry in expected.Where(e => e != MenuEntry.Settings)) {
					Assert.DoesNotContain(entry, tools);
				}
			}
		}

		//Decision 4: Classic keeps everything except its duplicates.
		[Fact]
		public void Classic_drops_record_and_share_and_install_hd_pack()
		{
			foreach(bool mac in new[] { true, false }) {
				MenuEntry[] classic = WorkspaceMenu.ClassicMenuBar(mac).SelectMany(m => m.Entries).ToArray();
				Assert.DoesNotContain(MenuEntry.RecordAndShare, classic);
				Assert.False(WorkspaceMenu.ShowsInClassic(MenuEntry.RecordAndShare, mac));
				Assert.Contains(MenuEntry.MoviePlay, classic);
				Assert.Contains(MenuEntry.MovieRecord, classic);
				Assert.Contains(MenuEntry.MovieStop, classic);
				Assert.Contains(MenuEntry.EnhancementPacks, classic);
				Assert.False(Enum.IsDefined(typeof(MenuEntry), "InstallHdPack"));
			}
		}

		[Fact]
		public void Classic_on_macOS_moves_about_preferences_and_exit_to_the_app_menu()
		{
			MenuEntry[] mac = WorkspaceMenu.ClassicMenuBar(true).SelectMany(m => m.Entries).ToArray();
			MenuEntry[] other = WorkspaceMenu.ClassicMenuBar(false).SelectMany(m => m.Entries).ToArray();
			foreach(MenuEntry moved in new[] { MenuEntry.About, MenuEntry.Preferences, MenuEntry.Exit }) {
				Assert.DoesNotContain(moved, mac);
				Assert.False(WorkspaceMenu.ShowsInClassic(moved, true));
				Assert.Contains(moved, other);
				Assert.True(WorkspaceMenu.ShowsInClassic(moved, false));
			}
			Assert.Equal(new[] { MenuEntry.About, MenuEntry.Preferences, MenuEntry.Exit }, WorkspaceMenu.AppMenu(Workspace.Classic, true));
			Assert.Empty(WorkspaceMenu.AppMenu(Workspace.Classic, false));
		}

		[Fact]
		public void Classic_menu_bar_order_ends_with_the_workspace_menu()
		{
			Assert.Equal(
				new[] { ClassicMenu.File, ClassicMenu.Game, ClassicMenu.Options, ClassicMenu.Tools, ClassicMenu.Debug, ClassicMenu.Help, ClassicMenu.Workspace },
				WorkspaceMenu.ClassicMenuBar(false).Select(m => m.Menu).ToArray()
			);
			Assert.Equal(
				new[] { MenuEntry.SwitchToPlay, MenuEntry.SwitchToRemaster, MenuEntry.SwitchToShare },
				WorkspaceMenu.ClassicMenuBar(true).Single(m => m.Menu == ClassicMenu.Workspace).Entries
			);
		}

		[Fact]
		public void Classic_has_one_pause_entry_and_no_dead_help_entries()
		{
			MenuEntry[] game = WorkspaceMenu.ClassicMenuBar(false).Single(m => m.Menu == ClassicMenu.Game).Entries.ToArray();
			Assert.Single(game, e => e == MenuEntry.PauseResume);
			Assert.Equal(new[] { MenuEntry.CommandLineHelp, MenuEntry.CheckForUpdates, MenuEntry.About }, WorkspaceMenu.ClassicMenuBar(false).Single(m => m.Menu == ClassicMenu.Help).Entries);
			Assert.False(Enum.IsDefined(typeof(MenuEntry), "OnlineHelp"));
			Assert.False(Enum.IsDefined(typeof(MenuEntry), "ReportBug"));
		}

		[Fact]
		public void Super_Game_Boy_viewers_are_labelled_Game_Boy()
		{
			Assert.Equal("Game Boy", WorkspaceMenu.SuperGameBoyViewerHint);
		}

		//Guard 1 (Decision 5): an action that belongs to no door fails here.
		[Fact]
		public void Every_menu_action_belongs_to_a_door()
		{
			HashSet<MenuEntry> placed = new();
			foreach(Workspace door in Doors) {
				foreach(bool mac in new[] { true, false }) {
					foreach(MenuPlacement p in WorkspaceMenu.Placements(door, mac, GameCapabilities.All)) {
						placed.Add(p.Entry);
					}
				}
			}
			MenuEntry[] orphans = Enum.GetValues<MenuEntry>().Where(e => !placed.Contains(e)).ToArray();
			Assert.True(orphans.Length == 0, "Entries with no door: " + string.Join(", ", orphans));
		}

		//Guard 2 (Decision 1): one door never lists the same action twice
		//across its surfaces - Tools ⋯ or Classic's bar, the pause overlay,
		//the project chip and the home. The shared tail (About, Settings…,
		//Quit) is the app menu's, which is not one of the door's surfaces: on
		//macOS it is the system menu, elsewhere it ends Tools ⋯ in its place.
		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public void No_door_lists_an_action_twice(bool isMacOS)
		{
			foreach(Workspace door in Doors) {
				foreach(GameCapabilities game in new[] { GameCapabilities.None, GameCapabilities.All }) {
					MenuEntry[] entries = WorkspaceMenu.Placements(door, isMacOS, game).Where(p => p.Surface != MenuSurface.AppMenu).Select(p => p.Entry).ToArray();
					MenuEntry[] twice = entries.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
					Assert.True(twice.Length == 0, door + " lists twice: " + string.Join(", ", twice));
					MenuEntry[] app = WorkspaceMenu.AppMenu(door, isMacOS).ToArray();
					Assert.Equal(app.Length, app.Distinct().Count());
				}
			}
		}

		[Fact]
		public void Placements_tag_the_tail_as_app_menu_and_the_rest_by_surface()
		{
			MenuPlacement[] play = WorkspaceMenu.Placements(Workspace.Play, false, GameCapabilities.None).ToArray();
			Assert.Contains(new MenuPlacement(MenuSurface.AppMenu, MenuEntry.Settings), play);
			Assert.Contains(new MenuPlacement(MenuSurface.AppMenu, MenuEntry.Exit), play);
			Assert.Contains(new MenuPlacement(MenuSurface.ToolsMenu, MenuEntry.Help), play);
			Assert.Contains(new MenuPlacement(MenuSurface.ToolsMenu, MenuEntry.Reset), play);
			Assert.All(WorkspaceMenu.Placements(Workspace.Classic, false, GameCapabilities.All), p => Assert.Equal(MenuSurface.ClassicMenuBar, p.Surface));
		}
	}

	//ADR-0250 Decision 4: the four Pause/Resume variants of Classic's Game menu
	//become one entry whose label follows the state.
	public class PauseMenuEntryTests
	{
		[Theory]
		//Without "pause in menus": the label follows the emulator.
		[InlineData(false, false, false, false)]
		[InlineData(false, false, true, true)]
		[InlineData(false, true, false, false)]
		//With it, opening the menu auto-paused the game: Pause keeps it paused
		//when the menu closes; Resume (the user had paused) resumes it then.
		[InlineData(true, true, true, false)]
		[InlineData(true, false, true, true)]
		[InlineData(true, false, false, true)]
		public void Label_follows_the_state(bool pauseWhenInMenus, bool autoPaused, bool paused, bool showsResume)
		{
			Assert.Equal(showsResume, PauseMenuEntry.ShowsResume(pauseWhenInMenus, autoPaused, paused));
		}

		[Fact]
		public void Click_toggles_the_auto_pause_only_when_menus_pause_the_game()
		{
			Assert.True(PauseMenuEntry.TogglesAutoPause(pauseWhenInMenus: true));
			Assert.False(PauseMenuEntry.TogglesAutoPause(pauseWhenInMenus: false));
		}
	}
}
