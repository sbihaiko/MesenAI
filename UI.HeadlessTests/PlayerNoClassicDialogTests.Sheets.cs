using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.GUI.Utilities;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0249 (user decision 2026-10-03, "Virar sheets"): in Player mode a
//multi-ROM archive and Look's Adjust… are sheets inside the main window, not
//windows; Advanced keeps SelectRomWindow and ShaderConfigWindow.
public partial class PlayerNoClassicDialogTests
{
	private string ArchiveWithTwoGames()
	{
		string zip = Path.Combine(Scratch(), "Contra Collection.zip");
		using(ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
			foreach(string name in new[] { "Contra (USA).nes", "Super C (USA).nes" }) {
				using Stream entry = archive.CreateEntry(name).Open();
				entry.Write(SyntheticNrom.Build());
			}
		}
		return zip;
	}

	private static Button[] VisibleRows(Visual root) => root.FindNamed<ItemsControl>("SelectRomSheetList")
		.FindAll<Button>().Where(b => b.Classes.Contains("row") && b.IsOnScreen()).ToArray();

	//The W-P5 sheet shape in the main window: the heading names the archive,
	//the games are Button.row rows on an inset list, Cancel; a row opens its game.
	[AvaloniaFact]
	public void An_archive_with_two_games_asks_on_a_sheet_in_the_main_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		UseAsMainWindow(window);
		string zip = ArchiveWithTwoGames();

		Task<ResourcePath?> pick = SelectRomWindow.Show(zip);
		Dispatcher.UIThread.RunJobs();

		Assert.Empty(window.OwnedWindows);
		Assert.True(model.SelectRomSheet.IsVisible, "the archive did not open the in-window sheet");
		Border sheet = window.FindNamed<Border>("SelectRomSheet");
		Assert.True(sheet.IsOnScreen());
		Assert.Contains("sheet", sheet.Classes);
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		TextBlock title = window.FindNamed<TextBlock>("SelectRomSheetTitle");
		Assert.Equal("Choose a game from Contra Collection.zip", title.Text);
		Assert.Equal("Inter", title.FontFamily.Name);
		Border inset = window.FindNamed<Border>("SelectRomSheetInset");
		Assert.Equal(Color.Parse("#F8F8FA"), PlayerRender.SolidColor(inset.Background));
		Assert.Equal(new CornerRadius(12), inset.CornerRadius);
		Button[] rows = VisibleRows(window);
		Assert.Equal(new[] { "Contra (USA).nes", "Super C (USA).nes" }, rows.Select(r => LabelOf(r).Text).ToArray());
		AssertPlayerButton(window.FindNamed<Button>("SelectRomSheetCancel"), Card);
		Assert.Equal("Cancel", LabelOf(window.FindNamed<Button>("SelectRomSheetCancel")).Text);
		SaveRender(window, "W-P5-select-rom-sheet");

