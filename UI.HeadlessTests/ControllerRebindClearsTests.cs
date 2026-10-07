using System;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#941 (ADR-0255 slice 3's carried limit): binding a pad button to a control in
//the sheet's REMAP mode takes it off every other control of the same port that
//had it, so one press no longer fires two controls, and the note names the
//control that lost it. Which controls are displaced is host-free
//(UI.Tests/Play/ControllerSheetRemapTests); this pins the write - the cleared
//fields land in the port's own KeyMapping slots through ConfigManager, other
//ports and keyboard keys stay as they were - and the note the player reads.
//
//Needs a MainWindow (EmuApi.InitDll) and the write calls ApplyConfig(), so it
//self-skips without a built core, like PlayerControllerSheetTests.
[Collection(NativeCoreCollection.Name)]
public class ControllerRebindClearsTests : IDisposable
{
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly NesConfig _nes = ConfigManager.Config.Nes.Clone();
	private bool _wasPaused = true;

	public void Dispose()
	{
		ConfigManager.Config.Nes = _nes;
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		if(NativeCore.IsAvailable && !_wasPaused) {
			EmuApi.Resume();
		}
	}

	private static ushort PadButton(int device, int button) => (ushort)(ControllerDevices.BaseGamepadIndex + device * 0x100 + button);

	//The Play window over a paused NES game, the sheet open on pad 0, its poll
	//stopped so only the test ticks it.
	private ControllerSheetViewModel OpenSheet()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new() { Width = 1100, Height = 740 };
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		_wasPaused = EmuApi.IsPaused();
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<Button>("OverlaySettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<TabControl>("PlayerSettingsTabs").SelectedIndex = PlayerSettingsEssentials.IndexOf(ConfigWindowTab.Input);
		Dispatcher.UIThread.RunJobs();
		window.FindNamed<Button>("btnPlayerSettingsMoreInOptions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		window.UpdateLayout();
		Dispatcher.UIThread.RunJobs();

		ControllerSheetViewModel sheet = model.ControllerSheet;
		((DispatcherTimer?)typeof(ControllerSheetViewModel).GetField("_poll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sheet))!.Stop();
		sheet.CurrentConsole = () => ConsoleType.Nes;
		return sheet;
	}

	private static void ShowPadZero(ControllerSheetViewModel sheet)
	{
		sheet.Tester.Gamepads.Add(new GamepadTestItem(0) { Name = "Pad Zero", Backend = GamepadBackend.GameController });
		sheet.PressedKeys = () => Array.Empty<ushort>();
		sheet.ApplyPad();
	}

	//Arm `button`, release, press `code`: the capture binds it.
	private static void Rebind(ControllerSheetViewModel sheet, SetupButton button, ushort code)
	{
		sheet.ArmRemap(button);
		sheet.PressedKeys = () => new ushort[] { code };
		sheet.RefreshRemap();
		Assert.False(sheet.IsCapturing);
	}

	[AvaloniaFact]
	public void Binding_a_button_another_control_holds_moves_it_and_names_that_control()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ControllerSheetViewModel sheet = OpenSheet();

		ushort x = PadButton(0, 2);
		const ushort keyboardKey = 0x2C;
		ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(0, 3);
		ConfigManager.Config.Nes.Port1.Mapping1.Start = x;
		ConfigManager.Config.Nes.Port1.Mapping1.Select = keyboardKey;
		//The other player's port binds the same code: a different port, so it stays.
		ConfigManager.Config.Nes.Port2.Mapping1.Start = x;
		ShowPadZero(sheet);

		Rebind(sheet, SetupButton.B, x);

		NesControllerConfig port1 = ConfigManager.Config.Nes.Port1;
		Assert.Contains(Enumerable.Range(0, 4), slot => ControllerSheetSlotWrite.Slot(port1, slot).B == x);
		Assert.Equal(0, port1.Mapping1.Start);
		Assert.Equal(PadButton(0, 3), port1.Mapping1.A);
		Assert.Equal(keyboardKey, port1.Mapping1.Select);
		Assert.Equal(x, ConfigManager.Config.Nes.Port2.Mapping1.Start);
		Assert.Contains("Start", sheet.RemapNote);
		Assert.Contains("unbound", sheet.RemapNote);
	}

	//The port's four slots are alternatives for one player, so the button comes
	//off another control in any of them, not only in the slot the bind lands in.
	[AvaloniaFact]
	public void The_button_comes_off_another_control_in_another_slot_of_the_port()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ControllerSheetViewModel sheet = OpenSheet();

		ushort x = PadButton(0, 4);
		ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port1.Mapping1.A = PadButton(0, 3);
		ConfigManager.Config.Nes.Port1.Mapping2.Select = x;
		ShowPadZero(sheet);

		Rebind(sheet, SetupButton.A, x);

		NesControllerConfig port1 = ConfigManager.Config.Nes.Port1;
		Assert.Equal(x, port1.Mapping1.A);
		Assert.Equal(0, port1.Mapping2.Select);
		Assert.Contains("Select", sheet.RemapNote);
	}

	//A button nothing else held is a plain bind, and the note says only that.
	[AvaloniaFact]
	public void A_button_no_other_control_holds_moves_nothing()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ControllerSheetViewModel sheet = OpenSheet();

		ushort x = PadButton(0, 5);
		ConfigManager.Config.Nes.Port1 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port2 = new NesControllerConfig();
		ConfigManager.Config.Nes.Port1.Mapping1.Start = PadButton(0, 6);
		ShowPadZero(sheet);

		Rebind(sheet, SetupButton.B, x);

		Assert.Equal(PadButton(0, 6), ConfigManager.Config.Nes.Port1.Mapping1.Start);
		Assert.DoesNotContain("unbound", sheet.RemapNote);
	}
}
