using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//#1037 (ADR-0264 Decision 9): the library scan is BOUNDED, BACKGROUNDED and
	//VISIBLE, and the visible half of that is streaming - the entries reach the
	//grid as each listing finds them instead of arriving all at once when the
	//last folder answers. What is pinned here is that the stream is a stream
	//(reports while the walk is still running, reports every entry exactly once)
	//and that the two caps the ADR names bound a streamed walk exactly as they
	//bound a collected one.
	//
	//Host-free: the folder listing comes in as the FolderLister seam, so nesting,
	//the depth cap and the count cap are decided here without a disk, a window or
	//a thread (ADR-0123).
	public class PlayerLibraryScanTests
	{
		private static string R(string path) => Path.GetFullPath(path);

		//One fake tree, listed the way a host lists a real one: a folder's
		//subfolders and its files, every ancestor a folder, and nothing for a
		//folder nobody named (a volume pulled out between listing and
		//descending). `Listed` records what the walk asked for, which is how a
		//case can tell a stream from a result.
		private static (FolderLister List, List<string> Listed) Tree(params string[] filePaths)
		{
			Dictionary<string, List<string>> folders = new(StringComparer.Ordinal);
			Dictionary<string, List<string>> files = new(StringComparer.Ordinal);
			foreach(string path in filePaths) {
				string full = R(path);
				string folder = Path.GetDirectoryName(full)!;
				folders.TryAdd(folder, new List<string>());
				if(!files.TryGetValue(folder, out List<string>? own)) {
					own = files[folder] = new List<string>();
				}
				own.Add(full);

				string child = folder;
				for(string? parent = Path.GetDirectoryName(folder); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)) {
					if(!folders.TryGetValue(parent, out List<string>? children)) {
						children = folders[parent] = new List<string>();
					}
					if(!children.Contains(child)) {
						children.Add(child);
					}
					files.TryAdd(parent, new List<string>());
					child = parent;
				}
			}

			List<string> listed = new();
			FolderLister list = folder => {
				listed.Add(folder);
				return (
					folders.TryGetValue(folder, out List<string>? sub) ? sub : new List<string>(),
					files.TryGetValue(folder, out List<string>? own) ? own : new List<string>());
			};
			return (list, listed);
		}

		private static string[] Paths(IEnumerable<LibraryEntry> entries) => entries.Select(e => e.Path).ToArray();

		//A scan that answered only at the end would leave the grid still for the
		//whole walk, which is the wait Decision 9 exists to remove. The proof is
		//an interleaving: the first batch is handed over before the walk has
		//asked about every folder it is going to ask about.
		[Fact]
		public void Entries_are_reported_while_the_walk_is_still_running()
		{
			(FolderLister list, List<string> listed) = Tree("/lib/NES/Contra.nes", "/lib/GB/Tetris.gb");
			List<string> streamed = new();
			List<int> listingsAtReport = new();

			GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => {
				listingsAtReport.Add(listed.Count);
				streamed.AddRange(Paths(batch));
			});

			//Two folders hold a game, so the walk reports twice - and the first
			//report lands while folders are still to be listed. (A whole-result
			//scan reports once, after the last one.)
			Assert.Equal(2, streamed.Count);
			Assert.Equal(2, listingsAtReport.Count);
			Assert.True(listingsAtReport[0] < listed.Count,
				$"the first batch was handed over only after all {listed.Count} listings, which is not a stream");
		}

		//The batches ARE the scan: whatever is streamed is exactly what the module
		//returns, once each. Batching can change when a tile appears; it can never
		//lose one or show it twice.
		[Fact]
		public void Every_entry_is_streamed_exactly_once_and_the_batches_are_the_result()
		{
			(FolderLister list, _) = Tree(
				"/lib/NES/Contra.nes", "/lib/NES/Metroid.nes", "/lib/GB/Tetris.gb", "/lib/Hacks/2024/Zelda Redux.gb");
			List<string> streamed = new();
			int batches = 0;

			LibraryScanResult result = GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => {
				batches++;
				streamed.AddRange(Paths(batch));
			});

			Assert.True(batches > 1, $"the whole scan arrived in {batches} batch(es), which is not a stream");
			Assert.Equal(
				Paths(result.Entries).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
				streamed.OrderBy(p => p, StringComparer.Ordinal).ToArray());
		}

		//A batch with nothing in it is a repaint of a grid that did not change, so
		//a folder that found no game reports nothing at all.
		[Fact]
		public void A_folder_that_found_no_game_reports_no_batch()
		{
			(FolderLister list, _) = Tree("/lib/notes/readme.txt", "/lib/NES/Contra.nes");
			List<int> sizes = new();

			GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => sizes.Add(batch.Count));

			Assert.Equal(new[] { 1 }, sizes);
		}

		//MaxDepth is six levels BELOW a library folder, and a streamed walk is the
		//same walk: the folder at the seventh level is never listed and its game
		//is never reported.
		[Fact]
		public void The_depth_cap_bounds_what_is_streamed()
		{
			(FolderLister list, _) = Tree("/lib/a/b/c/d/e/f/six.nes", "/lib/a/b/c/d/e/f/g/seven.nes");
			List<string> streamed = new();

			LibraryScanResult result = GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => streamed.AddRange(Paths(batch)));

			Assert.Equal(new[] { R("/lib/a/b/c/d/e/f/six.nes") }, streamed);
			Assert.Equal(new[] { R("/lib/a/b/c/d/e/f/six.nes") }, Paths(result.Entries));
		}

		//The count cap is checked before the add, so a capped stream holds exactly
		//the cap and not one tile more - and the result says it stopped, which is
		//what the header shows (Decision 9: say so rather than silently truncate).
		[Fact]
		public void The_entry_cap_bounds_the_stream_and_says_so()
		{
			string[] many = Enumerable.Range(0, GameLibrary.MaxEntries + 5)
				.Select(i => "/lib/games/Game " + i.ToString("D5") + ".nes")
				.ToArray();
			(FolderLister list, _) = Tree(many);
			List<string> streamed = new();

			LibraryScanResult result = GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => streamed.AddRange(Paths(batch)));

			Assert.Equal(GameLibrary.MaxEntries, streamed.Count);
			Assert.Equal(GameLibrary.MaxEntries, result.Entries.Count);
			Assert.True(result.Truncated, "a scan that stopped at the cap did not say so");
		}

		//The two constants ARE the decision (ADR-0264 Decision 9 names them, so
		//that they are the decision rather than a detail of one implementation).
		//Pinned against the ADR's own numbers: changing one is an amendment to the
		//ADR, not an edit to a scan.
		[Fact]
		public void The_caps_are_the_numbers_ADR_0264_names()
		{
			Assert.Equal(6, GameLibrary.MaxDepth);
			Assert.Equal(20000, GameLibrary.MaxEntries);
		}

		//A grid that places each streamed entry with the module's own comparator
		//ends in exactly the order the module's final list has - which is why the
		//streaming caller asks instead of restating the rule, and why a grid that
		//fills this way never has to reorder itself when the scan ends.
		[Fact]
		public void Merging_the_batches_with_the_comparator_gives_the_results_order()
		{
			(FolderLister list, _) = Tree("/lib/NES/Zelda.nes", "/lib/NES/Contra.nes", "/lib/GB/Metroid.gb");
			List<LibraryEntry> streamed = new();

			LibraryScanResult result = GameLibrary.ScanStreaming(new[] { "/lib" }, list, batch => streamed.AddRange(batch));

			streamed.Sort(GameLibrary.Compare);
			Assert.Equal(Paths(result.Entries), Paths(streamed));
		}
	}
}
