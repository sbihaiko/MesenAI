using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;

namespace Mesen.HeadlessTests;

//#953: the hand-offs that leave the app from the Player's core-free sheets -
//W-P16's Show Folder and the tool sheet's release page - pass the folder or
//URL their button names to the injected launcher, and only on the click. The
//launchers are stubs that record what they were handed, so nothing opens.
//PlayFolderHandoffCoreTests covers the surfaces that need the core.
public class PlayFolderHandoffTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-953-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> _launched = new();
	private Window? _window;

	public void Dispose()
	{
		_window?.Close();
		try {
			Directory.Delete(_folder, true);
		} catch {
			//The temp folder going is not what the case was proving.
		}
	}

	private void Show(Control view)
	{
		_window = new Window { Content = view, Width = 1000, Height = 700 };
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(Button button)
	{
		button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
	}

	[AvaloniaFact]
	public void Show_folder_on_the_pack_file_sheet_hands_over_the_packs_drop_folder()
	{
		string dropFolder = Path.Combine(_folder, "Contra Remastered", "deps");
		PlayPackDepSheetViewModel sheet = new() { FolderLauncher = _launched.Add };
		sheet.SetPending("Contra Remastered", new[] {
			new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", dropFolder, "00")
		});
		sheet.Open();
		Show(new PlayPackDepSheetView { DataContext = sheet });
		Assert.Empty(_launched);

		Click(_window!.FindNamed<Button>("PackDepSheetShowFolder"));

		Assert.Equal(new[] { dropFolder }, _launched);
		//The folder is made first, so the file manager has something to open.
		Assert.True(Directory.Exists(dropFolder));
	}

	[AvaloniaFact]
	public void Open_releases_on_the_tool_sheet_hands_over_the_forks_release_page()
	{
		PlayerToolSheetViewModel sheet = new() { ReleasePageLauncher = _launched.Add };
		sheet.OpenCheckForUpdates();
		Show(new PlayerToolSheetView { DataContext = sheet });
		Assert.Empty(_launched);

		Click(_window!.FindNamed<Button>("ToolSheetOpenReleases"));

		Assert.Equal(new[] { "https://github.com/sbihaiko/MesenAI/releases" }, _launched);
		Assert.False(sheet.IsVisible);
	}
}
