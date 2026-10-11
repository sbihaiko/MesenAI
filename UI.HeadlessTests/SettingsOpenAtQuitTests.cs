using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#1484: a Home batch that ends with Settings open (the pad's Y) exited -11.
//Every crash report is the same frame: MesenCore SetVideoConfig reading the
//emulator's settings at 0xd8, i.e. through a null _emu. The Settings sheet's
//Display page observes the video config and re-applies it on every change; the
//quit's own writes (window settings, Config.Save) came after EmuApi.Release,
//so a still-open sheet pushed the config into a core that was already gone.
//The exit path must let the sheet go while the core is still alive.
[Collection(NativeCoreCollection.Name)]
public class SettingsOpenAtQuitTests
{
	[AvaloniaFact]
	public void QuitWithSettingsOpen_ClosesTheSheetBeforeTheCoreIsReleased()
	{
		if(!NativeCore.IsAvailable) {
			return;
		}
		MainWindow window = new();
		window.ShowStarted();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		window.OpenPlayerSettingsSheet();
		Assert.NotNull(model.PlayerSettings);

		bool sheetOpenAtRelease = true;
		window.ReleaseCore = () => { sheetOpenAtRelease = model.PlayerSettings != null; };
		window.SkipCloseConfirmation = true;
		window.Close();
		Dispatcher.UIThread.RunJobs();

		Assert.False(sheetOpenAtRelease, "Settings was still open when the core was released: its video-config observer would call SetVideoConfig into a null emulator (#1484).");
	}
}
