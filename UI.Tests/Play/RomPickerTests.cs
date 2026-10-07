using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#845 (ADR-0256 Decision 9): the ROM picker's rules, pinned against a fake
	//tree. Every path here is a name the view-model would have read off a disk;
	//nothing in PlayRomPicker touches one, which is what lets the rules be
	//tested without a filesystem, a pad or a window.
	public class RomPickerTests
	{
		private static readonly RomPickerRoot[] Roots = {
			new("Your games", R("/games")),
			new("MesenAI's games folder", R("/home/roms")),
			new("My Book", R("/Volumes/My Book"))
		};

		private static string R(string path) => Path.GetFullPath(path);

		//The console's own name is the caller's in production (it comes from the
		//locale files), so the tests pass their own - the enum's name, which makes
		//a wrong console in an assertion read as the wrong console and not as a
		//missing string.
		private static string Name(RomConsole console) => console.ToString();

		private static string[] Labels(IReadOnlyList<RomPickerRoot> roots) => roots.Select(r => r.Label).ToArray();

		[Fact]
		public void The_roots_lead_with_the_configured_folder_then_the_apps_own_then_the_volumes()
		{
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder", "My Book" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/Volumes/My Book" })));
		}

		//A machine set up on purpose lands on its own folder; the app's own folder
		//is always a root, so a fresh install still has one.
		[Fact]
		public void Without_a_configured_folder_the_apps_own_is_the_first_root()
		{
			Assert.Equal(
				new[] { "MesenAI's games folder", "My Book" },
				Labels(PlayRomPicker.Roots(null, "/home/roms", new[] { "/Volumes/My Book" })));
			Assert.Equal(
				new[] { "MesenAI's games folder" },
				Labels(PlayRomPicker.Roots("  ", "/home/roms", new[] { "" })));
		}

		//A volume that is also the configured folder is one row, not two: the
		//player would otherwise see the same place twice and have to guess.
		[Fact]
		public void A_folder_named_twice_is_one_root()
		{
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/games", "/GAMES/" })));
		}

		//Folders first - they are the way through - then games, each alphabetical
		//and case-insensitive. A file that is not a game is not a row, and neither
		//is anything whose name starts with a dot, at any level.
		[Fact]
		public void Folders_come_first_then_games_and_nothing_else_is_a_row()
		{
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.Rows(
				new[] { R("/games/zeta"), R("/games/Alpha"), R("/games/.git") },
				new[] { R("/games/contra.nes"), R("/games/README.txt"), R("/games/beta.zip"), R("/games/.DS_Store"), R("/games/Down.7z") });

			Assert.Equal(new[] { "Alpha", "zeta", "beta.zip", "contra.nes", "Down.7z" }, rows.Select(r => r.Label).ToArray());
			//A folder row descends; a game row is the pick.
			Assert.Equal(new[] { true, true, false, false, false }, rows.Select(r => r.IsFolder).ToArray());
			//And the path each row carries is the one the view-model read.
			Assert.Equal(R("/games/Alpha"), rows[0].Path);
			Assert.Equal(R("/games/contra.nes"), rows[3].Path);
		}

		//The ROM extensions FolderHelper has always carried, plus the two archive
		//kinds everything downstream already unpacks.
		//A regression pin, not a RED: RomFileKinds came before this slice and this
		//case existed to keep the picker's idea of "openable" on it. It is repeated
		//here because the picker reads it on every row.
		[Theory]
		[InlineData("Contra.nes", true)]
		[InlineData("Tetris.GB", true)]
		[InlineData("Sonic.sms", true)]
		[InlineData("game.gba", true)]
		[InlineData("pack.zip", true)]
		[InlineData("pack.7z", true)]
		[InlineData("notes.txt", false)]
		[InlineData("movie.mmo", false)]
		[InlineData("state.mst", false)]
		[InlineData("noextension", false)]
		public void Only_a_game_or_an_archive_is_openable(string name, bool openable)
		{
			Assert.Equal(openable, RomFileKinds.IsOpenable(name));
		}

		[Fact]
		public void A_root_is_where_up_stops_and_the_picker_goes_back_to_its_first_list()
		{
			Assert.Null(PlayRomPicker.Ascend("/games", Roots));
			Assert.Null(PlayRomPicker.Ascend("/Volumes/My Book", Roots));
			//Inside one, up is one step - and it never leaves the tree on its own.
			Assert.Equal(R("/games/nes"), PlayRomPicker.Ascend("/games/nes/usa", Roots));
			Assert.Equal(R("/games"), PlayRomPicker.Ascend("/games/nes", Roots));
		}

		//The path line names a root by its label, shortens the home folder to `~`,
		//and is empty on the roots list, where there is no folder to name.
		[Fact]
		public void The_path_line_names_the_place()
		{
			Assert.Equal("", PlayRomPicker.PathText(null, Roots, "/home"));
			Assert.Equal("Your games", PlayRomPicker.PathText("/games", Roots, "/home"));
			Assert.Equal("My Book", PlayRomPicker.PathText("/Volumes/My Book", Roots, "/home"));
			Assert.Equal("~/roms/x", PlayRomPicker.PathText("/home/roms/x", Roots, "/home"));
			//Outside the home folder it is the whole path: there is nothing to
			//shorten it to.
			Assert.Equal("/opt/games/nes", PlayRomPicker.PathText("/opt/games/nes", Roots, "/home"));
		}

		//The first list is the roots themselves, and every one of them descends.
		[Fact]
		public void The_first_list_is_the_roots()
		{
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.RootRows(Roots);
			Assert.Equal(new[] { "Your games", "MesenAI's games folder", "My Book" }, rows.Select(r => r.Label).ToArray());
			Assert.All(rows, r => Assert.True(r.IsFolder, r.Label + " is a place, so it descends"));
			Assert.Equal(new[] { R("/games"), R("/home/roms"), R("/Volumes/My Book") }, rows.Select(r => r.Path).ToArray());
		}

		//An empty folder is no rows, which is what the view-model turns into the
		//sentence on the sheet rather than a list with nothing in it.
		[Fact]
		public void A_folder_with_nothing_to_open_has_no_rows()
		{
			Assert.Empty(PlayRomPicker.Rows(new[] { R("/games/.hidden") }, new[] { R("/games/a.txt"), R("/games/.b.nes") }));
		}

		//The review of #845 (Grok 4.6, 2026-10-05): the configured game folder is a
		//string out of settings.json, which a person can edit, and Path.GetFullPath
		//throws on one the platform cannot spell. That throw would leave the press
		//that opened the picker - the pad's Confirm on the home - and the sheet
		//would never appear on the machine this exists for. A path that cannot be
		//spelled is not a root: it is left out, like a blank.
		[Fact]
		public void A_root_the_platform_cannot_spell_is_left_out_instead_of_throwing()
		{
			//The unspellable folder is dropped and the two that can be spelled are
			//not: this is a root left out, not a picker that refuses to open.
			Assert.Equal(new[] { R("/roms"), R("/Volumes/My Book") },
				PlayRomPicker.Roots("bad\0path", "/roms", new[] { "/Volumes/My Book" }).Select(r => r.Folder).ToArray());
			//And a volume the platform cannot spell is not a row either.
			Assert.Equal(new[] { R("/roms") }, PlayRomPicker.Roots(null, "/roms", new[] { "bad\0volume" }).Select(r => r.Folder).ToArray());
		}

		//The user's own pick: a root at the whole computer, LAST of the fixed
		//roots and supplied by the host (both its folder and its label), so a
		//library the scan does not reach is still reachable by hand. Without one
		//the roots are exactly what they always were.
		[Fact]
		public void The_whole_computer_is_the_last_root_when_the_host_offers_one()
		{
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder", "My Book", "This computer" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/Volumes/My Book" }, new RomPickerRoot("This computer", "/"))));
			Assert.Equal(
				new[] { "Your games", "MesenAI's games folder", "My Book" },
				Labels(PlayRomPicker.Roots("/games", "/home/roms", new[] { "/Volumes/My Book" })));
		}

		//The coordinator's correction: a root at `/` must NOT be part of the
		//exclusion set the suggestions dedupe against - every folder is under it,
		//so excluding its subtree would discard every suggestion there is. The
		//excluded set is the specific roots only.
		[Fact]
		public void A_root_at_the_whole_computer_does_not_swallow_every_suggestion()
		{
			IReadOnlyList<RomPickerRoot> roots = PlayRomPicker.Roots(
				null, "/home/roms", Array.Empty<string>(), new RomPickerRoot("This computer", "/"));
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/emulators/lib"), 30),
				new RomPickerHit(R("/home/roms"), 40)
			}, roots);

			//Under the whole computer -> offered; the app's own ROM folder -> not.
			Assert.Equal(new[] { R("/home/emulators/lib") }, suggestions.Select(s => s.Folder).ToArray());
		}

		//The approved layout: the action row is the FIRST item of a folder's list,
		//and it is not content - a confirm acts on content, so the empty line only
		//shows when there is nothing to open.
		[Fact]
		public void The_action_row_leads_a_folder_but_never_counts_as_content()
		{
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.FolderRows(
				R("/games"), null, "Make this my games folder",
				new[] { R("/games/nes") }, new[] { R("/games/contra.nes") });

			Assert.Equal(RomPickerRowKind.Action, rows[0].Kind);
			Assert.Equal(2, PlayRomPicker.ContentCount(rows));
			Assert.False(rows[0].IsFolder);
			Assert.Equal("Make this my games folder", rows[0].Label);
			Assert.Equal(R("/games"), rows[0].Path);
		}

		//The folder that IS the games folder has nothing to make: no action row,
		//no write, no dead control.
		//
		//Both halves are asserted in this one case on purpose. "Already the games
		//folder has no action row" is an assertion a row list with no action rows
		//at all satisfies trivially - including the list an unimplemented action row
		//produces - so on its own it proves nothing. The first half is what makes it
		//evidence: the same call, with a different games folder, DOES emit the row.
		[Fact]
		public void A_folder_that_is_already_the_games_folder_has_no_action_row()
		{
			IReadOnlyList<RomPickerRow> other = PlayRomPicker.FolderRows(
				R("/games"), R("/elsewhere"), "Make this my games folder",
				Array.Empty<string>(), new[] { R("/games/c.nes") });
			Assert.Single(other, r => r.Kind == RomPickerRowKind.Action);
			Assert.Equal(R("/games"), other[0].Path);

			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.FolderRows(
				R("/games"), R("/games"), "Make this my games folder",
				Array.Empty<string>(), new[] { R("/games/c.nes") });

			Assert.DoesNotContain(rows, r => r.Kind == RomPickerRowKind.Action);
			Assert.Equal(1, PlayRomPicker.ContentCount(rows));
		}

		//A folder that contains a better library is not a second choice: the
		//count-first walk keeps `…/roms` and drops `~/VSCodeProjects` above it.
		[Fact]
		public void A_parent_hit_is_dropped_once_a_better_descendant_is_listed()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/a"), 3),
				new RomPickerHit(R("/a/b"), 30),
				new RomPickerHit(R("/c"), 10)
			}, Roots);

			Assert.Equal(new[] { R("/a/b"), R("/c") }, suggestions.Select(s => s.Folder).ToArray());
		}

		//A folder already reachable through a root is not offered again.
		[Fact]
		public void A_hit_inside_a_root_is_not_a_suggestion()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/games"), 5),
				new RomPickerHit(R("/games/nes"), 40),
				new RomPickerHit(R("/d"), 1)
			}, Roots);

			Assert.Equal(new[] { R("/d") }, suggestions.Select(s => s.Folder).ToArray());
		}

		[Fact]
		public void No_more_than_five_suggestions_are_offered()
		{
			RomPickerHit[] hits = Enumerable.Range(0, 8)
				.Select(i => new RomPickerHit(R("/lib" + i), i + 1)).ToArray();

			Assert.Equal(PlayRomPicker.MaxSuggestions, PlayRomPicker.Suggestions(hits, Roots).Count);
		}

		//A suggestion is a place, so Confirm descends into it exactly like a root.
		[Fact]
		public void A_suggestion_row_is_a_folder_that_descends()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(
				new[] { new RomPickerHit(R("/home/lib"), 4) }, Roots);
			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.SuggestionRows(suggestions, "/home", Name);

			Assert.All(rows, r => Assert.Equal(RomPickerRowKind.Folder, r.Kind));
			//A library whose console could not be named: its own name, then `~`.
			Assert.Equal("lib  ·  ~", rows[0].Label);
			Assert.Equal(R("/home/lib"), rows[0].Path);
		}

		//The sheet is 480 px wide and the label is trimmed with CharacterEllipsis,
		//so the segment that tells two libraries apart has to come first. Every
		//library on disk is a folder called `roms` under a folder named after its
		//console, so measured on the requesting machine the four the scan found
		//rendered as labels sharing their first 51 characters - and the console,
		//the only discriminating part, sat right at the ellipsis. The console comes
		//first now, and with it the count.
		[Fact]
		public void A_suggestion_label_leads_with_the_console_and_the_count()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms"), 30, RomConsole.Nes)
			}, Roots);

			IReadOnlyList<RomPickerRow> rows = PlayRomPicker.SuggestionRows(suggestions, "/home", Name);

			Assert.Equal("Nes  ·  30 games  ·  ~/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho", rows[0].Label);
			Assert.Equal(R("/home/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms"), rows[0].Path);
		}

		//The four libraries the requesting machine's own scan found, and the point
		//of the whole change: read one under the other, they no longer begin with
		//the same fifty characters.
		[Fact]
		public void Two_libraries_of_different_consoles_do_not_share_their_leading_text()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/VSCodeProjects/EMULADORES/2. Switch/G3 - Nitendinho/roms"), 30, RomConsole.Nes),
				new RomPickerHit(R("/home/VSCodeProjects/EMULADORES/2. Switch/G3 - MasterSystem/roms"), 10, RomConsole.MasterSystem)
			}, Roots);

			IReadOnlyList<string> labels = PlayRomPicker.SuggestionRows(suggestions, "/home", Name)
				.Select(r => r.Label).ToList();

			Assert.Equal(2, labels.Count);
			Assert.StartsWith("Nes", labels[0], StringComparison.Ordinal);
			Assert.StartsWith("MasterSystem", labels[1], StringComparison.Ordinal);
		}

		//One game is not "1 games": the count is a word, not a number with an `s`
		//glued to it.
		[Fact]
		public void A_single_game_library_says_one_game()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/nes"), 1, RomConsole.Nes)
			}, Roots);

			Assert.Equal("Nes  ·  1 game  ·  ~",
				PlayRomPicker.SuggestionRows(suggestions, "/home", Name)[0].Label);
		}

		//A folder of archives cannot have its console read off the names, so it
		//keeps the older shape: the folder's own name, and no console claimed.
		[Fact]
		public void A_library_whose_console_is_unknown_claims_no_console()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/G3 - Atari 7800/roms"), 10)
			}, Roots);

			Assert.Equal("roms  ·  ~/G3 - Atari 7800",
				PlayRomPicker.SuggestionRows(suggestions, "/home", Name)[0].Label);
		}

		//Outside the home folder there is nothing to shorten to, and the rule is
		//still "what it is, then where it is", not "`~` or nothing".
		[Fact]
		public void A_suggestion_label_outside_the_home_folder_is_the_name_then_the_whole_parent()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/opt/emuladores/roms"), 3)
			}, Roots);

			Assert.Equal("roms  ·  /opt/emuladores",
				PlayRomPicker.SuggestionRows(suggestions, "/home", Name)[0].Label);
		}

		//The walk counts openable files DIRECTLY in a folder, never a dot-name,
		//and never recurses to build the count.
		[Fact]
		public void The_scan_counts_openable_files_directly_in_a_folder_and_ignores_dot_names()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/a")] = (Array.Empty<string>(),
					new[] { R("/a/contra.nes"), R("/a/beta.zip"), R("/a/notes.txt"), R("/a/.hidden.nes") })
			};

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/a"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree), () => TimeSpan.Zero, CancellationToken.None);

			//`.nes` votes and `.zip` does not, so the folder is named NES.
			Assert.Equal(new[] { new RomPickerHit(R("/a"), 2, RomConsole.Nes) }, hits);
		}

		//Depth 5 is measured-load-bearing (the user's library sits at depth 5, and
		//depth 4 misses it), and the folder budget stops a home that is a forest.
		[Fact]
		public void The_scan_stops_at_the_depth_limit_and_the_folder_budget()
		{
			string deep = R("/base/1/2/3/4/5");
			string deeper = R("/base/1/2/3/4/5/6");
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/base")] = (new[] { R("/base/1") }, Array.Empty<string>()),
				[R("/base/1")] = (new[] { R("/base/1/2") }, Array.Empty<string>()),
				[R("/base/1/2")] = (new[] { R("/base/1/2/3") }, Array.Empty<string>()),
				[R("/base/1/2/3")] = (new[] { R("/base/1/2/3/4") }, Array.Empty<string>()),
				[R("/base/1/2/3/4")] = (new[] { deep }, Array.Empty<string>()),
				[deep] = (new[] { deeper }, new[] { R("/base/1/2/3/4/5/contra.nes") }),
				[deeper] = (Array.Empty<string>(), new[] { R("/base/1/2/3/4/5/6/tetris.nes") })
			};

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/base"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree), () => TimeSpan.Zero, CancellationToken.None);

			Assert.Contains(new RomPickerHit(deep, 1, RomConsole.Nes), hits);
			Assert.DoesNotContain(hits, h => h.Folder == deeper);

			//A tree wider than the folder budget stops at the budget.
			Dictionary<string, (string[] Folders, string[] Files)> wide = new() {
				[R("/w")] = (Enumerable.Range(0, 20).Select(i => R("/w/" + i)).ToArray(), Array.Empty<string>())
			};
			int walked = 0;
			RomFolderScan.Run(
				new[] { new ScanBase(R("/w"), 5) },
				new ScanLimits(3, TimeSpan.FromSeconds(5)),
				FakeLister(wide, _ => walked++), () => TimeSpan.Zero, CancellationToken.None);
			Assert.True(walked <= 3, $"the walk entered {walked} folders past a budget of 3");

			//And a scan whose clock has already run out walks nothing at all.
			int timed = 0;
			RomFolderScan.Run(
				new[] { new ScanBase(R("/w"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(1.5)),
				FakeLister(wide, _ => timed++), () => TimeSpan.FromSeconds(2), CancellationToken.None);
			Assert.Equal(0, timed);
		}

		//A folder that cannot be read is simply not a hit, and a symlink that
		//lists itself terminates instead of walking forever.
		[Fact]
		public void The_scan_skips_a_folder_it_cannot_read_and_never_loops_on_a_symlink()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/good")] = (Array.Empty<string>(), new[] { R("/good/contra.nes") })
			};
			int calls = 0;
			FolderLister lister = folder => {
				calls++;
				if(folder == R("/bad")) {
					throw new UnauthorizedAccessException("permission denied");
				}
				if(folder == R("/loop")) {
					return (new[] { R("/loop") }, Array.Empty<string>());
				}
				return tree.TryGetValue(folder, out var e)
					? (e.Folders, e.Files) : (Array.Empty<string>(), Array.Empty<string>());
			};

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/bad"), 5), new ScanBase(R("/loop"), 5), new ScanBase(R("/good"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				lister, () => TimeSpan.Zero, CancellationToken.None);

			Assert.Equal(new[] { new RomPickerHit(R("/good"), 1, RomConsole.Nes) }, hits);
			Assert.True(calls < 20, $"the walk did not terminate promptly ({calls} listings)");
		}

		//The walk never enters a network mount. A server that is gone blocks a
		//directory read in the kernel, where the scan's wall-clock budget cannot
		//interrupt it, so the feature would hang instead of degrading - and the
		//mount table is the host's (RomFolderScanSource reads it), which is why the
		//scan takes the answer as a parameter and this needs no mount to pin.
		[Fact]
		public void A_network_mount_is_never_a_scan_base_and_is_never_entered()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/home")] = (new[] { R("/home/nas"), R("/home/lib") }, Array.Empty<string>()),
				[R("/home/nas")] = (Array.Empty<string>(), new[] { R("/home/nas/contra.nes") }),
				[R("/home/lib")] = (Array.Empty<string>(), new[] { R("/home/lib/tetris.nes") })
			};
			List<string> listed = new();

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				//The dead mount is BOTH a base of its own and a child of the home
				//walk, so both ways in are pinned by the one case.
				new[] { new ScanBase(R("/home/nas"), 5), new ScanBase(R("/home"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree, listed.Add), () => TimeSpan.Zero, CancellationToken.None,
				new[] { R("/home/nas") });

			Assert.DoesNotContain(R("/home/nas"), listed);
			Assert.Equal(new[] { new RomPickerHit(R("/home/lib"), 1, RomConsole.Nes) }, hits);
		}

		//The mount table, in the two shapes the host reads: macOS `mount` output
		//("src on /path (fstype, opts)") and Linux /proc/mounts ("src /path fstype
		//opts 0 0"). Only the network file systems come back.
		[Theory]
		[InlineData("nfs", true)]
		[InlineData("nfs4", true)]
		[InlineData("smbfs", true)]
		[InlineData("afpfs", true)]
		[InlineData("webdav", true)]
		[InlineData("cifs", true)]
		[InlineData("autofs", true)]
		[InlineData("apfs", false)]
		[InlineData("ext4", false)]
		[InlineData("vfat", false)]
		[InlineData("exfat", false)]
		public void A_network_file_system_is_the_one_the_scan_refuses(string fileSystem, bool network)
		{
			Assert.Equal(network, NetworkMounts.IsNetworkFileSystem(fileSystem));
		}

		[Fact]
		public void The_macos_mount_command_says_which_mounts_are_network_ones()
		{
			string table =
				"/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)\n" +
				"//ana@nas.local/roms on /Volumes/roms (smbfs, nodev, nosuid, mounted by ana)\n" +
				"map auto_home on /System/Volumes/Data/home (autofs, automounted, nobrowse)\n" +
				"nas.local:/export/games on /Volumes/games (nfs, nodev, nosuid)\n";

			Assert.Equal(new[] { "/Volumes/roms", "/System/Volumes/Data/home", "/Volumes/games" },
				NetworkMounts.FromTable(table));
		}

		[Fact]
		public void The_linux_proc_mounts_table_says_which_mounts_are_network_ones()
		{
			string table =
				"# a comment\n" +
				"/dev/sda1 / ext4 rw,relatime 0 0\n" +
				"nas:/export/roms /mnt/nas nfs4 rw,relatime 0 0\n" +
				"//nas/share /mnt/win cifs rw 0 0\n" +
				"//nas/share /mnt/my\\040share cifs rw 0 0\n" +
				"/dev/sdb1 /media/My\\040Stick vfat rw 0 0\n";

			//The mount point is unescaped: `/proc/mounts` spells a space as \040,
			//and the answer has to be a path the rest of the app can compare.
			Assert.Equal(new[] { "/mnt/nas", "/mnt/win", "/mnt/my share" }, NetworkMounts.FromTable(table));
		}

		//A build output folder is not a library, whatever it happens to hold:
		//measured on the requesting machine, `…/MesenCE/out/release` and
		//`~/runs-archive/fix-499/runs/499-measure/mint` were both hits.
		[Fact]
		public void A_build_output_folder_is_never_a_hit_and_is_never_entered()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/p")] = (new[] { R("/p/out"), R("/p/runs"), R("/p/runs-archive"), R("/p/roms") }, Array.Empty<string>()),
				[R("/p/out")] = (Array.Empty<string>(), new[] { R("/p/out/a.nes") }),
				[R("/p/runs")] = (Array.Empty<string>(), new[] { R("/p/runs/b.nes") }),
				[R("/p/runs-archive")] = (Array.Empty<string>(), new[] { R("/p/runs-archive/c.nes") }),
				[R("/p/roms")] = (Array.Empty<string>(), new[] { R("/p/roms/d.nes") })
			};
			List<string> listed = new();

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/p"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree, listed.Add), () => TimeSpan.Zero, CancellationToken.None);

			Assert.Equal(new[] { new RomPickerHit(R("/p/roms"), 1, RomConsole.Nes) }, hits);
			Assert.DoesNotContain(R("/p/out"), listed);
			Assert.DoesNotContain(R("/p/runs"), listed);
			Assert.DoesNotContain(R("/p/runs-archive"), listed);
		}

		//The durable rule, and the one that holds off this machine's layout: a
		//Remaster project (ADR-0243) is `<dir>/<Game>/` with `auto/`, `mep/`, `kit/`
		//and the `.bootstrap` stamp. Its ROM is the artist's subject, and the folder
		//is a workspace - so the scan neither offers it nor walks into it.
		[Theory]
		[InlineData("project.json")]
		[InlineData("mep")]
		[InlineData("auto")]
		[InlineData(".bootstrap")]
		public void An_enhancement_project_is_never_a_hit_and_is_never_entered(string marker)
		{
			//`mep/` and `auto/` are folders of the layout; `project.json` and the
			//`.bootstrap` stamp are files in it.
			bool isFolder = marker is "mep" or "auto";
			string mark = R("/lib/Castlevania/" + marker);
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/lib")] = (new[] { R("/lib/Castlevania"), R("/lib/other") }, Array.Empty<string>()),
				[R("/lib/Castlevania")] = (
					isFolder ? new[] { mark } : Array.Empty<string>(),
					isFolder ? new[] { R("/lib/Castlevania/Castlevania.nes") } : new[] { mark, R("/lib/Castlevania/Castlevania.nes") }),
				[R("/lib/other")] = (Array.Empty<string>(), new[] { R("/lib/other/tetris.nes") })
			};
			List<string> listed = new();

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/lib"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree, listed.Add), () => TimeSpan.Zero, CancellationToken.None);

			//The other folder is still a library: this is one folder left out, not a
			//walk that stops at the first project it meets.
			Assert.Equal(new[] { new RomPickerHit(R("/lib/other"), 1, RomConsole.Nes) }, hits);
			//The project folder is READ once - that is how the walk learns what it
			//is - and then not entered: nothing under it is listed at all.
			string under = R("/lib/Castlevania") + Path.DirectorySeparatorChar;
			Assert.DoesNotContain(listed, folder => folder.StartsWith(under, StringComparison.Ordinal));
		}

		//A real library whose folder happens to hold a ROM beside an ordinary
		//subfolder is still a library: the mark is the project layout, not "has
		//subfolders".
		//
		//A regression pin, not a RED: it was written with the project rule to keep
		//that rule from over-reaching, and it passed the moment it was written -
		//which is the point of it, and why it is labelled here rather than counted
		//as evidence for the rule.
		[Fact]
		public void An_ordinary_folder_holding_a_game_is_still_a_library()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/lib")] = (new[] { R("/lib/sub") }, new[] { R("/lib/contra.nes") }),
				[R("/lib/sub")] = (Array.Empty<string>(), Array.Empty<string>())
			};

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/lib"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree), () => TimeSpan.Zero, CancellationToken.None);

			Assert.Equal(new[] { new RomPickerHit(R("/lib"), 1, RomConsole.Nes) }, hits);
		}

		// --- the console a library holds (RomConsoleKinds) --------------------

		//#886 follow-up: every library on disk is a folder named `roms` under a
		//folder named after its console, so the extension is the only thing that
		//says which console a folder is - and it has to be read, not guessed,
		//because a guess offers the player a game that cannot open.
		[Theory]
		[InlineData("contra.nes", RomConsole.Nes)]
		[InlineData("game.unif", RomConsole.Nes)]
		[InlineData("game.unf", RomConsole.Nes)]
		[InlineData("disk.fds", RomConsole.Nes)]
		[InlineData("tetris.gb", RomConsole.GameBoy)]
		[InlineData("kirby.gbx", RomConsole.GameBoy)]
		[InlineData("shantae.gbc", RomConsole.GameBoyColor)]
		[InlineData("metroid.gba", RomConsole.GameBoyAdvance)]
		[InlineData("sonic.sms", RomConsole.MasterSystem)]
		[InlineData("columns.gg", RomConsole.GameGear)]
		//SG-1000, not Master System: the core runs both out of one SmsConsole
		//(Core/SMS/SmsConsole.cpp picks SmsModel::Sg for `.sg`), but the app
		//associates `.sg` with SG-1000 and `.sms` with SMS as two separate
		//settings, and the two libraries are two machines on disk.
		[InlineData("flicky.sg", RomConsole.Sg1000)]
		//An archive, a save, another machine's ROM, and no extension at all.
		[InlineData("contra.zip", RomConsole.Unknown)]
		[InlineData("contra.7z", RomConsole.Unknown)]
		[InlineData("zelda.sav", RomConsole.Unknown)]
		[InlineData("asteroids.a26", RomConsole.Unknown)]
		[InlineData("mario.z64", RomConsole.Unknown)]
		[InlineData("ff7.chd", RomConsole.Unknown)]
		[InlineData("README", RomConsole.Unknown)]
		public void A_files_extension_names_its_console_or_nothing(string name, RomConsole expected)
		{
			Assert.Equal(expected, RomConsoleKinds.OfFile(name));
		}

		//A library that grew a few stray files is still that library: the console
		//is the majority, and the files that name no console do not vote.
		[Fact]
		public void A_folders_console_is_the_majority_of_its_files()
		{
			Assert.Equal(RomConsole.Nes, RomConsoleKinds.OfFiles(new[] {
				"contra.nes", "mario.nes", "zelda.nes", "notes.txt", "backup.zip"
			}));
		}

		//The archive question decided honestly: a folder of zips is NOT claimed as
		//NES, because a zip's console is not knowable from its name. Claiming one
		//is how the Atari 7800 folder on the requesting machine - ten `.zip`/`.7z`
		//files the emulator cannot open - was offered as a library beside three
		//real ones.
		[Fact]
		public void A_folder_of_archives_names_no_console()
		{
			Assert.Equal(RomConsole.Unknown, RomConsoleKinds.OfFiles(new[] {
				"Asteroids (USA).zip", "Centipede (1987) (Atari).7z"
			}));
		}

		//A tie must not depend on the order the host happened to list the files in.
		[Fact]
		public void A_tie_between_two_consoles_answers_the_same_either_way()
		{
			string[] one = { "contra.nes", "tetris.gb" };
			string[] other = { "tetris.gb", "contra.nes" };

			Assert.Equal(RomConsoleKinds.OfFiles(one), RomConsoleKinds.OfFiles(other));
			Assert.Equal(RomConsole.Nes, RomConsoleKinds.OfFiles(one));
		}

		//"Is this a ROM" and "which console is this" are one list: RomFileKinds
		//asks RomConsoleKinds rather than keeping a second copy that drifts.
		[Theory]
		[InlineData("contra.nes", true)]
		[InlineData("contra.zip", false)]
		[InlineData("asteroids.a26", false)]
		public void The_rom_extension_table_has_one_home(string name, bool isRom)
		{
			Assert.Equal(isRom, RomFileKinds.IsRomFile(name));
		}

		//The walk reads the console off the files it already listed, so naming it
		//costs no extra read.
		[Fact]
		public void The_scan_names_the_consoles_console()
		{
			Dictionary<string, (string[] Folders, string[] Files)> tree = new() {
				[R("/lib")] = (Array.Empty<string>(), new[] { R("/lib/contra.nes"), R("/lib/mario.nes") })
			};

			IReadOnlyList<RomPickerHit> hits = RomFolderScan.Run(
				new[] { new ScanBase(R("/lib"), 5) },
				new ScanLimits(200_000, TimeSpan.FromSeconds(5)),
				FakeLister(tree), () => TimeSpan.Zero, CancellationToken.None);

			Assert.Equal(new[] { new RomPickerHit(R("/lib"), 2, RomConsole.Nes) }, hits);
		}

		// --- the suggestions, grouped by console ------------------------------

		//The list reads as a console list: the console with the biggest library
		//leads, and its own rows follow it before the next console starts.
		[Fact]
		public void The_suggestions_are_ordered_by_console()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/sms"), 3, RomConsole.MasterSystem),
				new RomPickerHit(R("/home/nes"), 30, RomConsole.Nes),
				new RomPickerHit(R("/home/gba"), 10, RomConsole.GameBoyAdvance)
			}, Roots);

			Assert.Equal(
				new[] { RomConsole.Nes, RomConsole.GameBoyAdvance, RomConsole.MasterSystem },
				suggestions.Select(s => s.Console).ToArray());
		}

		//One row per console, and the biggest library is the one that stands for
		//it. This is the rule that keeps the cap from being eaten alive: measured
		//on the requesting machine, ranking by count alone gave all five rows to
		//five NES folders and dropped the Master System and Game Boy Advance
		//libraries the player had actually set up.
		[Fact]
		public void One_row_per_console_and_the_biggest_library_stands_for_it()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/nes-small"), 4, RomConsole.Nes),
				new RomPickerHit(R("/home/sms"), 20, RomConsole.MasterSystem),
				new RomPickerHit(R("/home/nes-big"), 30, RomConsole.Nes)
			}, Roots);

			Assert.Equal(
				new[] { R("/home/nes-big"), R("/home/sms") },
				suggestions.Select(s => s.Folder).ToArray());
		}

		//The cap is shared, so the row that stands for a console must not be the
		//one that starves the others: a machine whose only libraries are many NES
		//folders still offers one row per console it has, in console order.
		[Fact]
		public void Many_libraries_of_one_console_never_starve_another_console()
		{
			List<RomPickerHit> hits = Enumerable.Range(0, 8)
				.Select(i => new RomPickerHit(R("/home/nes" + i), 40 - i, RomConsole.Nes))
				.ToList();
			hits.Add(new RomPickerHit(R("/home/gba"), 3, RomConsole.GameBoyAdvance));

			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(hits, Roots);

			Assert.Contains(suggestions, s => s.Console == RomConsole.GameBoyAdvance);
			Assert.Single(suggestions, s => s.Console == RomConsole.Nes);
		}

		//SG-1000 and Master System are one console to the core and two machines to
		//the player, so the one-row rule must not merge them. Merged, a player
		//holding a library of each is offered only the larger one - the other
		//machine's games cannot be reached from the picker at all. Found by
		//adversarial review (grok 2026-10-05, PR #889): `.sg` was mapped to
		//MasterSystem, so this is the case that pins the fix.
		[Fact]
		public void An_sg1000_library_is_not_collapsed_into_the_master_system_one()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/sms"), 40, RomConsole.MasterSystem),
				new RomPickerHit(R("/home/sg1000"), 6, RomConsole.Sg1000)
			}, Roots);

			Assert.Equal(
				new[] { R("/home/sms"), R("/home/sg1000") },
				suggestions.Select(s => s.Folder).ToArray());
			Assert.Equal(
				new[] { RomConsole.MasterSystem, RomConsole.Sg1000 },
				suggestions.Select(s => s.Console).ToArray());
		}

		//A folder whose console could not be named is not a console, so it cannot
		//be collapsed into one: two of them are two places the player may mean.
		[Fact]
		public void Two_folders_with_no_known_console_are_two_rows()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/atari"), 10),
				new RomPickerHit(R("/home/switch"), 4)
			}, Roots);

			Assert.Equal(2, suggestions.Count);
		}

		//A folder whose console could not be named is the one the picker is least
		//sure about, so it goes after every console - never displacing a library
		//whose console it does know.
		[Fact]
		public void A_library_with_no_known_console_goes_last()
		{
			IReadOnlyList<RomPickerSuggestion> suggestions = PlayRomPicker.Suggestions(new[] {
				new RomPickerHit(R("/home/atari"), 10),
				new RomPickerHit(R("/home/nes"), 4, RomConsole.Nes)
			}, Roots);

			Assert.Equal(
				new[] { R("/home/nes"), R("/home/atari") },
				suggestions.Select(s => s.Folder).ToArray());
		}

		//#1060 (ADR-0264 Decision 8): a library folder that answers no games is a
		//named state with a next step rather than a blank grid - and it is a
		//DIFFERENT state from having no library folder at all, because the two
		//tell the player different things. The rule is the decision table, and the
		//ids it answers are the ones the locale file must hold.
		[Fact]
		public void A_library_folder_that_yields_no_games_is_its_own_named_state()
		{
			Assert.Equal("RomPickerLibraryNoFolders", PlayRomPicker.LibraryEmptyMessageId(0, 0));
			Assert.Equal("RomPickerLibraryEmpty", PlayRomPicker.LibraryEmptyMessageId(1, 0));
			Assert.Equal("RomPickerLibraryEmpty", PlayRomPicker.LibraryEmptyMessageId(4, 0));
			Assert.Null(PlayRomPicker.LibraryEmptyMessageId(1, 1));
			Assert.Null(PlayRomPicker.LibraryEmptyMessageId(4, 250));
		}

		//#1060 review finding 2: the empty sentence belongs to a scan that ANSWERED
		//nothing, never to a library that has not been scanned yet. `gamesFound` is
		//nullable for exactly that reason - null is "no scan has answered" - and a
		//library WITH folders then has nothing to say: borrowing the sentence for a
		//scan that came back empty would put "No games found in your library folder."
		//next to "Looking for your games…" for the whole scan, which is a false fact
		//on a slow or large drive.
		[Fact]
		public void A_library_that_has_not_been_scanned_yet_claims_nothing_about_its_games()
		{
			Assert.Null(PlayRomPicker.LibraryEmptyMessageId(1, null));
			Assert.Null(PlayRomPicker.LibraryEmptyMessageId(4, null));
			//The missing-folder state is known BEFORE any scan - it is the folder
			//list's own answer, not the scan's - so it still speaks.
			Assert.Equal("RomPickerLibraryNoFolders", PlayRomPicker.LibraryEmptyMessageId(0, null));
		}

		private static FolderLister FakeLister(Dictionary<string, (string[] Folders, string[] Files)> tree, Action<string>? onCall = null)
		{
			return folder => {
				onCall?.Invoke(folder);
				return tree.TryGetValue(folder, out var e)
					? (e.Folders, e.Files) : (Array.Empty<string>(), Array.Empty<string>());
			};
		}
	}
}
