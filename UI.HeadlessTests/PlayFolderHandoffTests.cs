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

//#953: the hand-off that leaves the app from the Player's core-free pack file
//sheet - W-P16's Show Folder - passes the folder its button names to the
//injected launcher, and only on the click. The launcher is a stub that records
//what it was handed, so nothing opens. PlayFolderHandoffCoreTests covers the
//surfaces that reach the core.
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

	private void Click(string name)
	{
		_window!.FindNamed<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

		Click("PackDepSheetShowFolder");

		Assert.Equal(new[] { dropFolder }, _launched);
		//The folder is made first, so the file manager has something to open.
		Assert.True(Directory.Exists(dropFolder));
	}
}
