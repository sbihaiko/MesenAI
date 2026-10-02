using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.ViewModels;

namespace Mesen.Views
{
	//G.2 (W-P1/W-P2): thin code-behind - Open a ROM… reuses the existing
	//Open-ROM shortcut/dialog (ShortcutHandler.OpenFile), Continue resumes the
	//most recent game exactly like clicking its tile.
	public class PlayHomeView : UserControl
	{
		private RecentGamesViewModel? _observed;

		public PlayHomeView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private RecentGamesViewModel? Model => DataContext as RecentGamesViewModel;

		protected override void OnDataContextChanged(System.EventArgs e)
		{
			base.OnDataContextChanged(e);
			if(_observed != null) {
				_observed.PropertyChanged -= OnModelPropertyChanged;
			}
			_observed = Model;
			if(_observed != null) {
				_observed.PropertyChanged += OnModelPropertyChanged;
			}
			FocusPrimary();
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			FocusPrimary();
		}

		//#625: Visible too - the home also comes back without a home-kind change
		//(Quit game with recents already listed, a DisplayMessageHelper message
		//that hid it), after GameLoaded moved focus to RendererPanel.
		private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if(e.PropertyName == nameof(RecentGamesViewModel.ShowFirstRunHome) || e.PropertyName == nameof(RecentGamesViewModel.ShowRecentsHome) || e.PropertyName == nameof(RecentGamesViewModel.Visible)) {
				FocusPrimary();
			}
		}

		//Keyboard/gamepad (rule 9): the screen's primary action has focus when
		//the home appears, so A/Enter acts without a pointer. Posted after the
		//StateGrid's own attach-time Focus() and the layout pass (Focus() on a
		//not-yet-visible control is a no-op).
		private void FocusPrimary()
		{
			Dispatcher.UIThread.Post(() => {
				if(!IsEffectivelyVisible || AnotherSurfaceHasFocus()) {
					return;
				}
				if(Model?.ShowFirstRunHome == true) {
					this.FindControl<Button>("PlayHomeOpenRomPrimary")?.Focus();
				} else if(Model?.ShowRecentsHome == true) {
					this.FindControl<Button>("PlayHomeContinueButton")?.Focus();
				}
			});
		}

		//A picker, the pause overlay or a sheet painted over the home already
		//focused its own first control (MainWindow); the home must not steal it.
		//Nothing focused, the renderer panel or a StateGrid is fair game - in
		//this window only: keyboard focus is one for the app, so the home coming
		//back while another window (a debugger, a tool) has it leaves it there.
		private bool AnotherSurfaceHasFocus()
		{
			TopLevel? topLevel = TopLevel.GetTopLevel(this);
			object? focused = topLevel?.FocusManager?.GetFocusedElement();
			if(focused is Visual elsewhere && TopLevel.GetTopLevel(elsewhere) != topLevel) {
				return true;
			}
			return focused is Button or CheckBox or TextBox && focused is Visual visual && !this.IsVisualAncestorOf(visual);
		}

		private void OnOpenRom(object? sender, RoutedEventArgs e)
		{
			EmuApi.ExecuteShortcut(new ExecuteShortcutParams() { Shortcut = EmulatorShortcut.OpenFile });
		}

		//G.5 (W-P14): the alert's next step is the same Open a ROM… dialog.
		private void OnOpenAnother(object? sender, RoutedEventArgs e)
		{
			Model?.OnOpenStarted();
			OnOpenRom(sender, e);
		}

		private void OnDismissLoadAlert(object? sender, RoutedEventArgs e) => Model?.DismissLoadAlert();

		private void OnContinue(object? sender, RoutedEventArgs e)
		{
			if(Model?.GameEntries.Count > 0) {
				Model.GameEntries[0].Load();
			}
		}
	}
}
