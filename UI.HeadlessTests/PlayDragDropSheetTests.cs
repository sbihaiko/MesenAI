using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Interop;
using Mesen.Services;
using Mesen.ViewModels;
using Mesen.Views;
using Xunit;
using Xunit.Sdk;

namespace Mesen.HeadlessTests;

//#953: a file dropped on the Player's two drop sheets reaches the path the
//sheet's own Choose File… goes to - W-P16's pack file is checked and copied
//into the pack's drop folder, W-P13's BIOS is read and offered for use. The
//rules (hash match, target name, size check) are pinned host-free in
//UI.Tests; this checks the drop crossing PlayPackDepSheetView/PlayBiosSheetView.
//Core-free: no MainWindow, no EmuApi, and every path is under this case's temp
//folder, so it runs on Linux CI.
public class PlayDragDropSheetTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-953-" + Guid.NewGuid().ToString("N"));
	private Window? _window;

	public PlayDragDropSheetTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		_window?.Close();
		try {
			Directory.Delete(_folder, true);
		} catch {
			//The temp folder going is not what the case was proving.
		}
	}

	private Window Show(Control view)
	{
		_window = new Window { Content = view, Width = 1000, Height = 700 };
		_window.Show();
		Dispatcher.UIThread.RunJobs();
		return _window;
	}

	private static void WaitFor(Func<bool> condition, string failure)
	{
		DateTime until = DateTime.UtcNow.AddSeconds(10);
		while(!condition()) {
			if(DateTime.UtcNow > until) {
				throw new XunitException(failure);
			}
			Dispatcher.UIThread.RunJobs();
			Thread.Sleep(10);
		}
	}

	[AvaloniaFact]
	public void A_pack_file_dropped_on_the_sheet_is_copied_into_the_packs_drop_folder()
	{
		byte[] content = SyntheticNrom.Build();
		string dropFolder = Path.Combine(_folder, "pack", "deps");
		string dropped = Path.Combine(_folder, "Contra (USA).nes");
		File.WriteAllBytes(dropped, content);
		PlayPackDepSheetViewModel sheet = new();
		sheet.SetPending("Contra Remastered", new[] {
			new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", dropFolder, Convert.ToHexString(SHA256.HashData(content)))
		});
		sheet.Open();
		int added = 0;
		sheet.FileAdded += () => added++;
		Window window = Show(new PlayPackDepSheetView { DataContext = sheet });
		Assert.True(window.FindNamed<Panel>("PackDepSheetBackdrop").IsOnScreen());

		PlayDragDrop.DropFile(window, window.FindNamed<Button>("PackDepSheetDropZone"), dropped);
		WaitFor(() => added == 1, "the dropped file never reached the pack's add path");

		//The window closes the sheet and reloads the game on FileAdded.
		Assert.Equal(content, File.ReadAllBytes(Path.Combine(dropFolder, "Contra (USA).nes")));
	}

	[AvaloniaFact]
	public void A_wrong_file_dropped_on_the_pack_sheet_is_refused_on_the_sheet()
	{
		string dropFolder = Path.Combine(_folder, "pack", "deps");
		string dropped = Path.Combine(_folder, "wrong.nes");
		File.WriteAllBytes(dropped, new byte[] { 1, 2, 3 });
		PlayPackDepSheetViewModel sheet = new();
		sheet.SetPending("Contra Remastered", new[] {
			new CommunityPackDepPrompt("contra-usa", "Contra (USA).nes", "", dropFolder, Convert.ToHexString(SHA256.HashData(new byte[] { 9 })))
		});
		sheet.Open();
		Window window = Show(new PlayPackDepSheetView { DataContext = sheet });
		Assert.False(window.FindNamed<Border>("PackDepSheetError").IsOnScreen());

		PlayDragDrop.DropFile(window, window.FindNamed<Button>("PackDepSheetDropZone"), dropped);
		WaitFor(() => window.FindNamed<Border>("PackDepSheetError").IsOnScreen(), "the dropped file was never checked");

		Assert.False(Directory.Exists(dropFolder) && Directory.GetFiles(dropFolder).Length > 0);
		Assert.True(window.FindNamed<Panel>("PackDepSheetBackdrop").IsOnScreen());
	}

	//A file of the size the core asked for, but not a known dump: the drop reads
	//and hashes it and asks before using it (W-X1) - the same path as Choose File….
	[AvaloniaFact]
	public void A_bios_file_dropped_on_the_sheet_is_read_and_offered_for_use()
	{
		string dropped = Path.Combine(_folder, "dmg_boot.bin");
		File.WriteAllBytes(dropped, new byte[256]);
		PlayBiosSheetViewModel sheet = new();
		Task<bool> request = sheet.Request(FirmwareType.Gameboy, "dmg_boot.bin", 256, 0, "Tetris (World)");
		Window window = Show(new PlayBiosSheetView { DataContext = sheet });
		Assert.False(window.FindNamed<Control>("BiosSheetConfirm").IsOnScreen());

		PlayDragDrop.DropFile(window, window.FindNamed<Button>("BiosSheetDropZone"), dropped);

		Assert.True(window.FindNamed<Control>("BiosSheetConfirm").IsOnScreen());
		Assert.True(window.FindNamed<Button>("BiosSheetUseIt").IsOnScreen());
		Assert.False(request.IsCompleted);
		sheet.Cancel();
		Assert.False(request.Result);
	}

	[AvaloniaFact]
	public void A_bios_file_of_the_wrong_size_dropped_on_the_sheet_says_so()
	{
		string dropped = Path.Combine(_folder, "short.bin");
		File.WriteAllBytes(dropped, new byte[100]);
		PlayBiosSheetViewModel sheet = new();
		_ = sheet.Request(FirmwareType.Gameboy, "dmg_boot.bin", 256, 0, "Tetris (World)");
		Window window = Show(new PlayBiosSheetView { DataContext = sheet });
		Assert.False(window.FindNamed<Control>("BiosSheetError").IsOnScreen());

		PlayDragDrop.DropFile(window, window.FindNamed<Button>("BiosSheetDropZone"), dropped);

		Assert.True(window.FindNamed<Control>("BiosSheetError").IsOnScreen());
		Assert.False(window.FindNamed<Control>("BiosSheetConfirm").IsOnScreen());
		sheet.Cancel();
	}
}
