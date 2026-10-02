using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Views;

namespace Mesen.Windows
{
	//G.4 (PRD Part B §8, ADR-0241, §13.5.2 W-P5–W-P9): the window side of the
	//Play sheets. It owns the EmuApi reads the ViewModel is handed (pack list,
	//ROM sha1, folders, install record) and the jobs a sheet starts (Restore).
	public partial class MainWindow
	{
		//W-P8's Scale row: the renderer's height over the console's, in physical
		//pixels (0 before a game has drawn).
		public double CurrentScale
		{
			get
			{
				FrameInfo baseSize = EmuApi.GetBaseScreenSize();
				return baseSize.Height == 0 ? 0 : _rendererPanel.Bounds.Height * LayoutHelper.GetLayoutScale(this) / baseSize.Height;
			}
		}

		private void InitPlaySheets()
		{
			PlayerPackDetailSheetView detail = this.GetControl<PlayerPackDetailSheetView>("PlayerPackDetailHost");
			detail.ChangePackRequested += (_, _) => _model.ChangePackFromDetail(EmuApi.GetMepPackList());
			detail.RestoreRequested += (_, _) => RestorePackFromDetail();

			//W-P9: the auto-install's pill. Static events, so unsubscribe when
			//the window goes (headless tests open several MainWindows).
			Action<string> started = name => _model.OnPackInstallStarted(name);
			Action<bool, bool> finished = (installed, silent) => _model.OnPackInstallFinished(installed, silent);
			CommunityPackInstallService.InstallStarted += started;
			CommunityPackInstallService.InstallFinished += finished;
			Closed += (_, _) => {
				CommunityPackInstallService.InstallStarted -= started;
				CommunityPackInstallService.InstallFinished -= finished;
			};
		}

		//Window.GetControl only sees this window's own name scope; the sheets
		//are UserControls with their own, so their controls are found by walking
		//the visual tree.
		private Control? FindNamedDescendant(string name)
		{
			return this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);
		}

		//W-P5: focus the selected radio (the stored choice, else the first).
		private void FocusPackPickerChoice()
		{
			ItemsControl? list = FindNamedDescendant("PackPickerList") as ItemsControl;
			RadioButton[] choices = list?.GetVisualDescendants().OfType<RadioButton>().ToArray() ?? Array.Empty<RadioButton>();
			(choices.FirstOrDefault(c => c.IsChecked == true) ?? choices.FirstOrDefault())?.Focus();
		}

		//W-P4's Pack row: W-P5 for 2+ packs (even with a stored choice -
		//"changing the choice later"), else W-P6 inspects the one pack (or none).
		private void OnOverlayPack(object? sender, RoutedEventArgs e)
		{
			string romSha1 = EmuApi.GetMepRomSha1();
			string? installed = string.IsNullOrWhiteSpace(romSha1) ? null : CommunityPackInstallRegistry.Read(CommunityPackPaths.CacheRoot, romSha1)?.SourceSha256;
			_model.OpenPackFromOverlay(EmuApi.GetMepPackList(), romSha1, ConfigManager.EnhancementPackFolder, EmuApi.GetMepSiblingFolder(), installed);
		}

		//W-P8: Player mode's Settings is the essentials strip (Display, Look,
		//Audio, Controls), opened on Display; closing it returns to W-P4 while a
		//game is loaded (every sheet from W-P4 closes back to it).
		private void OnOverlaySettings(object? sender, RoutedEventArgs e)
		{
			_model.IsPlayerOverlayVisible = false;
			ConfigWindow wnd = ApplicationHelper.GetOrCreateUniqueWindow(this, () => new ConfigWindow(ConfigWindowTab.Display, playerMode: true));
			wnd.Closed += OnPlaySettingsClosed;
		}

		private void OnPlaySettingsClosed(object? sender, EventArgs e)
		{
			if(sender is ConfigWindow wnd) {
				wnd.Closed -= OnPlaySettingsClosed;
			}
			if(ConfigManager.Config.Preferences.UiMode == UiMode.Player && EmuApi.IsRunning()) {
				_model.OpenPauseOverlay();
			}
		}

		//W-P6's Restore (ADR-0147), after its one in-place confirm (rule 7): a
		//job with the W-P9 pill, never a window (rule 6). On success the game
		//restarts to load the restored files - the confirm said so.
		private async void RestorePackFromDetail()
		{
			try {
				(bool ok, string error) = await CommunityPackInstallService.RestoreInstalledPack();
				_model.RestoreFinished();
				if(!ok) {
					EmuApi.WriteLogEntry("[CommunityPack] W-P6 Restore failed: " + error);
					return;
				}
				ConfigManager.Config.EnhancementPacks.ApplyConfig();
				_model.IsPackDetailVisible = false;
				_model.IsPlayerOverlayVisible = false;
				LoadRomHelper.PowerCycle();
				EmuApi.Resume();
			} catch(Exception ex) {
				_model.RestoreFinished();
				EmuApi.WriteLogEntry("[CommunityPack] W-P6 Restore failed: " + ex.Message);
			}
		}
	}
}