		//The search narrows the rows; a row opens its game.
		model.SelectRomSheet.SearchString = "super";
		Dispatcher.UIThread.RunJobs();
		rows = VisibleRows(window);
		Assert.Single(rows);
		Click(rows[0]);
		WaitFor(() => pick.IsCompleted, "a row did not answer the open");
		Assert.False(model.SelectRomSheet.IsVisible);
		Assert.Equal(zip, pick.Result?.Path);
		Assert.Equal("Super C (USA).nes", pick.Result?.InnerFile);
	}

	//Cancel and Esc open nothing; Esc goes through the shortcut handler, as the
	//Core delivers it, and works outside Play too (an open from Remaster).
	[AvaloniaFact]
	public void The_archive_sheet_cancels_with_cancel_or_esc_in_any_workspace()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		UseAsMainWindow(window);
		string zip = ArchiveWithTwoGames();

		Task<ResourcePath?> cancelled = SelectRomWindow.Show(zip);
		Dispatcher.UIThread.RunJobs();
		Click(window.FindNamed<Button>("SelectRomSheetCancel"));
		WaitFor(() => cancelled.IsCompleted, "Cancel did not answer the open");
		Assert.Null(cancelled.Result);
		Assert.False(model.SelectRomSheet.IsVisible);

		model.SelectWorkspace(Workspace.Remaster);
		Dispatcher.UIThread.RunJobs();
		Task<ResourcePath?> escaped = SelectRomWindow.Show(zip);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("SelectRomSheet").IsOnScreen(), "the archive sheet is not on screen over Remaster");
		new ShortcutHandler(window).ExecuteShortcut(EmulatorShortcut.ToggleOverlay);
		WaitFor(() => escaped.IsCompleted, "Esc did not answer the open");
		Assert.Null(escaped.Result);
		Assert.Equal(Workspace.Remaster, ConfigManager.Config.Preferences.Workspace);
		Assert.Empty(window.OwnedWindows);
	}

	//Advanced keeps the classic window.
	[AvaloniaFact]
	public void An_archive_in_advanced_mode_keeps_the_classic_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Advanced);
		UseAsMainWindow(window);

		Task<ResourcePath?> pick = SelectRomWindow.Show(ArchiveWithTwoGames());
		SelectRomWindow dialog = OwnedDialog<SelectRomWindow>(window);
		Assert.False(model.SelectRomSheet.IsVisible);
		Assert.True(dialog.FindNamed<ListBox>("ListBox").IsOnScreen());
		dialog.Close();
		WaitFor(() => pick.IsCompleted, "closing the classic window did not answer the open");
		Assert.Null(pick.Result);
	}

	//A one-pass preset, set as the current shader (Adjust… needs a file).
	private string SyntheticPreset(string name)
	{
		string folder = Scratch();
		File.WriteAllText(Path.Combine(folder, name + ".slang"),
			"#version 450\n" +
			"#pragma parameter NOCL_BRIGHT \"Brightness\" 1.0 0.0 2.0 0.05\n" +
			"#pragma stage vertex\nvoid main() {}\n#pragma stage fragment\nvoid main() {}\n");
		string preset = Path.Combine(folder, name + ".slangp");
		File.WriteAllText(preset, "shaders = 1\nshader0 = " + name + ".slang\n");
		ConfigManager.Config.Video.ShaderFile = preset;
		return preset;
	}

	private static ShaderConfigViewModel ShaderWithThreeParams(string file)
	{
		return new ShaderConfigViewModel(false, "") {
			Config = new ShaderConfig {
				ShaderFile = file,
				Params = new() {
					new ShaderParam { Name = "NOCL_BRIGHT", Description = "Brightness", Min = 0, Max = 2, Step = 0.05m, Initial = 1, Value = 1 },
					new ShaderParam { Name = "NOCL_GLOW", Description = "Glow", Min = 0, Max = 1, Step = 1, Initial = 0, Value = 1 },
					new ShaderParam { Name = "NOCL_MASK", Description = "Mask strength", Min = 0, Max = 1, Step = 0.1m, Initial = 0.3m, Value = 0.3m },
				},
			},
		};
	}

	//W-P10's Adjust… in Player mode: a sheet over Settings in the main window -
	//the shader's file as the heading, the parameters on an inset list, Reset
	//on the left, Cancel then OK on the right. Esc returns to Settings.
	[AvaloniaFact]
	public void Looks_adjust_opens_the_shader_parameters_on_a_sheet()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player);
		string preset = SyntheticPreset("nocl-test");
		LookConfigView look = ShowLookSettings(window, model);

		Invoke(look, "OnAdjust");

		Assert.Empty(window.OwnedWindows);
		Assert.True(model.IsShaderSheetVisible, "Adjust… did not open the in-window sheet");
		Assert.True(model.IsPlayerSettingsVisible, "the Settings sheet should stay under the shader sheet");
		Border sheet = window.FindNamed<Border>("ShaderSheet");
		Assert.True(sheet.IsOnScreen());
		Assert.Equal(Card, PlayerRender.SolidColor(sheet.Background));
		TextBlock title = window.FindNamed<TextBlock>("ShaderSheetTitle");
		Assert.Equal("nocl-test.slangp", title.Text);
		Assert.Equal("Inter", title.FontFamily.Name);
		Border list = window.FindNamed<Border>("ShaderSheetList");
		Assert.Equal(Color.Parse("#F8F8FA"), PlayerRender.SolidColor(list.Background));
		Assert.Equal(new CornerRadius(12), list.CornerRadius);
		Button cancel = window.FindNamed<Button>("ShaderSheetCancel");
		Button ok = window.FindNamed<Button>("ShaderSheetOk");
		Assert.True(cancel.Bounds.X < ok.Bounds.X, "Cancel should come before OK");
		AssertPlayerButton(cancel, Card);
		AssertPlayerButton(ok, PlayTint);
		AssertPlayerButton(window.FindNamed<Button>("ShaderSheetReset"), Card);

		model.TogglePlayerOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsShaderSheetVisible, "Esc did not close the shader sheet");
		Assert.True(model.IsPlayerSettingsVisible, "Esc on the shader sheet closed Settings too");

		//The rows, from parameters given directly (librashader does not parse a
		//synthetic preset in every environment): a stepper, a switch and a
		//third row, in Inter. Cancel goes back to Settings.
		model.OpenShaderSheet(ShaderWithThreeParams(preset));
		Dispatcher.UIThread.RunJobs();
		TextBlock[] names = window.FindAll<TextBlock>().Where(t => t.Classes.Contains("shaderParam") && t.IsOnScreen()).ToArray();
		Assert.Equal(new[] { "Brightness", "Glow", "Mask strength" }, names.Select(t => t.Text).ToArray());
		Assert.All(names, t => Assert.Equal("Inter", t.FontFamily.Name));
		Assert.Single(window.FindNamed<ItemsControl>("ShaderSheetParams").FindAll<CheckBox>(), c => c.IsOnScreen() && c.Classes.Contains("switch"));
		SaveRender(window, "W-P10-adjust-sheet");
		Click(window.FindNamed<Button>("ShaderSheetCancel"));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsShaderSheetVisible);
		Assert.True(model.IsPlayerSettingsVisible);
		model.ClosePlayerSettings();
	}

	//Advanced's Look (the Options window) keeps the classic window.
	[AvaloniaFact]
	public void Looks_adjust_in_the_options_window_keeps_the_classic_window()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, MainWindowViewModel model) = Show(UiMode.Advanced);
		SyntheticPreset("nocl-classic");
		ConfigWindow options = new(ConfigWindowTab.Look);
		options.Show();
		Dispatcher.UIThread.RunJobs();
		LookConfigView look = options.FindAll<LookConfigView>().First(v => v.IsOnScreen());

		Invoke(look, "OnAdjust");

		ShaderConfigWindow classic = OwnedDialog<ShaderConfigWindow>(options);
		Assert.False(model.IsShaderSheetVisible);
		Assert.DoesNotContain(classic.FindAll<Control>(), c => c.Classes.Contains("player"));
		classic.Close();
		options.Close();
	}

	//The native picture is drawn above every Avalonia control (macOS): both
	//sheets hide it while they are up (PlayGameLayer, IsPlaySurfaceOverGame).
	[AvaloniaFact]
	public void Both_sheets_hide_the_native_picture()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(_, MainWindowViewModel model) = Show(UiMode.Player);
		model.RecentGames.Visible = false;
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsNativeRendererVisible);

		Task<PlaySelectRomRow?> pick = model.SelectRomSheet.Request("Contra Collection.zip", new List<ArchiveRomEntry> {
			new() { Filename = "Contra (USA).nes", IsUtf8 = true }, new() { Filename = "Super C (USA).nes", IsUtf8 = true }
		});
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsNativeRendererVisible, "the archive sheet stayed under the game picture");
		model.SelectRomSheet.Cancel();
		Dispatcher.UIThread.RunJobs();
		Assert.True(pick.IsCompleted);
		Assert.True(model.IsNativeRendererVisible);

		model.OpenShaderSheet(ShaderWithThreeParams(SyntheticPreset("nocl-layer")));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsNativeRendererVisible, "the shader sheet stayed under the game picture");
		model.CloseShaderSheet(false);
		Dispatcher.UIThread.RunJobs();
		Assert.True(model.IsNativeRendererVisible);
	}
}
