using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//P.12 (ADR-0245 §4, second bullet; #924): W-P11's Look Online for a copy not
//in the bundled list. The rules (only a passed check is a row, the label, the
//toggle into the shared list) are pinned host-free in
//UI.Tests/Cheats/CheatWebLookupTests; this checks the crossing into XAML with
//a fake checker standing in for scripts/cheat_web_lookup.py, whose check fails
//closed on real data today (#934).
//
//Core-free: the sheet view is hosted on its own, never MainWindow, and nothing
//here reaches EmuApi.
public class PlayerCheatsWebCodesTests
{
	private const string Sha1 = "0123456789ABCDEF0123456789ABCDEF01234567";

	private sealed class FakeChecker : ICheatWebChecker
	{
		public int Runs { get; private set; }
		public string RomPath { get; private set; } = "";

		public Task<IReadOnlyList<WebFoundCode>> LookUpAsync(string romPath, string gameName)
		{
			Runs++;
			RomPath = romPath;
			return Task.FromResult<IReadOnlyList<WebFoundCode>>(new[] {
				new WebFoundCode("Infinite lives", "0032:09", WebCheckState.Passed),
				new WebFoundCode("Max power", "0040:FF", WebCheckState.Failed),
				new WebFoundCode("Moon jump", "0050:01", WebCheckState.Unchecked),
			});
		}
	}

	//The real checker's run can fail to start (JobProcessLauncher throws) or
	//finish on the child's Exited thread; this one does either on demand.
	private sealed class ScriptedChecker : ICheatWebChecker
	{
		private readonly Func<Task<IReadOnlyList<WebFoundCode>>> _answer;

		public ScriptedChecker(Func<Task<IReadOnlyList<WebFoundCode>>> answer)
		{
			_answer = answer;
		}

		public Task<IReadOnlyList<WebFoundCode>> LookUpAsync(string romPath, string gameName)
		{
			return _answer();
		}
	}

	private static (Window Window, PlayerCheatsSheetViewModel Model, List<IReadOnlyList<StoredCheat>> Saves) Show(FakeChecker checker, IReadOnlyList<CheatDbGame> db)
	{
		return Show((ICheatWebChecker)checker, db);
	}

	private static (Window Window, PlayerCheatsSheetViewModel Model, List<IReadOnlyList<StoredCheat>> Saves) Show(ICheatWebChecker checker, IReadOnlyList<CheatDbGame> db)
	{
		PlayerCheatsSheetViewModel model = new();
		List<IReadOnlyList<StoredCheat>> saves = new();
		model.ConfigureWebLookup(() => Task.FromResult<ICheatWebChecker?>(checker));
		model.Open(ConsoleType.Nes, Sha1, db, Array.Empty<StoredCheat>(), false, false, saves.Add, gameName: "Castlevania", romPath: "/roms/Castlevania.nes");
		Window window = new() { Content = new PlayerCheatsSheetView { DataContext = model } };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return (window, model, saves);
	}

	private static T Named<T>(Window window, string name) where T : Control
	{
		return window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
	}

