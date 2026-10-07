using System;
using System.Collections.Generic;
using System.Linq;
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

	private static (Window Window, PlayerCheatsSheetViewModel Model, List<IReadOnlyList<StoredCheat>> Saves) Show(FakeChecker checker, IReadOnlyList<CheatDbGame> db)
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
}