	private static async Task Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		for(int i = 0; i < 5; i++) {
			await Task.Yield();
			Dispatcher.UIThread.RunJobs();
		}
	}

	[AvaloniaFact]
	public async Task Look_online_lists_only_the_checked_code_with_its_label_and_toggles_it()
	{
		FakeChecker checker = new();
		(Window window, PlayerCheatsSheetViewModel model, List<IReadOnlyList<StoredCheat>> saves) = Show(checker, Array.Empty<CheatDbGame>());
		try {
			Button lookOnline = Named<Button>(window, "CheatsLookOnline");
			Assert.True(lookOnline.IsEffectivelyVisible);

			await Click(lookOnline);

			Assert.Equal(1, checker.Runs);
			Assert.Equal("/roms/Castlevania.nes", checker.RomPath);
			CheckBox row = Assert.Single(Named<ItemsControl>(window, "CheatsList").GetVisualDescendants().OfType<CheckBox>());
			Assert.Equal("Infinite lives", row.Content);
			Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "found online, checked on your copy" && t.IsEffectivelyVisible);
			Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "Max power" or "Moon jump");
			Assert.Equal("0 on · codes found online, checked on your copy", Named<TextBlock>(window, "CheatsStatusLine").Text);

			row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			Dispatcher.UIThread.RunJobs();

			Assert.Equal(new StoredCheat("Infinite lives", CheatType.NesCustom, "0032:09", true), Assert.Single(Assert.Single(saves)));
			Assert.Equal(1, model.CountOn);
			Assert.Equal("1 on · codes found online, checked on your copy", Named<TextBlock>(window, "CheatsStatusLine").Text);
		} finally {
			window.Close();
		}
	}

	[AvaloniaFact]
	public void A_copy_in_the_bundled_list_is_not_offered_the_web_lookup()
	{
		FakeChecker checker = new();
		CheatDbGame listed = new("Castlevania (USA)", Sha1, new[] { new CheatDbCode("Infinite hearts", "0071:63") });
		(Window window, PlayerCheatsSheetViewModel _, _) = Show(checker, new[] { listed });
		try {
			Assert.False(Named<Button>(window, "CheatsLookOnline").IsEffectivelyVisible);
			Assert.Equal(0, checker.Runs);
		} finally {
			window.Close();
		}
	}

	//#949 review: a run that throws (the launcher's IOException) must not leave
	//the button disabled and the bar moving forever.
	[AvaloniaFact]
	public async Task A_lookup_that_throws_ends_the_wait_and_says_the_check_did_not_run()
	{
		ScriptedChecker checker = new(() => throw new IOException("No such file"));
		(Window window, PlayerCheatsSheetViewModel model, _) = Show(checker, Array.Empty<CheatDbGame>());
		try {
			Button lookOnline = Named<Button>(window, "CheatsLookOnline");

			await Click(lookOnline);

			Assert.False(model.IsWebSearching);
			Assert.True(lookOnline.IsEffectivelyEnabled);
			Assert.False(Named<ProgressBar>(window, "CheatsWebBar").IsEffectivelyVisible);
			Assert.Equal(CheatWebLookup.FailedLine, Named<TextBlock>(window, "CheatsWebLine").Text);
		} finally {
			window.Close();
		}
	}

	//#949 review: the answer can come back on a thread-pool thread with no
	//synchronization context; the sheet's state still changes on the UI thread.
	[AvaloniaFact]
	public async Task A_lookup_finishing_off_the_ui_thread_changes_the_sheet_on_the_ui_thread()
	{
		ScriptedChecker checker = new(async () => {
			await Task.Delay(10).ConfigureAwait(false);
			return new[] { new WebFoundCode("Infinite lives", "0032:09", WebCheckState.Passed) };
		});
		(Window window, PlayerCheatsSheetViewModel model, _) = Show(checker, Array.Empty<CheatDbGame>());
		try {
			List<(string Name, bool Searching, bool OnUi)> changes = new();
			model.PropertyChanged += (_, e) => {
				lock(changes) {
					changes.Add((e.PropertyName ?? "", model.IsWebSearching, Dispatcher.UIThread.CheckAccess()));
				}
			};

			//Started off the UI thread too, so no context brings the await back.
			await Task.Run(() => model.LookOnline()).WaitAsync(TimeSpan.FromSeconds(10));
			for(int i = 0; i < 50 && model.IsWebSearching; i++) {
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
			}

			Assert.False(model.IsWebSearching);
			lock(changes) {
				//Everything from the end of the wait on is the completion.
				int done = changes.FindIndex(c => c.Name == nameof(model.IsWebSearching) && !c.Searching);
				Assert.True(done >= 0);
				Assert.All(changes.Skip(done), c => Assert.True(c.OnUi, $"{c.Name} changed off the UI thread"));
			}
		} finally {
			window.Close();
		}
	}
}
